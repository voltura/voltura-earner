using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Updates;
using VolturaEarner.Platform;
using VolturaEarner.Ui;
using Forms = System.Windows.Forms;

namespace VolturaEarner;

internal sealed class AppRuntime : IAsyncDisposable
{
    private readonly AppPaths _paths;
    private readonly TimeProvider _clock;
    private readonly Func<Window, AppSettings, AppSettings> _editTasks;
    private readonly Action<string> _openReport;
    private readonly JsonStore<AppSettings> _settingsStore;
    private JsonStore<WorkData> _workStore;
    private FileStream? _workLock;
    private readonly ApplicationLog _log;
    private readonly UpdateService _updates;
    private readonly DispatcherTimer _display;
    private readonly DispatcherTimer _checkpoint;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _exportStop;
    private CancellationTokenSource? _historyStop;
    private Task _exportTask = Task.CompletedTask;
    private Task _historyTask = Task.CompletedTask;
    private Task _checkpointTask = Task.CompletedTask;
    private Forms.NotifyIcon? _tray;
    private TrayIconVisibilityPromoter? _trayVisibilityPromoter;
    private System.Drawing.Icon? _icon;
    private Forms.ContextMenuStrip? _trayMenu;
    private bool? _trayMenuDark;
    private string? _trayMenuLanguage;
    private long _savedRevision = -1;
    private bool _commandBusy;
    private bool _closing;
    private bool _sessionEnding;
    private bool _disposed;
    private bool _hiddenExplained;
    private string? _startupDataPath;
    public bool Initialized { get; private set; }
    public event Action? Closed;
    public MainWindow Window { get; } = new();
    internal TrayEarnerWindow LiveWindow { get; } = new();
    public TrackingSession Session { get; private set; } = new(new(), WorkData.Empty);
    internal bool DisplayTimerEnabled => _display.IsEnabled;
    internal bool CheckpointTimerEnabled => _checkpoint.IsEnabled;

    public AppRuntime(AppPaths paths, TimeProvider? clock = null, Func<Window, AppSettings, AppSettings>? editTasks = null, UpdateService? updates = null, Action<string>? openReport = null)
    {
        _paths = paths;
        _clock = clock ?? TimeProvider.System;
        _editTasks = editTasks ?? EditorDialogs.Tasks;
        _openReport = openReport ?? Launch;
        _settingsStore = new(Path.Combine(paths.Data, "settings.json"), s => s.Validate());
        _workStore = new(Path.Combine(paths.Data, "work.json"), d => d.Validate());
        _log = new(paths.Data);
        _updates = updates ?? new(paths, log: _log);
        _display = new(DispatcherPriority.Background, Window.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };

        _checkpoint = new(DispatcherPriority.Background, Window.Dispatcher);
        _display.Tick += DisplayTick;
        _checkpoint.Tick += CheckpointTick;
        Window.ActionRequested += ActionRequested;
        Window.IsVisibleChanged += VisibilityChanged;
        Window.StateChanged += StateChanged;
        Window.Closing += Closing;
        LiveWindow.IsVisibleChanged += VisibilityChanged;
        LiveWindow.OpenMainRequested += Open;
        LiveWindow.LiveView.ActionRequested += LiveActionRequested;
        _updates.Changed += UpdateChanged;

        if (!paths.Isolated)
        {
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.TimeChanged += TimeChanged;
            SystemEvents.UserPreferenceChanged += PreferenceChanged;
        }
    }

    public async Task InitializeAsync()
    {
        Window.Pages.IsEnabled = false;
        _startupDataPath = _settingsStore.FilePath;

        var storedSettings = await _settingsStore.LoadAsync(_stop.Token);
        var settings = storedSettings ?? new();

        ThemeManager.Apply(settings.Theme);
        Strings.Current.SetLanguage(settings.Language);

        if (storedSettings is null)
        {
            settings = settings with { Tasks = [Strings.Current["DefaultTask"]], SelectedTask = Strings.Current["DefaultTask"] };
        }

        _workStore.Dispose();
        _workStore = new(WorkPath(settings), d => d.Validate());
        _startupDataPath = _workStore.FilePath;

        _workLock = await Task.Run(() => WorkHistoryLock.Acquire(_workStore.FilePath, requireDirectory: !string.IsNullOrWhiteSpace(settings.WorkDirectory)), _stop.Token);

        var data = await _workStore.LoadAsync(requireDirectory: !string.IsNullOrWhiteSpace(settings.WorkDirectory), _stop.Token) ?? WorkData.Empty;

        _startupDataPath = null;
        Session = await Task.Run(() => new TrackingSession(settings, data, _clock), _stop.Token);
        _savedRevision = Session.Revision;
        _log.Enabled = settings.Logging;
        Window.Populate(settings);
        LiveWindow.LiveView.Populate(settings);
        Window.Render(Session);
        LiveWindow.Render(Session);

        if (!_paths.Isolated)
        {
            CreateTray();
        }

        Initialized = true;
        Window.Pages.IsEnabled = true;
        Window.StatusText.Text = settings.FirstRun
            ? Strings.Current["SetYourRatesToGetStarted"]
            : Strings.Current["Ready"];
        Window.Pages.SelectedIndex = settings.FirstRun
            ? 2
            : 0;
        _updates.Start(settings.AutomaticUpdates);
        UpdateChanged();
        Refresh();
    }

