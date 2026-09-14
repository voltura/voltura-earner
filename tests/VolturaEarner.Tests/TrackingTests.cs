using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Tests;

public sealed class TrackingTests
{
    [Fact]
    public void PausingBetweenTicksAndSwitchingTasksSettlesExactTime()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { Tasks = ["A", "B"], SelectedTask = "A", HourlyRate = 3600 }, WorkData.Empty, clock);

        session.Start("A");
        clock.Advance(TimeSpan.FromMilliseconds(1250));
        session.Start("B");
        clock.Advance(TimeSpan.FromMilliseconds(750));
        session.Pause();

        var data = session.Snapshot().Data;

        Assert.Equal(1.25m, data.Entries.Single(e => e.Task == "A").Earned);
        Assert.Equal(0.75m, data.Entries.Single(e => e.Task == "B").Earned);
        clock.Advance(TimeSpan.FromHours(1));
        session.Settle();
        Assert.Equal(2m, session.Snapshot().Data.Entries.Sum(e => e.Earned));
    }

    [Fact]
    public void OvertimeSplitsBoundaryAcrossTasksAndNotifiesOnlyOnce()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { Tasks = ["A", "B"], SelectedTask = "A", HourlyRate = 100, DailyCost = 50 }, WorkData.Empty, clock);

        session.Start("A");
        clock.Advance(TimeSpan.FromHours(7));
        session.Start("B");
        clock.Advance(TimeSpan.FromHours(2));
        session.Settle();

        var summary = session.Summary();

        Assert.Equal(TimeSpan.FromHours(8).Ticks, summary.RegularTicks);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, summary.OvertimeTicks);
        Assert.Equal(900m, summary.Gross["USD"]);
        Assert.Equal(850m, summary.Net["USD"]);
        Assert.True(session.OvertimePending);
        session.DismissNotices();
        clock.Advance(TimeSpan.FromMinutes(1));
        session.Settle();
        Assert.False(session.OvertimePending);
        Assert.True(session.Running);
    }

    [Fact]
    public void MidnightSettlesOldDayAndNeverBillsThePrompt()
    {
        var clock = new TestClock { Utc = new(2026, 12, 31, 23, 59, 59, TimeSpan.Zero) };
        var session = new TrackingSession(new() { HourlyRate = 3600 }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromSeconds(3));
        session.Settle();
        Assert.False(session.Running);
        Assert.True(session.NewDayPending);

        var old = Assert.Single(session.Snapshot().Data.Entries);

        Assert.Equal(new DateOnly(2026, 12, 31), old.Date);
        Assert.Equal(1m, old.Earned);
        clock.Advance(TimeSpan.FromHours(1));
        session.Start("Default task");
        clock.Advance(TimeSpan.FromSeconds(2));
        session.Pause();
        Assert.Equal(3m, session.Snapshot().Data.Entries.Sum(e => e.Earned));
    }

    [Fact]
    public void ClockChangesCannotCreateNegativeOrDuplicateEarnings()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { HourlyRate = 3600 }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.Utc = clock.Utc.AddMinutes(-30);
        session.Settle();
        clock.Advance(TimeSpan.FromSeconds(3));
        session.Pause();
        Assert.Equal(5m, Assert.Single(session.Snapshot().Data.Entries).Earned);
    }

    [Fact]
    public void RateCurrencyAndCostsAreNotRetroactive()
    {
        var clock = new TestClock();
        var settings = new AppSettings { HourlyRate = 100, DailyCost = 50 };
        var session = new TrackingSession(settings, WorkData.Empty, clock);

        session.Start(settings.SelectedTask);
        clock.Advance(TimeSpan.FromHours(1));
        session.ApplySettings(settings with { HourlyRate = 200, Currency = "EUR", DailyCost = 200 });
        session.Start(settings.SelectedTask);
        clock.Advance(TimeSpan.FromHours(1));
        session.Pause();
        Assert.Equal(50m, session.Summary().Net["USD"]);
        Assert.Equal(200m, session.Summary().Net["EUR"]);
        Assert.Equal(50m, Assert.Single(session.Snapshot().Data.Days).Cost);
    }

    [Fact]
    public void ReloadedDataStartsPausedAndIncludesZeroRateWork()
    {
        var clock = new TestClock();
        var settings = new AppSettings { HourlyRate = 0 };
        var session = new TrackingSession(settings, WorkData.Empty, clock);

        session.Start(settings.SelectedTask);
        clock.Advance(TimeSpan.FromMinutes(10));
        session.Settle();

        var restored = new TrackingSession(settings, session.Snapshot().Data, clock);

        Assert.False(restored.Running);
        Assert.Equal(TimeSpan.FromMinutes(10).Ticks, restored.Summary().RegularTicks);
        Assert.Equal(0m, restored.Summary().Gross["USD"]);
    }

    [Fact]
    public void SnapshotsRemainImmutableAndInvalidReplacementLeavesStateUntouched()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new(), WorkData.Empty, clock);

        session.Start("Default task");

        var snapshot = session.Snapshot().Data;

        clock.Advance(TimeSpan.FromSeconds(5));
        session.Pause();
        Assert.Equal(0, snapshot.Entries[0].TotalTicks);
        Assert.Throws<InvalidDataException>(() => session.Replace(new(99, [], [])));
        Assert.Equal(TimeSpan.FromSeconds(5).Ticks, session.Snapshot().Data.Entries[0].TotalTicks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public void InvalidHoursAreRejected(int hours) => Assert.Throws<InvalidDataException>(() => new AppSettings { DailyHours = hours }.Validate());

    [Fact]
    public void ReadingSummaryAcrossMidnightSettlesPreviousDayBeforeChangingTotals()
    {
        var clock = new TestClock { Utc = new(2026, 9, 14, 23, 0, 0, TimeSpan.Zero) };
        var session = new TrackingSession(new() { DailyHours = 1 }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromMinutes(30));
        session.Settle();
        clock.Advance(TimeSpan.FromMinutes(40));

        var summary = session.Summary();

        Assert.False(session.Running);
        Assert.Equal(0, summary.RegularTicks);

        var entry = Assert.Single(session.Snapshot().Data.Entries);

        Assert.Equal(TimeSpan.TicksPerHour, entry.RegularTicks);
        Assert.Equal(0, entry.OvertimeTicks);
    }

    [Theory]
    [InlineData("{\"Currency\":null}")]
    [InlineData("{\"Tasks\":null}")]
    [InlineData("{\"WorkDirectory\":null}")]
    public void NullStoredSettingsAreRejectedWithValidationError(string json)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Throws<InvalidDataException>(settings.Validate);
    }

    [Fact]
    public void FortyTasksSupportedButFortyOneRejected()
    {
        var tasks = Enumerable.Range(0, 40).Select(i => $"Task {i}").ToArray();

        new AppSettings { Tasks = tasks, SelectedTask = tasks[0] }.Validate();
        Assert.Throws<InvalidDataException>(() => new AppSettings { Tasks = [.. tasks, "extra"], SelectedTask = tasks[0] }.Validate());
    }
}

