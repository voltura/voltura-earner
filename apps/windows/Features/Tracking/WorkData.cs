using VolturaEarner.Ui;
namespace VolturaEarner.Features.Tracking;

public sealed record WorkEntry(Guid Id, DateOnly Date, string Task, string Currency, decimal HourlyRate, long RegularTicks, long OvertimeTicks, decimal Earned, decimal OvertimeEarned, decimal? OvertimeHourlyRate = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal EffectiveOvertimeRate => OvertimeHourlyRate ?? HourlyRate;
    public long TotalTicks => checked(RegularTicks + OvertimeTicks);
    public void Validate()
    {
        if (OvertimeHourlyRate is < 0 or > 1000000000m)
        {
            throw new InvalidDataException(Strings.Current["InvalidOvertimeHourlyRate"]);
        }

        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Task) || Task.Length > 120 || Task.Any(char.IsControl) || Currency is null || Currency.Length > 3 || Currency.Any(char.IsControl) || HourlyRate < 0 || HourlyRate > 1000000000m || RegularTicks < 0 || OvertimeTicks < 0 || RegularTicks > TimeSpan.TicksPerDay || OvertimeTicks > TimeSpan.TicksPerDay || TotalTicks > TimeSpan.TicksPerDay || Earned < 0 || Earned > 24000000000m || OvertimeEarned < 0 || OvertimeEarned > Earned)
        {
            throw new InvalidDataException(Strings.Current["InvalidWorkEntryCheckTaskDurationRateEarningsAndCurrency"]);
        }
    }
}

public sealed record WorkDay(DateOnly Date, decimal TargetHours, decimal Cost, string Currency, bool OvertimeNotified = false);
public sealed record WorkData(int Schema, WorkEntry[] Entries, WorkDay[] Days)
{
    public static WorkData Empty => new(1, [], []);
    public void Validate()
    {
        if (Schema != 1 || Entries is null || Days is null || Entries.Length > 1000000 || Days.Length > 100000 || Entries.Any(e => e is null) || Days.Any(d => d is null) || Entries.Select(e => e.Id).Distinct().Count() != Entries.Length || Days.Select(d => d.Date).Distinct().Count() != Days.Length)
        {
            throw new InvalidDataException(Strings.Current["UnsupportedOrInvalidWorkHistory"]);
        }

        foreach (var entry in Entries)
        {
            entry.Validate();
        }

        foreach (var day in Days)
        {
            if (day.TargetHours <= 0 || day.TargetHours > 24 || day.Cost < 0 || day.Cost > 1000000000m || day.Currency is null || day.Currency.Length > 3 || day.Currency.Any(char.IsControl))
            {
                throw new InvalidDataException(Strings.Current["InvalidDailyTargetOrCost"]);
            }
        }

        var dates = Days.Select(d => d.Date).ToHashSet();

        if (Entries.Any(e => !dates.Contains(e.Date)) || Entries.GroupBy(e => e.Date).Any(g => g.Sum(e => e.TotalTicks) > TimeSpan.TicksPerDay))
        {
            throw new InvalidDataException(Strings.Current["WorkEntriesRequireADailyRecordAndCannotExceed24HoursPerDate"]);
        }
    }
}

public sealed record DaySummary(long RegularTicks, long OvertimeTicks, IReadOnlyDictionary<string, decimal> Gross, IReadOnlyDictionary<string, decimal> Net, decimal TargetHours, IReadOnlyDictionary<string, decimal> OvertimeEarnings);
