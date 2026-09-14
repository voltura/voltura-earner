using VolturaEarner.Ui;
using VolturaEarner.Features.Settings;

namespace VolturaEarner.Features.Tracking;

public sealed class TrackingSession
{
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, WorkEntry> _entries;
    private readonly Dictionary<DateOnly, WorkDay> _days;
    private long _regularTicks;
    private long _overtimeTicks;
    private Dictionary<string, decimal> _gross = new(StringComparer.Ordinal);
    private Dictionary<string, decimal> _overtimeGross = new(StringComparer.Ordinal);
    private DateOnly _summaryDate;
    private AppSettings _settings;
    private Guid? _active;
    private long _stamp;
    private DateTimeOffset _wall;
    public bool Running => _active.HasValue;
    public bool ForceOvertime { get; private set; }
    public long Revision { get; private set; }
    public bool NewDayPending { get; private set; }
    public bool OvertimePending { get; private set; }
    public bool DailyLimitReached { get; private set; }
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public AppSettings Settings => _settings;

    public TrackingSession(AppSettings settings, WorkData data, TimeProvider? clock = null)
    {
        settings.Validate();
        data.Validate();
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _entries = data.Entries.ToDictionary(e => e.Id);
        _days = data.Days.ToDictionary(d => d.Date);
        RefreshToday();
    }

    private void RefreshToday()
    {
        _summaryDate = Today;

        var entries = _entries.Values.Where(e => e.Date == _summaryDate).ToArray();

        _regularTicks = entries.Sum(e => e.RegularTicks);
        _overtimeTicks = entries.Sum(e => e.OvertimeTicks);
        _gross = entries.GroupBy(e => e.Currency).ToDictionary(g => g.Key, g => g.Sum(e => e.Earned), StringComparer.Ordinal);
        _overtimeGross = entries.GroupBy(e => e.Currency).ToDictionary(g => g.Key, g => g.Sum(e => e.OvertimeEarned), StringComparer.Ordinal);
    }

    public void Start(string task, bool? overtime = null)
    {
        lock (_sync)
        {
            var forceOvertime = overtime ?? ForceOvertime;

            if (forceOvertime && _settings.Overtime == OvertimePolicy.NotAllowed)
            {
                throw new InvalidOperationException(Strings.Current["NotAllowedStopAtLimit"]);
            }

            if (!_settings.Tasks.Contains(task, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(Strings.Current["SelectATaskFirst"]);
            }

            Pause();
            ForceOvertime = forceOvertime;
            NewDayPending = false;
            DailyLimitReached = false;

            if (_summaryDate != Today)
            {
                RefreshToday();
            }

            if (_regularTicks + _overtimeTicks >= TimeSpan.TicksPerDay)
            {
                throw new InvalidOperationException(Strings.Current["ThisDayAlreadyContains24HoursOfWork"]);
            }

            if (!_days.TryGetValue(Today, out var day))
            {
                day = new(Today, _settings.DailyHours, _settings.DailyCost, _settings.Currency);
                _days.Add(Today, day);
            }

            if (_settings.Overtime == OvertimePolicy.NotAllowed && _regularTicks + _overtimeTicks >= (long)(day.TargetHours * TimeSpan.TicksPerHour))
            {
                throw new InvalidOperationException(Strings.Current["DailyLimitReachedEnableOvertimeOrIncreaseTodaySHours"]);
            }

            var entry = new WorkEntry(Guid.NewGuid(), Today, task, _settings.Currency, _settings.HourlyRate, 0, 0, 0, 0, _settings.OvertimeRate);

            _entries.Add(entry.Id, entry);
            _gross.TryAdd(entry.Currency, 0);
            _active = entry.Id;
            _stamp = _clock.GetTimestamp();
            _wall = _clock.GetLocalNow();
            Revision++;
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            Settle();
            _active = null;
        }
    }

    public void Settle()
    {
        lock (_sync)
        {
            if (_active is not { } id)
            {
                if (_summaryDate != Today)
                {
                    RefreshToday();
                }

                return;
            }

            var now = _clock.GetLocalNow();
            var stamp = _clock.GetTimestamp();
            var elapsed = Math.Max(0, _clock.GetElapsedTime(_stamp, stamp).Ticks);
            var entry = _entries[id];
            var changedDay = DateOnly.FromDateTime(now.DateTime) != entry.Date;

            if (changedDay)
            {
                var utc = StartOfDayUtc(entry.Date.AddDays(1), _clock.LocalTimeZone);

                elapsed = Math.Min(elapsed, Math.Max(0, (utc - _wall.UtcDateTime).Ticks));
                _active = null;
                NewDayPending = true;
            }

            var day = _days[entry.Date];
            var worked = _regularTicks + _overtimeTicks;
            var regularLimit = (long)(day.TargetHours * TimeSpan.TicksPerHour);

            elapsed = Math.Min(elapsed, Math.Max(0, TimeSpan.TicksPerDay - worked));

            if (_settings.Overtime == OvertimePolicy.NotAllowed)
            {
                elapsed = Math.Min(elapsed, Math.Max(0, regularLimit - worked));

                if (worked + elapsed >= regularLimit)
                {
                    _active = null;
                    DailyLimitReached = true;
                }
            }

            var regular = ForceOvertime
                ? 0
                : Math.Min(elapsed, Math.Max(0, (long)(day.TargetHours * TimeSpan.TicksPerHour) - worked));
            var overtime = elapsed - regular;
            var updated = entry with
            {
                RegularTicks = entry.RegularTicks + regular,
                OvertimeTicks = entry.OvertimeTicks + overtime,
                Earned = ((entry.RegularTicks + regular) * entry.HourlyRate + (entry.OvertimeTicks + overtime) * entry.EffectiveOvertimeRate) / TimeSpan.TicksPerHour,
                OvertimeEarned = (entry.OvertimeTicks + overtime) * entry.EffectiveOvertimeRate / TimeSpan.TicksPerHour,
            };

            _entries[id] = updated;
            _regularTicks += regular;
            _overtimeTicks += overtime;
            _gross[entry.Currency] = _gross.GetValueOrDefault(entry.Currency) + updated.Earned - entry.Earned;
            _overtimeGross[entry.Currency] = _overtimeGross.GetValueOrDefault(entry.Currency) + updated.OvertimeEarned - entry.OvertimeEarned;

            if (overtime > 0 && !day.OvertimeNotified)
            {
                _days[entry.Date] = day with { OvertimeNotified = true };

                OvertimePending = true;
            }

            if (elapsed > 0)
            {
                Revision++;
            }

            if (worked + elapsed >= TimeSpan.TicksPerDay)
            {
                _active = null;
            }

            _stamp = stamp;
            _wall = now;

            if (changedDay)
            {
                RefreshToday();
            }
        }
    }

    private static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);

