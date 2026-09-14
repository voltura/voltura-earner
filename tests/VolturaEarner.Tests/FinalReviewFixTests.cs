using System.Globalization;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Platform;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class FinalReviewFixTests(WpfTestFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SeparateCopiesCannotOpenTheSameHistoryUntilItsOwnerCloses()
    {
        var workDirectory = Path.Combine(_root, "shared");

        Directory.CreateDirectory(workDirectory);

        var settings = new AppSettings { WorkDirectory = workDirectory };
        var first = await CreateAsync("first", settings);

        try
        {
            await OnUiAsync(() => first.ExecuteAsync("toggle"));
            await OnUiAsync(() => first.ExecuteAsync("toggle"));

            var original = await File.ReadAllBytesAsync(Path.Combine(workDirectory, "work.json"), TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<IOException>(() => CreateAsync("second", settings));
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(workDirectory, "work.json"), TestContext.Current.CancellationToken));
        }
        finally
        {
            await DisposeAsync(first);
        }

        var reopened = await CreateAsync("second", settings);

        await DisposeAsync(reopened);
    }

    [Fact]
    public async Task MigrationRejectsAnOwnedFolderAndTransfersOwnershipAfterSuccess()
    {
        var first = await CreateAsync("first");
        var second = await CreateAsync("second");
        var target = Path.Combine(_root, "second");

        try
        {
            fixture.Run(() => first.Window.DataFolderInput.Text = target);
            await Assert.ThrowsAsync<IOException>(() => OnUiAsync(() => first.ExecuteAsync("save-settings")));
            Assert.Equal("", first.Session.Settings.WorkDirectory);
            await DisposeAsync(second);
            await OnUiAsync(() => first.ExecuteAsync("save-settings"));
            Assert.Equal(target, first.Session.Settings.WorkDirectory);
            Assert.Throws<IOException>(() => WorkHistoryLock.Acquire(Path.Combine(target, "work.json")));

            using var released = WorkHistoryLock.Acquire(Path.Combine(_root, "first", "work.json"));
        }
        finally
        {
            await DisposeAsync(second);
            await DisposeAsync(first);
        }

        using var releasedTarget = WorkHistoryLock.Acquire(Path.Combine(target, "work.json"));
    }

    [Fact]
    public async Task FailedMigrationReleasesOnlyTheDestinationAndCanRetry()
    {
        var runtime = await CreateAsync("first");
        var original = Path.Combine(_root, "first");
        var target = Path.Combine(_root, "target");

        try
        {
            fixture.Run(() => runtime.Window.DataFolderInput.Text = target);

            using (File.Open(Path.Combine(original, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = await Record.ExceptionAsync(() => OnUiAsync(() => runtime.ExecuteAsync("save-settings")));

                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            }

            Assert.Throws<IOException>(() => WorkHistoryLock.Acquire(Path.Combine(original, "work.json")));

            using (WorkHistoryLock.Acquire(Path.Combine(target, "work.json")))
            {
                Assert.Throws<IOException>(() => WorkHistoryLock.Acquire(Path.Combine(target, "work.json")));
            }

            await OnUiAsync(() => runtime.ExecuteAsync("save-settings"));
            Assert.Equal(target, runtime.Session.Settings.WorkDirectory);
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTaskSaveDisplaysTheCommittedTaskAndKeepsCorrectAccounting(bool failWorkWrite)
    {
        var clock = new TestClock();
        var runtime = await CreateAsync("first", clock: clock);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(1));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() =>
            {
                runtime.Window.LiveView.SelectTask("B");
                runtime.LiveWindow.LiveView.SelectTask("B");
            });

            var file = Path.Combine(_root, "first", failWorkWrite
                ? "work.json"
                : "settings.json");

            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = await Record.ExceptionAsync(() => OnUiAsync(() => runtime.ExecuteAsync("select-task")));

                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            }

            fixture.Run(() =>
            {
                var expected = failWorkWrite
                    ? "B"
                    : "A";

                Assert.Equal(expected, runtime.Session.Settings.SelectedTask);
                Assert.Equal(expected, runtime.Window.TaskChoice.SelectedItem);
                Assert.Equal(expected, runtime.LiveWindow.LiveView.TaskChoice.SelectedItem);
                Assert.Equal(!failWorkWrite, runtime.Session.Running);
                runtime.Session.Pause();
            });
            Assert.Equal(TimeSpan.FromMinutes(2).Ticks, runtime.Session.Snapshot().Data.Entries.Where(entry => entry.Task == "A").Sum(entry => entry.TotalTicks));
            fixture.Run(() => runtime.Window.LiveView.SelectTask("B"));
            await OnUiAsync(() => runtime.ExecuteAsync("select-task"));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(1));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            Assert.Equal(TimeSpan.TicksPerMinute, runtime.Session.Snapshot().Data.Entries.Where(entry => entry.Task == "B").Sum(entry => entry.TotalTicks));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Theory]
    [InlineData("topmost")]
    [InlineData("select-task")]
    [InlineData("tasks")]
    [InlineData("automatic-updates")]
    public async Task LiveSettingsChangesPreservePreferencesUntilExplicitSave(string action)
    {
        var runtime = await CreateAsync("first");

        try
        {
            fixture.Run(() =>
            {
                runtime.Window.RateInput.Text = "123";
                runtime.Window.DataFolderInput.Text = Path.Combine(_root, "draft");
                runtime.Window.ApplyTodayCheck.IsChecked = true;
                runtime.Window.Topmost = true;
                runtime.Window.LiveView.SelectTask("B");
            });
            await OnUiAsync(() => runtime.ExecuteAsync(action));
            fixture.Run(() =>
            {
                Assert.Equal("123", runtime.Window.RateInput.Text);
                Assert.Equal(Path.Combine(_root, "draft"), runtime.Window.DataFolderInput.Text);
                Assert.True(runtime.Window.ApplyTodayCheck.IsChecked);
                Assert.Equal(1000m, runtime.Session.Settings.HourlyRate);
                Assert.False(Directory.Exists(Path.Combine(_root, "draft")));
            });
            await OnUiAsync(() => runtime.ExecuteAsync("save-settings"));
            Assert.Equal(123m, runtime.Session.Settings.HourlyRate);
            Assert.Equal(action == "topmost", runtime.Session.Settings.AlwaysOnTop);
            fixture.Run(() => Assert.False(runtime.Window.ApplyTodayCheck.IsChecked));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyingTodayChangesCostAndCurrencyTogetherWithoutChangingEntries(bool applyToday)
    {
        var clock = new TestClock();
        var runtime = await CreateAsync("first", new() { DailyCost = 50, Currency = "USD", HourlyRate = 200 }, clock);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromHours(1));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));

            var original = runtime.Session.Snapshot().Data.Entries;

            fixture.Run(() =>
            {
                runtime.Window.CurrencyInput.Text = "SEK";
                runtime.Window.CostInput.Text = 100m.ToString(CultureInfo.CurrentCulture);
                runtime.Window.ApplyTodayCheck.IsChecked = applyToday;
            });
            await OnUiAsync(() => runtime.ExecuteAsync("save-settings"));

            using var store = new JsonStore<WorkData>(Path.Combine(_root, "first", "work.json"), data => data.Validate());
            var saved = (await store.LoadAsync(TestContext.Current.CancellationToken))!;
            var day = Assert.Single(saved.Days);

            Assert.Equal(applyToday
                ? "SEK"
                : "USD", day.Currency);
            Assert.Equal(applyToday
                ? 100m
                : 50m, day.Cost);
            Assert.Equal(original, saved.Entries);
            fixture.Run(() =>
            {
                var summary = runtime.Session.Summary();

                Assert.Equal(applyToday
                    ? 200m
                    : 150m, summary.Net["USD"]);

                if (applyToday)
                {
                    Assert.Equal(-100m, summary.Net["SEK"]);
                }
            });
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    private async Task<AppRuntime> CreateAsync(string name, AppSettings? settings = null, TestClock? clock = null)
    {
        var directory = Path.Combine(_root, name);
        using var store = new JsonStore<AppSettings>(Path.Combine(directory, "settings.json"), value => value.Validate());

        await store.SaveAsync((settings ?? new()) with { Tasks = ["A", "B"], SelectedTask = "A", ConfirmExit = false }, TestContext.Current.CancellationToken);

        AppRuntime runtime = null!;

        try
        {
            await OnUiAsync(() =>
            {
                runtime = new(new(directory, false, true), clock, (_, previous) => previous with { SelectedTask = "B" });

                return runtime.InitializeAsync();
            });

            return runtime;
        }
        catch
        {
            if (runtime is not null)
            {
                await DisposeAsync(runtime);
            }

            throw;
        }
    }

    private async Task OnUiAsync(Func<Task> action)
    {
        Task operation = Task.CompletedTask;

        fixture.Run(() => operation = action());
        await operation;
    }

    private async Task DisposeAsync(AppRuntime runtime)
    {
        await OnUiAsync(() => runtime.DisposeAsync().AsTask());
        fixture.Run(() => runtime.Window.Close());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