public sealed class MidnightTransitionTests
{
    [Theory]
    [InlineData("Pacific SA Standard Time", "2026-09-06T03:59:59Z")]
    [InlineData("Cuba Standard Time", "2026-11-01T03:59:59Z")]
    [InlineData("W. Europe Standard Time", "2026-09-14T21:59:59Z")]
    public void TrackingStopsAtTheFirstInstantOfTheNextDay(string zone, string beforeMidnight)
    {
        var clock = new TestClock { Zone = TimeZoneInfo.FindSystemTimeZoneById(zone), Utc = DateTimeOffset.Parse(beforeMidnight, System.Globalization.CultureInfo.InvariantCulture) };
        var session = new TrackingSession(new(), WorkData.Empty, clock);
        var date = session.Today;

        session.Start(session.Settings.SelectedTask);
        clock.Advance(TimeSpan.FromSeconds(2));
        session.Settle();
        session.Pause();

        var entry = Assert.Single(session.Snapshot().Data.Entries);

        Assert.Equal(date, entry.Date);
        // Windows represents some historical transitions at xx:59:59.999.
        Assert.InRange(entry.TotalTicks, TimeSpan.TicksPerSecond - TimeSpan.TicksPerMillisecond, TimeSpan.TicksPerSecond);

        var boundary = DateTimeOffset.Parse(beforeMidnight, System.Globalization.CultureInfo.InvariantCulture).AddTicks(entry.TotalTicks);

        Assert.Equal(date, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(boundary.AddTicks(-1), clock.Zone).DateTime));
        Assert.Equal(date.AddDays(1), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(boundary, clock.Zone).DateTime));
        Assert.False(session.Running);
        Assert.True(session.NewDayPending);
    }
}

internal sealed class TestClock : TimeProvider
{
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;
    public DateTimeOffset Utc { get; set; } = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);
    private long _ticks;
    public override TimeZoneInfo LocalTimeZone => Zone;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => Utc;
    public void Advance(TimeSpan duration)
    {
        _ticks += duration.Ticks;
        Utc += duration;
    }
}
