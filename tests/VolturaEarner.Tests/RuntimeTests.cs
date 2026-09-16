using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Updates;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class RuntimeTests(WpfTestFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task OvertimeSelectionSurvivesTaskChangesAndOrdinaryStartReturnsToRegular()
    {
        var clock = new TestClock();
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true), clock, (_, settings) => settings with { Tasks = [.. settings.Tasks, "Other"], SelectedTask = "Other" });
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() => operation = runtime.ExecuteAsync("start-overtime"));
            await operation;
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() => operation = runtime.ExecuteAsync("tasks"));
            await operation;
            Assert.True(runtime.Session.ForceOvertime);
            Assert.True(runtime.Session.Running);
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            Assert.False(runtime.Session.ForceOvertime);
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;

            var entries = runtime.Session.Snapshot().Data.Entries;

            Assert.Equal(2 * TimeSpan.TicksPerMinute, entries.Sum(entry => entry.OvertimeTicks));
            Assert.Equal(TimeSpan.TicksPerMinute, entries.Sum(entry => entry.RegularTicks));
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() => runtime.Window.Close());
        }
    }

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [InlineData("system")]
    public async Task InitializationPreparesSavedThemeBeforeAnyWindowIsShown(string theme)
    {
        using var store = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), settings => settings.Validate());

        await store.SaveAsync(new() { Theme = theme }, TestContext.Current.CancellationToken);

        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;
        System.Windows.Media.Color expected = default;
        var becameVisible = false;

        fixture.Run(() =>
        {
            VolturaEarner.Ui.ThemeManager.Apply(theme);
            expected = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("WindowBrush")).Color;
            VolturaEarner.Ui.ThemeManager.Apply(theme == "dark"
                ? "light"
                : "dark");
            runtime = new(new(_root, false, true));
            runtime.Window.IsVisibleChanged += (_, _) => becameVisible |= runtime.Window.IsVisible;
            operation = runtime.InitializeAsync();
        });

        try
        {
            await operation;
            fixture.Run(() =>
            {
                Assert.True(runtime.Initialized);
                Assert.False(becameVisible);
                Assert.False(runtime.Window.IsVisible);
                Assert.False(runtime.LiveWindow.IsVisible);
                Assert.Equal(VolturaEarner.Ui.Strings.Current["Paused"], runtime.Window.TrackingState.Text);
                Assert.Equal(VolturaEarner.Ui.Strings.Current["Paused"], runtime.LiveWindow.TrackingState.Text);
                Assert.Equal(expected, ((System.Windows.Media.SolidColorBrush)runtime.Window.Background).Color);
                Assert.Equal(expected, ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("WindowBrush")).Color);
            });
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() =>
            {
                runtime.Window.Close();
                VolturaEarner.Ui.ThemeManager.Apply("system");
            });
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ManagingTasksPreservesTrackingAndAccountsTimeToTheCorrectTask(bool running, bool switchTask)
    {
        var clock = new TestClock();
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;
        var originalTask = "";

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true), clock, (_, settings) =>
            {
                Assert.Equal(running, runtime.Session.Running);
                clock.Advance(TimeSpan.FromMinutes(2));

                var manager = new VolturaEarner.Ui.TaskManagerWindow(settings);

                try
                {
                    Assert.True(manager.AddTask("Another task"));

                    if (switchTask)
                    {
                        manager.UseSelected();
                    }

                    return manager.Result;
                }
                finally
                {
                    manager.Close();
                }
            });
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() =>
            {
                originalTask = runtime.Session.Settings.SelectedTask;
                operation = running
                    ? runtime.ExecuteAsync("toggle")
                    : Task.CompletedTask;
            });
            await operation;
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() => operation = runtime.ExecuteAsync("tasks"));
            await operation;
            fixture.Run(() =>
            {
                Assert.Equal(running, runtime.Session.Running);
                Assert.Equal(switchTask
                    ? "Another task"
                    : originalTask, runtime.Session.Settings.SelectedTask);
                Assert.Equal(running, runtime.CheckpointTimerEnabled);
                clock.Advance(TimeSpan.FromMinutes(2));
                operation = running
                    ? runtime.ExecuteAsync("toggle")
                    : Task.CompletedTask;
            });
            await operation;

            var entries = runtime.Session.Snapshot().Data.Entries;

            Assert.Equal(TimeSpan.FromMinutes(running
                ? 5
                : 0).Ticks, entries.Sum(entry => entry.TotalTicks));
            Assert.Equal(TimeSpan.FromMinutes(running
                ? (switchTask
                    ? 3
                    : 5)
                : 0).Ticks, entries.Where(entry => entry.Task == originalTask).Sum(entry => entry.TotalTicks));
            Assert.Equal(TimeSpan.FromMinutes(running && switchTask
                ? 2
                : 0).Ticks, entries.Where(entry => entry.Task == "Another task").Sum(entry => entry.TotalTicks));
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() => runtime.Window.Close());
        }
    }

    [Fact]
    public async Task DelayedPersistenceDoesNotBlockDispatcher()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var store = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), s =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            s.Validate();
        });
        Task operation = Task.CompletedTask;

        fixture.Run(() => operation = store.SaveAsync(new(), TestContext.Current.CancellationToken));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        try
        {
            var dispatched = false;

            fixture.Run(() => dispatched = true);
            Assert.True(dispatched);
            Assert.False(operation.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await operation;
    }
    [Fact]
    public async Task RuntimeHasNoTimersWhenPausedAndNoDisplayTimerWhenHidden()
    {
        AppRuntime runtime = null!;
        Task initialize = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true));
            initialize = runtime.InitializeAsync();
        });
        await initialize;

        try
        {
            fixture.Run(() =>
            {
                Assert.False(runtime.Session.Running);
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.False(runtime.CheckpointTimerEnabled);
            });

            Task start = Task.CompletedTask;

            fixture.Run(() => start = runtime.ExecuteAsync("toggle"));
            await start;
            fixture.Run(() =>
            {
                Assert.True(runtime.Session.Running);
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.True(runtime.CheckpointTimerEnabled);
            });

            Task pause = Task.CompletedTask;

            fixture.Run(() => pause = runtime.ExecuteAsync("toggle"));
            await pause;
            Assert.True(File.Exists(Path.Combine(_root, "work.json")));
            fixture.Run(() => Assert.False(runtime.CheckpointTimerEnabled));
        }
        finally
        {
            Task dispose = Task.CompletedTask;

            fixture.Run(() => dispose = runtime.DisposeAsync().AsTask());
            await dispose;
            fixture.Run(() => runtime.Window.Close());
        }
    }
    [Fact]
    public void AboutStatesAndSavedPreferencesRenderTogether()
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Populate(new() { PlaySounds = true, HourlyRate = 123.45m, Theme = "dark", MinimalViewSize = "small", SaveWorkLog = false });
                Assert.True(window.SoundsCheck.IsChecked);
                Assert.False(window.SaveLogCheck.IsChecked);
                Assert.Equal(2, window.ThemeChoice.SelectedIndex);
                Assert.Equal(1, window.MinimalViewSizeChoice.SelectedIndex);
                Assert.Equal("small", window.ReadSettings(new()).MinimalViewSize);
                window.UpdateState(new(UpdateStatus.Ready, "installer.exe"), true);
                Assert.Equal(Visibility.Visible, window.InstallButton.Visibility);
                Assert.True(window.CheckUpdateButton.IsEnabled);
                window.UpdateState(new(UpdateStatus.Downloading), true);
                Assert.False(window.CheckUpdateButton.IsEnabled);
                Assert.Equal(Visibility.Collapsed, window.InstallButton.Visibility);
                Assert.NotEqual("Downloading", window.UpdateStatusText.Text);
                window.UpdateState(new(UpdateStatus.ManualUpdates), false);
                Assert.Equal("Open downloads", window.CheckUpdateButton.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }
    [Fact]
    public async Task ReachingDisabledOvertimeLimitAutomaticallySavesAndStopsTimers()
    {
        using var settings = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), s => s.Validate());

        await settings.SaveAsync(new() { HourlyRate = 100, Overtime = OvertimePolicy.NotAllowed }, TestContext.Current.CancellationToken);

        var clock = new TestClock();
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true), clock);
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            clock.Advance(TimeSpan.FromHours(9));
            fixture.Run(() => operation = runtime.RefreshLiveAsync());
            await operation;
            fixture.Run(() =>
            {
                Assert.False(runtime.Session.Running);
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.False(runtime.CheckpointTimerEnabled);
                Assert.Contains("overtime is disabled", runtime.Window.StatusText.Text, StringComparison.Ordinal);
            });

            using var work = new JsonStore<VolturaEarner.Features.Tracking.WorkData>(Path.Combine(_root, "work.json"), d => d.Validate());
            var data = await work.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(800, Assert.Single(data!.Entries).Earned);
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() => runtime.Window.Close());
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
