using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Tests;

public sealed class OvertimeTests
{
    [Theory]
    [InlineData(150)]
    [InlineData(50)]
    [InlineData(0)]
    public void ExplicitOvertimeUsesItsRateImmediatelyAndRegularRestoresAutomaticLimit(int rate)
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { HourlyRate = 100, DailyHours = 4, Overtime = OvertimePolicy.CustomRate, CustomOvertimeRate = rate }, WorkData.Empty, clock);

        session.Start("Default task", true);
        clock.Advance(TimeSpan.FromHours(1));
        session.Start("Default task", false);
        clock.Advance(TimeSpan.FromHours(4));
        session.Pause();

        var entries = session.Snapshot().Data.Entries;
        var first = entries.First();

        Assert.Equal(0, first.RegularTicks);
        Assert.Equal(TimeSpan.TicksPerHour, first.OvertimeTicks);
        Assert.Equal(rate, first.Earned);
        Assert.Equal(3 * TimeSpan.TicksPerHour, entries.Sum(entry => entry.RegularTicks));
        Assert.Equal(2 * TimeSpan.TicksPerHour, entries.Sum(entry => entry.OvertimeTicks));
        Assert.Equal(300 + 2 * rate, entries.Sum(entry => entry.Earned));
    }

    [Fact]
    public void DisallowedExplicitOvertimeDoesNotInterruptRegularTracking()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { Overtime = OvertimePolicy.NotAllowed }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Throws<InvalidOperationException>(() => session.Start("Default task", true));
        Assert.True(session.Running);
        Assert.False(session.ForceOvertime);
        session.Pause();
        Assert.Equal(TimeSpan.TicksPerMinute, Assert.Single(session.Snapshot().Data.Entries).RegularTicks);
    }

    [Fact]
    public void ExplicitOvertimeStillStopsAtMidnight()
    {
        var clock = new TestClock { Utc = new(2026, 9, 14, 23, 30, 0, TimeSpan.Zero) };
        var session = new TrackingSession(new(), WorkData.Empty, clock);

        session.Start("Default task", true);
        clock.Advance(TimeSpan.FromHours(1));
        session.Settle();
        Assert.False(session.Running);
        Assert.True(session.NewDayPending);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, Assert.Single(session.Snapshot().Data.Entries).OvertimeTicks);
    }

    [Theory]
    [InlineData(150, 950)]
    [InlineData(50, 850)]
    [InlineData(0, 800)]
    public void CustomRateAppliesOnlyAfterDailyTarget(int overtimeRate, int earned)
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { HourlyRate = 100, Overtime = OvertimePolicy.CustomRate, CustomOvertimeRate = overtimeRate }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromHours(9));
        session.Pause();

        var entry = Assert.Single(session.Snapshot().Data.Entries);

        Assert.Equal(earned, entry.Earned);
        Assert.Equal(overtimeRate, entry.OvertimeEarned);
        Assert.Equal(overtimeRate, session.Summary().OvertimeEarnings["USD"]);
        Assert.Equal(8 * TimeSpan.TicksPerHour, entry.RegularTicks);
        Assert.Equal(TimeSpan.TicksPerHour, entry.OvertimeTicks);
    }

    [Fact]
    public void DisallowedOvertimeStopsExactlyAtTargetAndCannotRestartPastIt()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { HourlyRate = 100, DailyHours = 8, Overtime = OvertimePolicy.NotAllowed }, WorkData.Empty, clock);

        session.Start("Default task");
        clock.Advance(TimeSpan.FromHours(9));
        session.Settle();
        Assert.False(session.Running);
        Assert.True(session.DailyLimitReached);

        var entry = Assert.Single(session.Snapshot().Data.Entries);

        Assert.Equal(800, entry.Earned);
        Assert.Equal(8 * TimeSpan.TicksPerHour, entry.RegularTicks);
        Assert.Equal(0, entry.OvertimeTicks);
        Assert.Throws<InvalidOperationException>(() => session.Start("Default task"));
        session.DismissNotices();
        Assert.False(session.DailyLimitReached);
        session.ApplySettings(session.Settings with { Overtime = OvertimePolicy.CustomRate, CustomOvertimeRate = 175 });
        session.Start("Default task");
        clock.Advance(TimeSpan.FromHours(1));
        session.Pause();
        Assert.Equal(975, session.Summary().Gross["USD"]);
    }

    [Fact]
    public void ChangingOvertimeRatePreservesEarlierPayAcrossTasks()
    {
        var clock = new TestClock();
        var session = new TrackingSession(new() { HourlyRate = 100, DailyHours = 1, Overtime = OvertimePolicy.CustomRate, CustomOvertimeRate = 150, Tasks = ["One", "Two"], SelectedTask = "One" }, WorkData.Empty, clock);

        session.Start("One");
        clock.Advance(TimeSpan.FromHours(2));
        session.ApplySettings(session.Settings with { CustomOvertimeRate = 200 });
        session.Start("Two");
        clock.Advance(TimeSpan.FromHours(1));
        session.Pause();

        var entries = session.Snapshot().Data.Entries;

        Assert.Equal(250, entries.Single(e => e.Task == "One").Earned);
        Assert.Equal(150, entries.Single(e => e.Task == "One").OvertimeHourlyRate);
        Assert.Equal(200, entries.Single(e => e.Task == "Two").Earned);
        Assert.Equal(450, session.Summary().Gross["USD"]);
    }

    [Fact]
    public void InvalidPolicyAndNegativeRateAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new AppSettings { Overtime = (OvertimePolicy)99 }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { CustomOvertimeRate = -1 }.Validate());
    }
}