    private string WorkPath(AppSettings settings) => Path.GetFullPath(Path.Combine(string.IsNullOrWhiteSpace(settings.WorkDirectory)
        ? _paths.Data
        : settings.WorkDirectory, "work.json"));

    private void CreateTray()
    {
        using var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/App.ico")).Stream;

        _icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize);
        _trayMenu = new() { ShowImageMargin = false };

        foreach (var item in new[] { (Strings.Current["OpenVolturaEarner"], "open"), (Strings.Current["StartPause"], "toggle"), (Strings.Current["AboutAndUpdates"], "about"), ("Exit", "exit") })
        {
            if (item.Item2 == "exit")
            {
                _trayMenu.Items.Add(new Forms.ToolStripSeparator());
            }

            _trayMenu.Items.Add(item.Item1, null, (_, _) => Window.Dispatcher.BeginInvoke(() => ActionRequested(item.Item2)));
        }

        foreach (var item in _trayMenu.Items.OfType<Forms.ToolStripMenuItem>())
        {
            var padding = item.Padding;

            item.Padding = new(padding.Left, padding.Top + 4, padding.Right, padding.Bottom + 4);
        }

        _trayMenu.Opening += (_, _) => UpdateTrayMenuTheme();
        UpdateTrayMenuTheme();

        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "Voltura Earner", Visible = true, ContextMenuStrip = _trayMenu };

        _tray.BalloonTipClicked += (_, _) => Window.Dispatcher.BeginInvoke(Open);

        _trayVisibilityPromoter = new(Window.Dispatcher, () =>
        {
            if (!_disposed && _tray is not null)
            {
                _tray.Visible = false;
                _tray.Visible = true;
            }
        });
        _trayVisibilityPromoter.Start();

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                var position = Forms.Cursor.Position;

                Window.Dispatcher.BeginInvoke(() =>
                {
                    LiveWindow.Render(Session);
                    LiveWindow.Toggle(new(position.X, position.Y, 1, 1));
                    Refresh();
                });
            }
        };
    }

    private void UpdateTrayMenuTheme()
    {
        var dark = ThemeManager.IsTaskbarDark();

        if (_trayMenu is null || (_trayMenuDark == dark && _trayMenuLanguage == Strings.Current.LanguageId))
        {
            return;
        }

        var renderer = new TrayMenuRenderer(dark);

        _trayMenu.BackColor = renderer.Surface;
        _trayMenu.ForeColor = renderer.Text;
        _trayMenu.Renderer = renderer;

        var labels = new[] { Strings.Current["OpenVolturaEarner"], Strings.Current["StartPause"], Strings.Current["AboutAndUpdates"], Strings.Current["Exit"] + (Strings.Current.LanguageId == "en"
            ? ""
            : " / Exit") };
        var index = 0;

        foreach (Forms.ToolStripItem item in _trayMenu.Items)
        {
            if (item is Forms.ToolStripMenuItem)
            {
                item.Text = labels[index++];
            }

            item.BackColor = renderer.Surface;
            item.ForeColor = renderer.Text;
        }

        _trayMenuDark = dark;
        _trayMenuLanguage = Strings.Current.LanguageId;
    }

    public void Open()
    {
        Window.Show();
        Window.WindowState = WindowState.Normal;
        WindowWorkAreaPlacement.EnsureVisibleOnCurrentMonitor(Window);
        Window.Activate();
        Refresh();
    }

    private void LiveActionRequested(string action)
    {
        if (action == "select-task")
        {
            Window.LiveView.SelectTask((string?)LiveWindow.LiveView.TaskChoice.SelectedItem ?? Session.Settings.SelectedTask);
        }

        if (action == "tasks")
        {
            Open();
        }

        ActionRequested(action);
    }

    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Schedule();
    private void StateChanged(object? sender, EventArgs e)
    {
        if (Initialized && Window.WindowState == WindowState.Minimized && Session.Settings.MinimizeToTray)
        {
            Window.Hide();
        }

        Schedule();
    }

    private async void DisplayTick(object? sender, EventArgs e) => await RefreshLiveAsync();

    internal async Task RefreshLiveAsync()
    {
        if (_closing || _sessionEnding || _disposed)
        {
            return;
        }

        Session.Settle();
        Refresh();
        HandleNotices();

        if (!Session.Running && !_closing && !_sessionEnding && !_disposed)
        {
            await _checkpointTask;

            if (!_closing && !_sessionEnding && !_disposed)
            {
                _checkpointTask = CheckpointAsync();
                await _checkpointTask;
            }
        }
    }

    private async void CheckpointTick(object? sender, EventArgs e)
    {
        _checkpoint.Stop();

        if (_closing || _sessionEnding || _disposed)
        {
            return;
        }

        if (_checkpointTask.IsCompleted)
        {
            _checkpointTask = CheckpointAsync();
            await _checkpointTask;
            Schedule();
        }
    }

    private async Task CheckpointAsync()
    {
        try
        {
            Session.Settle();
            HandleNotices();
            await SaveWorkAsync();
            // A visible view can settle a boundary while the earlier snapshot is being written.
            Session.Settle();
            HandleNotices();

            if (!Session.Running)
            {
                await SaveWorkAsync();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (IsExpected(error))
        {
            Session.Pause();
            ReportError(Strings.Current["SaveFailedTrackingPausedStartToRetry"], error);
        }
        finally
        {
            Refresh();
        }
    }

    private void HandleNotices()
    {
        var newDay = Session.NewDayPending;

        if (Session.OvertimePending || Session.DailyLimitReached)
        {
            var message = Session.DailyLimitReached
                ? Strings.Current["DailyLimitReachedOvertimeIsDisabled"]
                : Strings.Current.Format("OvertimeNotice", $"{Session.Settings.OvertimeRate:N2} {Session.Settings.Currency}");

            _tray?.ShowBalloonTip(8000, "Voltura Earner", message, Forms.ToolTipIcon.Info);

            if (Session.Settings.PlaySounds && !_paths.Isolated)
            {
                SystemSounds.Exclamation.Play();
            }

            Window.StatusText.Text = message;
        }

        Session.DismissNotices();

        if (newDay)
        {
            Window.StatusText.Text = Strings.Current["NewDayTrackingPaused"];

            if (!_paths.Isolated)
            {
                _tray?.ShowBalloonTip(8000, Strings.Current["NewDay"], Strings.Current["TrackingPausedStartWhenReady"], Forms.ToolTipIcon.Info);
            }
        }
    }

    private void Refresh()
    {
        if (!Initialized || _disposed)
        {
            return;
        }

        if (Window.IsVisible)
        {
            Window.Render(Session);
        }

        if (LiveWindow.IsVisible)
        {
            LiveWindow.Render(Session);
        }

        if (_tray is not null)
        {
            var summary = Session.Summary();

            _tray.Text = $"Voltura Earner · {(Session.Running
                ? Strings.Current["Tracking"]
                : Strings.Current["Paused"])}\n{Strings.Current.Format("WorkedToday", ReportService.Duration(summary.RegularTicks + summary.OvertimeTicks))}";
        }

        Schedule();
    }

    private void Schedule()
    {
        _display.Stop();

        if (!Initialized || _closing || _sessionEnding || _disposed)
        {
            _checkpoint.Stop();

            return;
        }

        if (Session.Running && ((Window.IsVisible && Window.WindowState != WindowState.Minimized) || LiveWindow.IsVisible))
        {
            _display.Start();
        }

        if (!Session.Running)
        {
            // Rendering can settle the final interval before the scheduled checkpoint runs.
            if (_checkpoint.IsEnabled && (Session.NewDayPending || Session.DailyLimitReached || (Session.Settings.SaveWorkLog && Session.Revision != _savedRevision)))
            {
                _checkpoint.Interval = TimeSpan.FromMilliseconds(50);
            }
            else
            {
                _checkpoint.Stop();
            }

            return;
        }

        if (!_checkpoint.IsEnabled && _checkpointTask.IsCompleted)
        {
            var summary = Session.Summary();
            var untilTarget = (double)summary.TargetHours * 3600 - TimeSpan.FromTicks(summary.RegularTicks + summary.OvertimeTicks).TotalSeconds + 0.01;
            var now = _clock.GetLocalNow().DateTime;
            var untilMidnight = (now.Date.AddDays(1) - now).TotalSeconds;
            var seconds = Math.Min(30, untilMidnight);

            if (untilTarget > 0)
            {
                seconds = Math.Min(seconds, untilTarget);
            }

            _checkpoint.Interval = TimeSpan.FromSeconds(Math.Max(0.05, seconds));
            _checkpoint.Start();
        }
    }

    internal Task SaveWorkAsync() => SaveWorkAsync(_stop.Token);

    private async Task SaveWorkAsync(CancellationToken token)
    {
        if (!Session.Settings.SaveWorkLog)
        {
            return;
        }

        await _saveGate.WaitAsync(token);

        try
        {
            var snapshot = await Task.Run(Session.Snapshot, token);

            if (snapshot.Revision == _savedRevision)
            {
                return;
            }

            await _workStore.SaveAsync(snapshot.Data, token);
            _savedRevision = snapshot.Revision;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async void ActionRequested(string action)
    {
        try
        {
            await ExecuteAsync(action);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (IsExpected(error))
        {
            ReportError(Strings.Current["TheActionCouldNotComplete"], error);
        }
    }

    internal async Task ExecuteAsync(string action)
    {
        if (action == "open")
        {
            Open();

            return;
        }

        if (action == "about")
        {
            Open();
            Window.Pages.SelectedIndex = 3;

            return;
        }

        if (action == "cancel-export")
        {
            _exportStop?.Cancel();

            return;
        }

        if (action == "exit")
        {
            _ = Window.Dispatcher.BeginInvoke(RequestExit);

            return;
        }

        if (!Initialized || _closing || _sessionEnding)
        {
            return;
        }

        if (action == "pause-for-sleep")
        {
            Session.Pause();
            await SaveWorkAsync();
            Refresh();

            return;
        }

        if (action == "check-update")
        {
            if (_updates.Eligible)
            {
                await _updates.CheckAsync();
            }
            else
            {
                Launch(UpdateService.ProjectUrl + "/releases/latest");
            }

            return;
        }

        if (action.StartsWith("export", StringComparison.Ordinal) && action != "export-folder")
        {
            if (!_exportTask.IsCompleted)
            {
                Window.StatusText.Text = Strings.Current["AnExportIsAlreadyRunning"];

                return;
            }

            await ExportAsync(action == "export-today"
                ? ReportPeriod.Today
                : Window.Period, true, false);

            return;
        }

        if (action == "refresh-history")
        {
            _historyTask = RefreshHistoryAsync();

            var historyTask = _historyTask;

            try
            {
                await historyTask;
            }
            finally
            {
                if (ReferenceEquals(_historyTask, historyTask))
                {
                    _historyTask = Task.CompletedTask;
                }
            }

            return;
        }

        if (_commandBusy)
        {
            if (action == "select-task")
            {
                SyncTaskSelection();
            }

            Window.StatusText.Text = Strings.Current["PleaseWaitMore"];

            return;
        }

        _commandBusy = true;

        try
        {
            switch (action)
            {
                case "topmost":
                    await SaveSettingsAsync(Session.Settings with { AlwaysOnTop = Window.Topmost });
                    Window.TopmostCheck.IsChecked = Session.Settings.AlwaysOnTop;
                    break;
                case "toggle":
                    if (Session.Running)
                    {
                        Session.Pause();
                        await SaveWorkAsync();
                    }
                    else
                    {
                        await SaveWorkAsync();
                        Session.Start((string?)Window.TaskChoice.SelectedItem ?? Session.Settings.SelectedTask, false);
                    }

                    break;
                case "start-regular":
                case "start-overtime":
                    await SaveWorkAsync();
                    Session.Start((string?)Window.TaskChoice.SelectedItem ?? Session.Settings.SelectedTask, action == "start-overtime");
                    break;
                case "select-task":
                    await SaveSettingsAsync(Session.Settings with { SelectedTask = (string?)Window.TaskChoice.SelectedItem ?? Session.Settings.SelectedTask });

                    break;
                case "tasks":
                    await SaveSettingsAsync(_editTasks(Window, Session.Settings));
                    break;
                case "save-settings":
                    var settings = Window.ReadSettings(Session.Settings);
                    var applyToday = Window.ApplyTodayCheck.IsChecked == true;

                    Session.Pause();
                    await SaveWorkAsync();
                    await SaveSettingsAsync(settings);

                    if (applyToday)
                    {
                        var snapshot = await Task.Run(Session.Snapshot);
                        var data = snapshot.Data with
                        {
                            Days = snapshot.Data.Days.Select(d => d.Date == Session.Today
                                ? d with { TargetHours = settings.DailyHours, Cost = settings.DailyCost, Currency = settings.Currency }
                                : d).ToArray()
                        };

                        await CommitDraftAsync(data);
                    }

                    Window.Populate(settings);
                    Window.StatusText.Text = Strings.Current["PreferencesSavedTrackingPaused"];
                    break;
                case "automatic-updates":
                    await SaveSettingsAsync(Session.Settings with { AutomaticUpdates = Window.AutomaticUpdatesCheck.IsChecked == true });
                    break;
                case "reset":
                case "erase":
                    if (Confirm(action == "reset"
                        ? Strings.Current["EraseTodaySWorkOtherDaysWillBeKept"]
                        : Strings.Current["EraseAllHistoryThisCannotBeUndone"], Strings.Current["Erase"], "\uE74D"))
                    {
                        Session.Pause();

                        if (Session.Settings.AutoShowReport && action == "reset")
                        {
                            await ExportAsync(ReportPeriod.Today, false, true);
                        }

                        var data = (await Task.Run(Session.Snapshot)).Data;
                        var replacement = action == "erase"
                            ? WorkData.Empty
                            : data with { Entries = data.Entries.Where(e => e.Date != Session.Today).ToArray(), Days = data.Days.Where(d => d.Date != Session.Today).ToArray() };

                        await CommitDraftAsync(replacement, true);
                        await RefreshHistoryAsync();
                    }

                    break;
                case "add-record":
                case "edit-record":
                case "delete-record":
                    await EditRecordAsync(action);
                    break;
                case "export-folder":
                case "data-folder":
                    var folder = new OpenFolderDialog
                    {
                        Title = Strings.Current[action == "data-folder"
                            ? "WorkDataFolder"
                            : "ExportFolder"]
                    };

                    if (folder.ShowDialog(Window) == true)
                    {
                        if (action == "data-folder")
                        {
                            Window.DataFolderInput.Text = folder.FolderName;
                        }
                        else
                        {
                            Window.ExportFolderInput.Text = folder.FolderName;
                        }
                    }

                    break;
                case "install-update":
                    Session.Pause();

                    if (!Session.Settings.SaveWorkLog)
                    {
                        throw new InvalidOperationException(Strings.Current["EnableSaveWorkHistoryBeforeInstalling"]);
                    }

                    await SaveWorkAsync();
                    await _exportTask;

                    if (await _updates.InstallAsync())
                    {
                        await CloseAsync(false);
                    }

                    break;
                case "show-log":
                    await Task.Run(async () =>
                    {
                        Directory.CreateDirectory(_paths.Data);

                        if (!File.Exists(_log.FilePath))
                        {
                            await File.WriteAllTextAsync(_log.FilePath, "Application logging is optional.\n");
                        }
                    });
                    Launch(_log.FilePath);
                    break;
                case "clear-log":
                    if (Confirm(Strings.Current["ClearTheAppLog"], Strings.Current["ClearLog"], "\uE74D"))
                    {
                        await Task.Run(() =>
                        {
                            File.Delete(_log.FilePath);
                            File.Delete(_log.FilePath + ".1");
                        });
                    }

                    break;
                case "website":
                    Launch(UpdateService.ProductUrl);
                    break;
                case "bug":
                    Launch(UpdateService.ProjectUrl + "/issues/new");
                    break;
                case "license":
                    Launch(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"));
                    break;
                case "support":
                    Launch("https://www.paypal.com/donate?hosted_button_id=7PN65YXN64DBG");
                    break;
                case "coffee":
                    Launch("https://ko-fi.com/G2G74W5F8");
                    break;
            }
        }
        finally
        {
            if (action == "select-task")
            {
                SyncTaskSelection();
            }

            _commandBusy = false;
            Refresh();
        }
    }

    private void SyncTaskSelection()
    {
        Window.LiveView.SelectTask(Session.Settings.SelectedTask);
        LiveWindow.LiveView.SelectTask(Session.Settings.SelectedTask);
    }

    private async Task SaveSettingsAsync(AppSettings settings)
    {
        settings.Validate();

        var previous = Session.Settings;
        var workPath = WorkPath(settings);
        JsonStore<WorkData>? replacement = null;
        FileStream? replacementLock = null;

        if (!string.Equals(workPath, _workStore.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (Session.Running)
            {
                throw new InvalidOperationException(Strings.Current["PauseBeforeChangingTheDataFolder"]);
            }

            replacement = new(workPath, d => d.Validate());

            try
            {
                replacementLock = await Task.Run(() => WorkHistoryLock.Acquire(workPath), _stop.Token);

                var snapshot = (await Task.Run(Session.Snapshot)).Data;
                var existing = await replacement.LoadAsync();

                if (existing is null)
                {
                    await replacement.SaveAsync(snapshot, false, _stop.Token);
                }
                else if (!await Task.Run(() => existing.Entries.SequenceEqual(snapshot.Entries) && existing.Days.SequenceEqual(snapshot.Days), _stop.Token))
                {
                    throw new InvalidOperationException(Strings.Current["ThisFolderHasWorkHistoryChooseAnEmptyFolder"]);
                }
                // An identical copy is safe to reuse after a failed settings save.
            }
            catch
            {
                replacement.Dispose();
                replacementLock?.Dispose();
                throw;
            }
        }

        try
        {
            if (!_paths.Isolated && settings.StartWithWindows != previous.StartWithWindows)
            {
                await Task.Run(() => StartupRegistration.Apply(settings.StartWithWindows));
            }

            await _settingsStore.SaveAsync(settings, _stop.Token);
        }
        catch
        {
            replacement?.Dispose();
            replacementLock?.Dispose();

            if (!_paths.Isolated && settings.StartWithWindows != previous.StartWithWindows)
            {
                await Task.Run(() => StartupRegistration.Apply(previous.StartWithWindows));
            }

            throw;
        }

        if (replacement is not null)
        {
            // Settings already point at the new folder; finish transferring ownership.
            await _saveGate.WaitAsync();

            try
            {
                _workStore.Dispose();
                _workStore = replacement;
                _workLock?.Dispose();
                _workLock = replacementLock;
            }
            finally
            {
                _saveGate.Release();
            }
        }

        Session.ApplySettings(settings, preserveRunning: true);

        if (!previous.SaveWorkLog && settings.SaveWorkLog)
        {
            _savedRevision = -1;
        }

        try
        {
            await SaveWorkAsync();
        }
        catch
        {
            Session.Pause();
            throw;
        }

        _log.Enabled = settings.Logging;
        Strings.Current.SetLanguage(settings.Language);
        Window.ApplySettings(settings);
        LiveWindow.LiveView.Populate(settings);
        ThemeManager.Apply(settings.Theme);
        UpdateTrayMenuTheme();
        _updates.Start(settings.AutomaticUpdates);
        UpdateChanged();
    }

    private async Task CommitDraftAsync(WorkData draft, bool force = false)
    {
        await Task.Run(draft.Validate);
        await _saveGate.WaitAsync();

        try
        {
            if (Session.Settings.SaveWorkLog || force)
            {
                await _workStore.SaveAsync(draft, _stop.Token);
            }

            await Task.Run(() => Session.Replace(draft), _stop.Token);
            _savedRevision = Session.Settings.SaveWorkLog || force
                ? Session.Revision
                : -1;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task EditRecordAsync(string action)
    {
        var group = Window.WorkGrid.SelectedItem as WorkLogGroup;
        WorkEntry? selected = null;

        if (action != "add-record" && group is null)
        {
            Window.StatusText.Text = Strings.Current["SelectAWorkEntryFirst"];

            return;
        }

        Session.Pause();
        await SaveWorkAsync();

        if (action != "add-record")
        {
            var current = (await Task.Run(Session.Snapshot)).Data;

            group = WorkLogGroup.Create(current.Entries.Where(entry => entry.Date == group!.Day && entry.Task == group.Task)).SingleOrDefault();

            if (group is null)
            {
                return;
            }

            selected = group!.Entries.Length == 1
                ? group.Entries[0]
                : EditorDialogs.SelectEntry(Window, group, action == "delete-record");

            if (selected is null)
            {
                return;
            }
        }

        var replacement = action == "delete-record"
            ? null
            : EditorDialogs.Entry(Window, Session.Settings, selected);

        if (action == "delete-record"
            ? !Confirm(Strings.Current["DeleteTheSelectedWorkEntry"], Strings.Current["Delete"], "\uE74D")
            : replacement is null)
        {
            return;
        }

        var data = (await Task.Run(Session.Snapshot)).Data;
        var entries = data.Entries.Where(e => e.Id != selected?.Id).ToList();
        var days = data.Days.ToList();

        if (replacement is not null)
        {
            entries.Add(replacement);

            if (days.All(d => d.Date != replacement.Date))
            {
                days.Add(new(replacement.Date, Session.Settings.DailyHours, Session.Settings.DailyCost, Session.Settings.Currency));
            }
        }

        var occupied = entries.Select(e => e.Date).ToHashSet();

        await CommitDraftAsync(new(1, entries.ToArray(), days.Where(d => occupied.Contains(d.Date)).ToArray()));
        await RefreshHistoryAsync();
        Window.StatusText.Text = Strings.Current["WorkLogSavedTrackingPaused"];
    }

    private async Task RefreshHistoryAsync()
    {
        _historyStop?.Cancel();

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);

        _historyStop = cancellation;

        var period = Window.Period;
        var today = Session.Today;
        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

        try
        {
            var result = await Task.Run(() =>
            {
                var entries = ReportService.Select(Session.Snapshot().Data, today, period, firstDay);

                return (Groups: WorkLogGroup.Create(entries), Totals: WorkLogTotals.Create(entries));
            }, cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            Window.WorkGrid.ItemsSource = result.Groups;
            Window.WorkTotals.DataContext = result.Totals;
        }
        finally
        {
            if (ReferenceEquals(_historyStop, cancellation))
            {
                _historyStop = null;
            }

            cancellation.Dispose();
        }
    }

    internal async Task ExportAsync(ReportPeriod period, bool choosePath, bool autoOpen, CancellationToken token = default)
    {
        if (!_exportTask.IsCompleted)
        {
            throw new InvalidOperationException(Strings.Current["AnExportIsAlreadyRunning"]);
        }

        // Claim ownership before a native file dialog can pump another command.

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _exportTask = completion.Task;

        try
        {
            await ExportCoreAsync(period, choosePath, autoOpen, token);
        }
        finally
        {
            completion.SetResult();
        }
    }

    private async Task ExportCoreAsync(ReportPeriod period, bool choosePath, bool autoOpen, CancellationToken token)
    {
        var directory = string.IsNullOrWhiteSpace(Session.Settings.ExportDirectory)
            ? Path.Combine(_paths.Data, "Exports")
            : Session.Settings.ExportDirectory;
        var path = Path.Combine(directory, $"Earner-{Session.Today:yyyy-MM-dd}-{period}.xlsx");

        if (choosePath)
        {
            var dialog = new SaveFileDialog { Title = Strings.Current["ExportExcelMore"], Filter = Strings.Current["ExcelWorkbookXlsxXlsx"], FileName = Path.GetFileName(path), InitialDirectory = directory, DefaultExt = ".xlsx" };

            if (dialog.ShowDialog(Window) != true)
            {
                return;
            }

            path = dialog.FileName;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, token);

        _exportStop = cancellation;
        Window.CancelOperation.Visibility = Visibility.Visible;
        Window.StatusText.Text = Strings.Current["ExportingMore"];

        try
        {
            Session.Settle();

            var snapshot = await Task.Run(Session.Snapshot, cancellation.Token);

            path = await ReportService.ExportAsync(snapshot.Data, Session.Today, period, CultureInfo.CurrentCulture, path, Strings.Current.LanguageId, overwrite: choosePath, cancellation.Token);
            Window.StatusText.Text = Strings.Current.Format("ReportSaved", path);

            if (autoOpen || choosePath)
            {
                try
                {
                    _openReport(path);
                }
                catch (Exception error) when (IsExpected(error))
                {
                    ReportError(Strings.Current.Format("ReportSaved", path), error);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Window.StatusText.Text = Strings.Current["ExportCanceled"];
            throw;
        }
        finally
        {
            _exportStop = null;
            Window.CancelOperation.Visibility = Visibility.Collapsed;
        }
    }

    private bool Confirm(string text, string action, string glyph) => !_paths.Isolated && ConfirmationWindow.Confirm(Window, text, action, glyph);
    private static void Launch(string target)
    {
        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
    private void UpdateChanged() => Window.Dispatcher.BeginInvoke(() => Window.UpdateState(_updates.State, _updates.Eligible));
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            Session.Pause();
            Window.Dispatcher.BeginInvoke(() => ActionRequested("pause-for-sleep"));
        }
    }
    private void TimeChanged(object? sender, EventArgs e) => Window.Dispatcher.BeginInvoke(() => DisplayTick(sender, e));
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Window.Dispatcher.BeginInvoke(() => ThemeManager.Apply(Session.Settings.Theme));
    private void Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        e.Cancel = true;

        if (_sessionEnding)
        {
            return;
        }

        if (!Initialized)
        {
            _ = Window.Dispatcher.BeginInvoke(RequestExit);

            return;
        }

        Window.Hide();
        TooltipLifetime.Dismiss();

        if (!_hiddenExplained && !_paths.Isolated)
        {
            _hiddenExplained = true;
            _tray?.ShowBalloonTip(8000, "Voltura Earner", Strings.Current["HiddenToTray"], Forms.ToolTipIcon.Info);
        }
    }

    private async void RequestExit()
    {
        if (_closing || _sessionEnding)
        {
            return;
        }

        if (_commandBusy)
        {
            Window.StatusText.Text = Strings.Current["PleaseWaitBeforeClosing"];

            return;
        }

        if (Initialized && Session.Settings.ConfirmExit && !Confirm(Session.Settings.SaveWorkLog
            ? Strings.Current["CloseEarner"]
            : Strings.Current["CloseEarnerUnsavedWorkWillBeLost"], Strings.Current["Close"], "\uE8BB"))
        {
            return;
        }

        try
        {
            await CloseAsync(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (IsExpected(error))
        {
            ReportError(Strings.Current["CouldNotSaveAndCloseTryAgain"], error);
        }
    }

    internal async Task<bool> PrepareForSessionEndAsync(CancellationToken token)
    {
        if (_commandBusy || _closing || _sessionEnding)
        {
            Window.StatusText.Text = Strings.Current["PleaseWaitBeforeClosing"];

            return false;
        }

        _sessionEnding = true;

        try
        {
            Session.Pause();
            Schedule();

            if (Initialized)
            {
                await SaveWorkAsync(token);
                await _exportTask.WaitAsync(token);

                if (Session.Settings.AutoShowReport)
                {
                    // Save the report without starting another app during Windows shutdown.
                    await ExportAsync(ReportPeriod.Today, false, false, token);
                }
            }

            token.ThrowIfCancellationRequested();

            return true;
        }
        catch (Exception error)
        {
            // A failed session-ending save must decline shutdown, not lose data or crash.
            ReportError(Strings.Current["CouldNotSaveAndCloseTryAgain"], error);

            return false;
        }
        finally
        {
            _sessionEnding = false;
            Refresh();
        }
    }

    public async Task CloseAsync(bool normalClose)
    {
        if (_closing || _sessionEnding)
        {
            return;
        }

        _closing = true;

        try
        {
            Session.Pause();
            Schedule();

            if (Initialized)
            {
                await SaveWorkAsync();
                await _exportTask;

                if (normalClose && Session.Settings.AutoShowReport)
                {
                    await ExportAsync(ReportPeriod.Today, false, true);
                }
            }

            await DisposeAsync();
            Window.Close();
            Closed?.Invoke();
        }
        catch
        {
            if (!_disposed)
            {
                _closing = false;
                Refresh();
            }

            throw;
        }
    }
    internal void ReportError(string context, Exception error)
    {
        _log.Record(context, error);
        Window.StatusText.Text = context + (Session.Settings.ShowErrors
            ? " " + error.Message
            : " " + Strings.Current["SeeAppLog"]);
    }
    internal void ReportStartupError(Exception error)
    {
        _log.Record(Strings.Current["CouldNotLoadYourDataFilesAreUnchanged"], error);
        Window.TrackingState.Text = Strings.Current["LoadFailed"];
        Window.StatusText.Text = Strings.Current["CouldNotLoadYourDataFilesAreUnchanged"] + " " + (_startupDataPath is null
            ? Strings.Current["StartupRecoveryInstructions"]
            : Strings.Current.Format("DataRecoveryInstructions", _startupDataPath));
    }
    private static bool IsExpected(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or System.Security.SecurityException;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        _historyStop?.Cancel();
        _exportStop?.Cancel();
        _display.Stop();
        _checkpoint.Stop();
        _display.Tick -= DisplayTick;
        _checkpoint.Tick -= CheckpointTick;
        Window.ActionRequested -= ActionRequested;
        Window.IsVisibleChanged -= VisibilityChanged;
        Window.StateChanged -= StateChanged;
        Window.Closing -= Closing;
        LiveWindow.IsVisibleChanged -= VisibilityChanged;
        LiveWindow.OpenMainRequested -= Open;
        LiveWindow.LiveView.ActionRequested -= LiveActionRequested;
        LiveWindow.Exit();
        _updates.Changed -= UpdateChanged;

        if (!_paths.Isolated)
        {
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.TimeChanged -= TimeChanged;
            SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        }

        _trayVisibilityPromoter?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _trayMenu?.Dispose();
        _icon?.Dispose();

        try
        {
            await Task.WhenAll(_checkpointTask, _exportTask, _historyTask);
        }
        catch (OperationCanceledException) { }

        await _updates.DisposeAsync();
        await _log.DisposeAsync();
        _settingsStore.Dispose();
        _workStore.Dispose();
        _workLock?.Dispose();
        _saveGate.Dispose();
        _stop.Dispose();
    }
}
