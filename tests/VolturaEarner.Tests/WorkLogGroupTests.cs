using System.Globalization;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

public sealed class WorkLogGroupTests
{
    [Fact]
    public void PeriodTotalsIncludeAllTasksAndKeepCurrenciesSeparate()
    {
        var day = new DateOnly(2026, 9, 14);
        var data = new WorkData(1, [], [])
        {
            Entries =
            [
                new(Guid.NewGuid(), day, "A", "USD", 100, TimeSpan.TicksPerHour, 0, 100, 0),
                new(Guid.NewGuid(), day, "B", "USD", 100, 0, TimeSpan.TicksPerHour, 150, 150),
                new(Guid.NewGuid(), day, "A", "SEK", 650, 20 * TimeSpan.TicksPerHour, 0, 13000, 0),
                new(Guid.NewGuid(), day, "B", "SEK", 650, 5 * TimeSpan.TicksPerHour, 0, 3250, 0),
                new(Guid.NewGuid(), day.AddDays(-1), "A", "USD", 100, TimeSpan.TicksPerHour, 0, 100, 0),
            ],
        };
        var totals = WorkLogTotals.Create(ReportService.Select(data, day, ReportPeriod.Today, DayOfWeek.Monday));

        Assert.Equal("27:00:00", totals.Duration);
        Assert.Equal("01:00:00", totals.Overtime);
        Assert.Equal($"{16250:N2} SEK{Environment.NewLine}{250:N2} USD", totals.Amount);
        Assert.Equal(5, data.Entries.Length);
        Assert.Equal("28:00:00", WorkLogTotals.Create(ReportService.Select(data, day, ReportPeriod.All, DayOfWeek.Monday)).Duration);
    }

    [Fact]
    public void EmptyPeriodShowsZeroTimeWithoutInventingACurrency()
    {
        var totals = WorkLogTotals.Create([]);

        Assert.Equal("00:00:00", totals.Duration);
        Assert.Equal("00:00:00", totals.Overtime);
        Assert.Equal("—", totals.Amount);
    }

    [Fact]
    public void RepeatedSessionsBecomeOneRowPerTaskAndDayWithoutMixingCurrenciesOrRates()
    {
        var day = new DateOnly(2026, 9, 14);
        var entries = new[]
        {
            new WorkEntry(Guid.NewGuid(), day, "A", "USD", 100, TimeSpan.TicksPerHour, 0, 100, 0),
            new WorkEntry(Guid.NewGuid(), day, "A", "USD", 200, 0, TimeSpan.TicksPerHour, 250, 250, 250),
            new WorkEntry(Guid.NewGuid(), day, "A", "SEK", 300, TimeSpan.TicksPerHour, 0, 300, 0),
            new WorkEntry(Guid.NewGuid(), day, "B", "USD", 100, TimeSpan.TicksPerHour, 0, 100, 0),
            new WorkEntry(Guid.NewGuid(), day.AddDays(-1), "A", "USD", 100, TimeSpan.TicksPerHour, 0, 100, 0),
        };
        var groups = WorkLogGroup.Create(entries);

        Assert.Equal(3, groups.Length);

        var group = groups[0];

        Assert.Equal(day, group.Day);
        Assert.Equal("A", group.Task);
        Assert.Equal("03:00:00", group.Duration);
        Assert.Equal("01:00:00", group.Overtime);
        Assert.Contains($"{350:N2} USD", group.Amount);
        Assert.Contains($"{300:N2} SEK", group.Amount);
        Assert.Equal(entries.Take(3), group.Entries);
        Assert.Equal(250, group.Entries[1].EffectiveOvertimeRate);
    }

    [Theory]
    [InlineData(599474823)]
    [InlineData(1)]
    [InlineData(863999999999)]
    public void ReadableHoursKeepExactStoredTimeWhenUnchanged(long ticks)
    {
        var text = EditorDialogs.FormatHours(ticks);
        var value = decimal.Parse(text, CultureInfo.CurrentCulture);

        Assert.Equal(ticks, EditorDialogs.ParseHours(text, value, ticks));
        Assert.True(text.Length <= 7, text);
        Assert.Equal(TimeSpan.TicksPerHour, EditorDialogs.ParseHours("1", 1, ticks));
    }
}
