using System.ComponentModel;
using System.Reflection;
using System.Windows;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Platform;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class ReleaseFixTests(WpfTestFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("topmost", false)]
    [InlineData("automatic-updates", false)]
    [InlineData("tasks", true)]
    [InlineData("select-task", true)]
    public async Task SettingsWritesKeepCountingWhileDiskIsBusy(string action, bool switchTask)
    {
        var clock = new TestClock();
        var runtime = await CreateAsync(clock);
        using var write = new DelayedWorkWrite(runtime, _root);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(1));

            Task operation = Task.CompletedTask;

            fixture.Run(() =>
            {
                if (action == "select-task")
                {
                    runtime.Window.LiveView.SelectTask("B");
                }

                operation = runtime.ExecuteAsync(action);
            });
            await write.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            fixture.Run(() =>
            {
                Assert.True(runtime.Session.Running);
                Assert.False(SessionEndingSave.Complete(runtime.Window.Dispatcher, runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4)));
                Assert.True(runtime.Session.Running);
                clock.Advance(TimeSpan.FromMinutes(2));
            });
            write.Release.Set();
            await operation;
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));

            var entries = runtime.Session.Snapshot().Data.Entries;

            Assert.Equal(TimeSpan.FromMinutes(3).Ticks, entries.Sum(entry => entry.TotalTicks));
            Assert.Equal(TimeSpan.FromMinutes(switchTask
                ? 2
                : 0).Ticks, entries.Where(entry => entry.Task == "B").Sum(entry => entry.TotalTicks));
        }
        finally
        {
            write.Release.Set();
            await DisposeAsync(runtime);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpeningAtBoundaryDuringAWritePersistsTheFinalInterval(bool midnight)
    {
        var clock = new TestClock();

        if (midnight)
        {
            clock.Utc = new(2026, 9, 14, 23, 59, 0, TimeSpan.Zero);
        }

        var runtime = await CreateAsync(clock, new() { DailyHours = 1m / 60, Overtime = OvertimePolicy.NotAllowed });
        using var write = new DelayedWorkWrite(runtime, _root);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(30));

            Task checkpoint = Task.CompletedTask;

            fixture.Run(() =>
            {
                typeof(AppRuntime).GetMethod("CheckpointTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime, [null, EventArgs.Empty]);
                checkpoint = Field<Task>(runtime, "_checkpointTask");
            });
            await write.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            fixture.Run(() =>
            {
                clock.Advance(TimeSpan.FromSeconds(31));
                runtime.Open();
                Assert.False(runtime.Session.Running);
            });
            write.Release.Set();
            await checkpoint;

            using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());
            var saved = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.InRange(saved!.Entries.Sum(entry => entry.TotalTicks), TimeSpan.TicksPerMinute - 1, TimeSpan.TicksPerMinute);
            fixture.Run(() =>
            {
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.False(runtime.CheckpointTimerEnabled);
                Assert.False(runtime.Session.NewDayPending);
                Assert.False(runtime.Session.DailyLimitReached);
            });
        }
        finally
        {
            write.Release.Set();
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task AutomaticReportBeforeResetSurvivesTheNextSavedExit()
    {
        var clock = new TestClock();
        var opened = new List<string>();
        var runtime = await CreateAsync(clock, new() { AutoShowReport = true }, opened.Add);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(1));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            await OnUiAsync(() => runtime.ExportAsync(ReportPeriod.Today, false, true));

            var original = await File.ReadAllBytesAsync(Assert.Single(opened), TestContext.Current.CancellationToken);

            fixture.Run(() => runtime.Session.Replace(WorkData.Empty));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(2));
            await OnUiAsync(() => runtime.CloseAsync(true));
            Assert.Equal(2, opened.Count);
            Assert.NotEqual(opened[0], opened[1]);
            Assert.Equal(original, await File.ReadAllBytesAsync(opened[0], TestContext.Current.CancellationToken));
            Assert.True(File.Exists(opened[1]));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task WorkbookLaunchFailureDoesNotPreventSavedExit()
    {
        var clock = new TestClock();
        var attempts = 0;
        var runtime = await CreateAsync(clock, new() { AutoShowReport = true }, _ =>
        {
            attempts++;
            throw new Win32Exception(1155);
        });
        var closed = false;

        try
        {
            fixture.Run(() => runtime.Closed += () => closed = true);
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromMinutes(2));
            await OnUiAsync(() => runtime.CloseAsync(true));
            Assert.True(closed);
            Assert.Equal(1, attempts);
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "Exports"), "*.xlsx"));

            using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());

            Assert.Equal(TimeSpan.FromMinutes(2).Ticks, (await store.LoadAsync(TestContext.Current.CancellationToken))!.Entries.Sum(entry => entry.TotalTicks));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task AutomaticExportOwnsCancellationAndRejectsOverlappingExports()
    {
        var runtime = await CreateAsync(new TestClock());
        Task export = Task.CompletedTask;
        Task duplicate = Task.CompletedTask;

        try
        {
            fixture.Run(() =>
            {
                // All commands run before the first export's dispatcher continuation.
                export = runtime.ExportAsync(ReportPeriod.Today, false, false);
                duplicate = runtime.ExportAsync(ReportPeriod.Today, false, false);
                Assert.True(runtime.ExecuteAsync("export-today").IsCompletedSuccessfully);
                Assert.Equal(Strings.Current["AnExportIsAlreadyRunning"], runtime.Window.StatusText.Text);
                Assert.Equal(Visibility.Visible, runtime.Window.CancelOperation.Visibility);
                Assert.True(runtime.ExecuteAsync("cancel-export").IsCompletedSuccessfully);
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => duplicate);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
            fixture.Run(() => Assert.Equal(Visibility.Collapsed, runtime.Window.CancelOperation.Visibility));
            await OnUiAsync(() => runtime.ExportAsync(ReportPeriod.Today, false, false));
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "Exports"), "*.xlsx"));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task SessionEndingSettlesAndSavesWithoutLaunchingAWorkbook()
    {
        var clock = new TestClock();
        var opened = false;
        var runtime = await CreateAsync(clock, new() { AutoShowReport = true }, _ => opened = true);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(7));
            fixture.Run(() =>
            {
                Assert.True(SessionEndingSave.Complete(runtime.Window.Dispatcher, runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4)));
                Assert.False(runtime.Session.Running);
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.False(runtime.CheckpointTimerEnabled);
            });
            Assert.False(opened);
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "Exports"), "*.xlsx"));

            using var store = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());

            Assert.Equal(TimeSpan.FromSeconds(7).Ticks, (await store.LoadAsync(TestContext.Current.CancellationToken))!.Entries.Sum(entry => entry.TotalTicks));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task SessionEndingTimeoutDeclinesShutdownAndLeavesTheAppUsable()
    {
        var clock = new TestClock();
        var runtime = await CreateAsync(clock);
        var gate = Field<SemaphoreSlim>(runtime, "_saveGate");
        Task<bool> preparation = Task.FromResult(false);

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(7));
            await gate.WaitAsync(TestContext.Current.CancellationToken);

            try
            {
                fixture.Run(() =>
                {
                    var closed = false;
                    var dispatched = false;

                    runtime.Window.Show();
                    runtime.Window.Closed += (_, _) => closed = true;
                    runtime.Window.Dispatcher.BeginInvoke(() =>
                    {
                        dispatched = true;
                        runtime.Window.Close();
                        _ = runtime.ExecuteAsync("exit");
                    });
                    Assert.False(SessionEndingSave.Complete(runtime.Window.Dispatcher, token => preparation = runtime.PrepareForSessionEndAsync(token), TimeSpan.FromMilliseconds(50)));
                    Assert.True(dispatched);
                    Assert.False(closed);
                });
                Assert.False(await preparation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            }
            finally
            {
                gate.Release();
            }

            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            fixture.Run(() => Assert.True(runtime.Session.Running));
            fixture.Run(() => Assert.True(SessionEndingSave.Complete(runtime.Window.Dispatcher, runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4))));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task SessionEndingSaveFailureKeepsTheOldFileAndAllowsRetry()
    {
        var clock = new TestClock();
        var runtime = await CreateAsync(clock);
        var path = Path.Combine(_root, "work.json");

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(1));
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));

            var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(6));

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fixture.Run(() => Assert.False(SessionEndingSave.Complete(runtime.Window.Dispatcher, runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4))));
                Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            }

            fixture.Run(() => Assert.True(SessionEndingSave.Complete(runtime.Window.Dispatcher, runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4))));

            using var store = new JsonStore<WorkData>(path, data => data.Validate());

            Assert.Equal(TimeSpan.FromSeconds(7).Ticks, (await store.LoadAsync(TestContext.Current.CancellationToken))!.Entries.Sum(entry => entry.TotalTicks));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    [Fact]
    public async Task CanceledExitReportLeavesTheAppOpenAndTrackingCanRestart()
    {
        var clock = new TestClock();
        var runtime = await CreateAsync(clock, new() { AutoShowReport = true });
        Task closing = Task.CompletedTask;
        var closed = false;

        try
        {
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            clock.Advance(TimeSpan.FromSeconds(7));
            fixture.Run(() =>
            {
                runtime.Closed += () => closed = true;
                // Closing sets the cancel button before yielding to the export snapshot.
                runtime.Window.CancelOperation.IsVisibleChanged += (_, _) =>
                {
                    if (runtime.Window.CancelOperation.Visibility == Visibility.Visible)
                    {
                        _ = runtime.ExecuteAsync("cancel-export");
                    }
                };

                runtime.Window.Show();
                closing = runtime.CloseAsync(true);
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
            Assert.False(closed);
            await OnUiAsync(() => runtime.ExecuteAsync("toggle"));
            fixture.Run(() => Assert.True(runtime.Session.Running));
        }
        finally
        {
            await DisposeAsync(runtime);
        }
    }

    private async Task<AppRuntime> CreateAsync(TestClock clock, AppSettings? settings = null, Action<string>? openReport = null)
    {
        using var store = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), value => value.Validate());

        await store.SaveAsync((settings ?? new()) with { Tasks = ["A", "B"], SelectedTask = "A", ConfirmExit = false }, TestContext.Current.CancellationToken);

        AppRuntime runtime = null!;

        await OnUiAsync(() =>
        {
            runtime = new(new(_root, false, true), clock, (_, previous) => previous with { SelectedTask = "B" }, openReport: openReport ?? (_ => { }));

            return runtime.InitializeAsync();
        });

        return runtime;
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

    private static T Field<T>(AppRuntime runtime, string name) => (T)typeof(AppRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;

    private sealed class DelayedWorkWrite : IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();

        internal DelayedWorkWrite(AppRuntime runtime, string directory)
        {
            var first = 1;

            Field<JsonStore<WorkData>>(runtime, "_workStore").Dispose();

            var store = new JsonStore<WorkData>(Path.Combine(directory, "work.json"), data =>
            {
                data.Validate();

                if (Interlocked.Exchange(ref first, 0) == 1)
                {
                    Entered.SetResult();
                    Assert.True(Release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                }
            });

            typeof(AppRuntime).GetField("_workStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime, store);
        }

        public void Dispose() => Release.Dispose();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
