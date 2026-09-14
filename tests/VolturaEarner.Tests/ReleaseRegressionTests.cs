using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Updates;
using VolturaEarner.Ui;
using VolturaEarner.Platform;
using System.Net.Http;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class ReleaseRegressionTests(WpfTestFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
    private static readonly int[] ScrollPages = [0, 2, 3];

    [Fact]
    public async Task UpdateCacheSurvivesAnApplicationLanguageChange()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        UpdateService service = null!;
        var language = "system";

        fixture.Run(() =>
        {
            language = Strings.Current.LanguageId;
            Strings.Current.SetLanguage("sv");
            service = new(new(_root, false, true), new UpdateServiceTests.ReleaseHandler(key), key.ExportSubjectPublicKeyInfoPem(), true, false, current: new(1, 0, 0));
            Strings.Current.SetLanguage(language);
        });

        await using (service)
        {
            await service.CheckAsync();
            Assert.True(service.State.Ready);
            Assert.Equal(Path.Combine(_root, "Updates", "pending"), Path.GetDirectoryName(service.State.Installer));
        }

        await using var restored = new UpdateService(new(_root, false, true), new UpdateServiceTests.ReleaseHandler(key), key.ExportSubjectPublicKeyInfoPem(), true, false, current: new(1, 0, 0));

        await restored.RestorePendingAsync();
        Assert.True(restored.State.Ready);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateHandoffSavesBeforeLaunchAndOnlyClosesAfterSuccessfulLaunch(bool failLaunch)
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var clock = new TestClock();
        var closed = false;
        var launched = false;
        var update = new UpdateService(new(_root, false, true), new UpdateServiceTests.ReleaseHandler(key), key.ExportSubjectPublicKeyInfoPem(), true, false, current: new(1, 0, 0), install: async (_, token) =>
        {
            using var work = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), data => data.Validate());
            var saved = await work.LoadAsync(token);

            Assert.Equal(TimeSpan.TicksPerMinute, Assert.Single(saved!.Entries).TotalTicks);
            Assert.False(closed);
            launched = true;

            if (failLaunch)
            {
                throw new System.ComponentModel.Win32Exception(1223);
            }
        });
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true), clock, updates: update);
            runtime.Closed += () => closed = true;
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            do
            {
                // Startup cache restoration shares the updater gate with manual checks.
                await update.CheckAsync();

                if (!update.State.Ready)
                {
                    Assert.True(DateTime.UtcNow < deadline);
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
            } while (!update.State.Ready);

            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Run(() => operation = runtime.ExecuteAsync("install-update"));
            await operation;
            Assert.True(launched);
            Assert.Equal(!failLaunch, closed);
            Assert.False(runtime.Session.Running);
            Assert.False(runtime.CheckpointTimerEnabled);

            if (failLaunch)
            {
                Assert.Equal(UpdateStatus.UpdateInstallFailed, update.State.Status);
                fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
                await operation;
                Assert.True(runtime.Session.Running);
            }
        }
        finally
        {
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Theory]
    [InlineData("settings.json", false)]
    [InlineData("work.json", false)]
    [InlineData("settings.json", true)]
    [InlineData("work.json", true)]
    public async Task FailedStartupCanExitWithoutTouchingData(string file, bool titleBar)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, file);

        await File.WriteAllTextAsync(path, "{invalid", TestContext.Current.CancellationToken);

        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true));
            runtime.Closed += () => closed.TrySetResult();
            operation = runtime.InitializeAsync();
        });

        var failure = await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => operation);

        try
        {
            fixture.Run(() =>
            {
                runtime.ReportStartupError(failure);
                Assert.Equal(Strings.Current["LoadFailed"], runtime.Window.TrackingState.Text);
                Assert.Contains(Strings.Current.Format("DataRecoveryInstructions", path), runtime.Window.StatusText.Text, StringComparison.Ordinal);

                if (titleBar)
                {
                    runtime.Window.Close();
                }
                else
                {
                    operation = runtime.ExecuteAsync("exit");
                }
            });
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(runtime.DisplayTimerEnabled);
            Assert.False(runtime.CheckpointTimerEnabled);
            Assert.Equal("{invalid", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Fact]
    public async Task InvalidPreferencesAreHandledByTheActualButtonEvent()
    {
        var runtime = await CreateRuntimeAsync();
        Exception? unhandled = null;
        DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
        {
            unhandled = e.Exception;
            e.Handled = true;
        };

        try
        {
            fixture.Run(() =>
            {
                Application.Current.DispatcherUnhandledException += handler;
                runtime.Window.Pages.SelectedIndex = 2;
                Arrange(runtime.Window.RootSurface);
                runtime.Window.RateInput.Text = "invalid";

                var save = Visuals(runtime.Window.RootSurface).OfType<Button>().Single(button => Equals(button.Tag, "save-settings"));

                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            // Drain the dispatcher, including exceptions posted by async void handlers.

            Task drain = Task.CompletedTask;

            fixture.Run(() => drain = Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task);
            await drain;
            fixture.Run(() =>
            {
                Assert.Null(unhandled);
                Assert.Contains(Strings.Current["TheActionCouldNotComplete"], runtime.Window.StatusText.Text, StringComparison.Ordinal);
                Assert.False(runtime.Session.Running);
            });
        }
        finally
        {
            fixture.Run(() => Application.Current.DispatcherUnhandledException -= handler);
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Fact]
    public async Task FailedFolderMigrationCanRetryAndKeepsDifferentHistoryProtected()
    {
        var runtime = await CreateRuntimeAsync();
        var destination = Path.Combine(_root, "moved");
        Task operation = Task.CompletedTask;

        try
        {
            fixture.Run(() => operation = runtime.ExecuteAsync("save-settings"));
            await operation;
            fixture.Run(() => runtime.Window.DataFolderInput.Text = destination);

            using (File.Open(Path.Combine(_root, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                fixture.Run(() => operation = runtime.ExecuteAsync("save-settings"));

                var error = await Record.ExceptionAsync(() => operation);

                Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            }

            Assert.NotEqual(destination, runtime.Session.Settings.WorkDirectory);
            fixture.Run(() => operation = runtime.ExecuteAsync("save-settings"));
            await operation;
            Assert.Equal(destination, runtime.Session.Settings.WorkDirectory);

            var occupied = Path.Combine(_root, "occupied");
            var workPath = Path.Combine(occupied, "work.json");
            using var store = new JsonStore<WorkData>(workPath, data => data.Validate());
            var day = new DateOnly(2026, 9, 14);
            var data = new WorkData(1, [], [new(day, 8, 123, "USD")]);

            await store.SaveAsync(data, TestContext.Current.CancellationToken);

            var before = await File.ReadAllBytesAsync(workPath, TestContext.Current.CancellationToken);

            fixture.Run(() =>
            {
                runtime.Window.DataFolderInput.Text = occupied;
                operation = runtime.ExecuteAsync("save-settings");
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => operation);
            Assert.Equal(before, await File.ReadAllBytesAsync(workPath, TestContext.Current.CancellationToken));
            Assert.Equal(destination, runtime.Session.Settings.WorkDirectory);
        }
        finally
        {
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Theory]
    [InlineData(2, "79228162514264337593543950335", "1")]
    [InlineData(3, "79228162514264337593543950335", "1")]
    [InlineData(7, "79228162514264337593543950335", "1")]
    [InlineData(7, "-1", "1")]
    public void EditorRejectsExtremeNumbersWithoutThrowing(int field, string value, string overtime)
    {
        fixture.Run(() =>
        {
            var owner = new MainWindow();
            Exception? failure = null;
            string? message = null;

            owner.Show();
            _ = owner.Dispatcher.BeginInvoke(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().Single(window => window.Owner == owner);

                try
                {
                    var panel = (StackPanel)((ScrollViewer)dialog.Content).Content;
                    var inputs = panel.Children.OfType<TextBox>().ToArray();

                    inputs[3].Text = overtime;
                    inputs[field].Text = value;

                    var save = panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().First();

                    save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    message = panel.Children.OfType<TextBlock>().Last().Text;
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    dialog.Close();
                }
            }, DispatcherPriority.ApplicationIdle);

            try
            {
                Assert.Null(EditorDialogs.Entry(owner, new() { HourlyRate = 1000 }, null));
                Assert.Null(failure);
                Assert.False(string.IsNullOrWhiteSpace(message));
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void DecorativeScrollViewersCannotTakeFocus()
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();
            var live = new TrayEarnerWindow();
            var tasks = new TaskManagerWindow(new());

            try
            {
                foreach (var index in ScrollPages)
                {
                    window.Pages.SelectedIndex = index;
                    Arrange(window.RootSurface);
                    Assert.False(Visuals(window.RootSurface).OfType<ScrollViewer>().First().Focusable);
                }

                Assert.False(live.LiveScroll.Focusable);
                Assert.False(((ScrollViewer)tasks.Content).Focusable);
                Assert.True(window.RateInput.Focusable);
                Assert.True(window.TaskChoice.Focusable);
            }
            finally
            {
                window.Close();
                live.Exit();
                tasks.Close();
            }
        });
    }

    [Fact]
    public async Task MissingConfiguredFolderFailsStartupAndPreservesReturningHistory()
    {
        var workDirectory = Path.Combine(_root, "work");
        var offlineDirectory = Path.Combine(_root, "offline");
        using var settings = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), value => value.Validate());
        using var work = new JsonStore<WorkData>(Path.Combine(workDirectory, "work.json"), value => value.Validate());
        var original = new WorkData(1, [], [new(new(2026, 9, 14), 8, 50, "USD")]);

        await settings.SaveAsync(new() { WorkDirectory = workDirectory }, TestContext.Current.CancellationToken);
        await work.SaveAsync(original, TestContext.Current.CancellationToken);
        Directory.Move(workDirectory, offlineDirectory);

        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true));
            operation = runtime.InitializeAsync();
        });

        try
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => operation);
            Assert.False(runtime.Initialized);
            Directory.Move(offlineDirectory, workDirectory);
            fixture.Run(() => operation = runtime.CloseAsync(false));
            await operation;
            Assert.Equal(original.Days, (await work.LoadAsync(TestContext.Current.CancellationToken))!.Days);
        }
        finally
        {
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Fact]
    public async Task ManualUpdateAllowsPauseTaskSwitchAndSavedExit()
    {
        using var settings = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), value => value.Validate());

        await settings.SaveAsync(new() { Tasks = ["A", "B"], SelectedTask = "A", AutomaticUpdates = false, ConfirmExit = false }, TestContext.Current.CancellationToken);

        var handler = new WaitingUpdateHandler();
        var updates = new UpdateService(new(_root, false, true), handler, eligible: true);
        var clock = new TestClock();
        var runtime = await CreateRuntimeAsync(clock, updates);
        Task operation = Task.CompletedTask;
        Task check = Task.CompletedTask;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await updates.RestorePendingAsync();
            fixture.Run(() =>
            {
                runtime.Closed += () => closed.TrySetResult();
                operation = runtime.ExecuteAsync("toggle");
            });
            await operation;
            fixture.Run(() => check = runtime.ExecuteAsync("check-update"));
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            clock.Advance(TimeSpan.FromSeconds(10));
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            Assert.False(runtime.Session.Running);
            fixture.Run(() =>
            {
                runtime.Window.LiveView.SelectTask("B");
                operation = runtime.ExecuteAsync("select-task");
            });
            await operation;
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(check.IsCompleted);
            fixture.Run(() => operation = runtime.ExecuteAsync("exit"));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await check;

            using var work = new JsonStore<WorkData>(Path.Combine(_root, "work.json"), value => value.Validate());
            var saved = (await work.LoadAsync(TestContext.Current.CancellationToken))!;

            Assert.Equal(10 * TimeSpan.TicksPerSecond, saved.Entries.Single(entry => entry.Task == "A").TotalTicks);
            Assert.Equal(5 * TimeSpan.TicksPerSecond, saved.Entries.Single(entry => entry.Task == "B").TotalTicks);
        }
        finally
        {
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OpeningAtBoundaryFinishesCheckpointAndStopsTimers(bool midnight, bool failSave)
    {
        using var settings = new JsonStore<AppSettings>(Path.Combine(_root, "settings.json"), value => value.Validate());

        await settings.SaveAsync(new() { DailyHours = 1m / 60, Overtime = OvertimePolicy.NotAllowed }, TestContext.Current.CancellationToken);

        var clock = new TestClock();

        if (midnight)
        {
            clock.Utc = new(2026, 9, 14, 23, 59, 0, TimeSpan.Zero);
        }

        var runtime = await CreateRuntimeAsync(clock);
        Task operation = Task.CompletedTask;
        var path = Path.Combine(_root, "work.json");
        FileStream? locked = null;

        try
        {
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            clock.Advance(TimeSpan.FromSeconds(30));
            fixture.Run(() =>
            {
                runtime.Session.Settle();
                operation = runtime.SaveWorkAsync();
            });
            await operation;

            if (failSave)
            {
                locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            clock.Advance(TimeSpan.FromSeconds(31));
            fixture.Run(runtime.Open);

            using var work = new JsonStore<WorkData>(path, value => value.Validate());
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (true)
            {
                var failed = false;

                fixture.Run(() => failed = runtime.Window.StatusText.Text.Contains(Strings.Current["SaveFailedTrackingPausedStartToRetry"], StringComparison.Ordinal));

                if (failSave
                    ? failed
                    : (await work.LoadAsync(TestContext.Current.CancellationToken))!.Entries.Sum(entry => entry.TotalTicks) >= TimeSpan.TicksPerMinute - 1)
                {
                    break;
                }

                Assert.True(DateTime.UtcNow < deadline, "The boundary checkpoint did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            fixture.Run(() =>
            {
                Assert.False(runtime.Session.Running);
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.False(runtime.CheckpointTimerEnabled);
                Assert.False(runtime.Session.NewDayPending);
                Assert.False(runtime.Session.DailyLimitReached);
            });
        }
        finally
        {
            locked?.Dispose();
            await DisposeRuntimeAsync(runtime);
        }
    }

    [Fact]
    public void WorkAreaMinimumsShrinkAndRecoverOnTheRealWindow()
    {
        fixture.Run(() =>
        {
            var window = new MainWindow();

            try
            {
                window.Show();
                WindowWorkAreaPlacement.ConstrainMinimumSize(window, new(500, 450));
                window.Width = 500;
                window.Height = 450;
                window.UpdateLayout();
                // Native pixel rounding can produce a fractional DIP at mixed scaling.
                Assert.InRange(Math.Abs(window.ActualWidth - 500), 0, 1);
                Assert.InRange(Math.Abs(window.ActualHeight - 450), 0, 1);
                WindowWorkAreaPlacement.ConstrainMinimumSize(window, new(1920, 1080));
                Assert.Equal(540, window.MinWidth);
                Assert.Equal(520, window.MinHeight);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class WaitingUpdateHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new(System.Net.HttpStatusCode.OK);
        }
    }

    private async Task<AppRuntime> CreateRuntimeAsync(TimeProvider? clock = null, UpdateService? updates = null)
    {
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(_root, false, true), clock, updates: updates);
            operation = runtime.InitializeAsync();
        });
        await operation;

        return runtime;
    }

    private async Task DisposeRuntimeAsync(AppRuntime runtime)
    {
        Task operation = Task.CompletedTask;

        fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
        await operation;
        fixture.Run(() => runtime.Window.Close());
    }

    private static void Arrange(FrameworkElement element)
    {
        element.Measure(new(708, 764));
        element.Arrange(new Rect(0, 0, 708, 764));
        element.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        yield return parent;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, index)))
            {
                yield return child;
            }
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