        if (zone.IsInvalidTime(midnight))
        {
            // Midnight can be skipped by a daylight-saving or date-line transition.
            var invalid = midnight.Ticks;
            var valid = midnight.AddDays(2).Ticks;

            while (valid - invalid > 1)
            {
                var middle = invalid + (valid - invalid) / 2;

                if (zone.IsInvalidTime(new DateTime(middle)))
                {
                    invalid = middle;
                }
                else
                {
                    valid = middle;
                }
            }

            midnight = new DateTime(valid);
        }

        return zone.IsAmbiguousTime(midnight)
            ? new DateTimeOffset(midnight, zone.GetAmbiguousTimeOffsets(midnight).Max()).UtcDateTime
            : TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }

    public void DismissNotices()
    {
        NewDayPending = false;
        OvertimePending = false;
        DailyLimitReached = false;
    }

    public void ApplySettings(AppSettings settings, bool preserveRunning = false)
    {
        settings.Validate();

        lock (_sync)
        {
            Settle();

            var resume = preserveRunning && Running;

            Pause();
            _settings = settings;

            if (resume)
            {
                Start(settings.SelectedTask);
            }
        }
    }

    public void Replace(WorkData draft)
    {
        draft.Validate();

        lock (_sync)
        {
            if (Running)
            {
                throw new InvalidOperationException(Strings.Current["PauseTrackingBeforeEditingHistory"]);
            }

            _entries.Clear();
            _days.Clear();

            foreach (var entry in draft.Entries)
            {
                _entries.Add(entry.Id, entry);
            }

            foreach (var day in draft.Days)
            {
                _days.Add(day.Date, day);
            }

            RefreshToday();
            Revision++;
        }
    }

    public (WorkData Data, long Revision) Snapshot()
    {
        lock (_sync)
        {
            return (new(1, _entries.Values.ToArray(), _days.Values.ToArray()), Revision);
        }
    }

    public DaySummary Summary()
    {
        lock (_sync)
        {
            Settle();

            if (_summaryDate != Today)
            {
                RefreshToday();
            }

            var gross = new Dictionary<string, decimal>(_gross, StringComparer.Ordinal);
            var net = new Dictionary<string, decimal>(gross, StringComparer.Ordinal);
            var day = _days.GetValueOrDefault(Today);

            if (day is not null)
            {
                net[day.Currency] = net.GetValueOrDefault(day.Currency) - day.Cost;
            }

            return new(_regularTicks, _overtimeTicks, gross, net, day?.TargetHours ?? _settings.DailyHours, new Dictionary<string, decimal>(_overtimeGross, StringComparer.Ordinal));
        }
    }
}
