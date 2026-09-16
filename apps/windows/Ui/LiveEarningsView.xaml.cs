using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Reports;
namespace VolturaEarner.Ui;

public partial class LiveEarningsView : UserControl
{
    private static readonly string[] MenuBrushes = ["SurfaceBrush", "TextBrush", "BorderBrush", "AccentBrush", "AccentTextBrush"];
    private bool _populating;
    private bool _minimal;
    private string? _earningsTooltip;
    private string? _timeTooltip;
    internal event Action<string>? ActionRequested;
    public LiveEarningsView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                StartMenu.IsOpen = false;
            }
        };
    }
    internal bool IsStartMenuOpen => StartMenu.IsOpen;
    internal void ToggleTracking() => ActionRequested?.Invoke("toggle");
    internal void SetMinimalView(bool minimal)
    {
        _minimal = minimal;

        var details = minimal
            ? Visibility.Collapsed
            : Visibility.Visible;

        NetEarningsHeading.Visibility = details;
        GrossEarnings.Visibility = details;
        EarningsSeparator.Visibility = details;
        TimeDetails.Visibility = details;
        TargetText.Visibility = details;
        TaskCard.Visibility = details;
        EarningsCard.Margin = minimal
            ? new(0)
            : new(0, 0, 0, 12);
        NetEarnings.Margin = minimal
            ? new(0)
            : new(0, 8, 0, 4);
        DayProgress.Margin = minimal
            ? new(0, 12, 0, 0)
            : new(0, 12, 0, 8);
        UpdateSummaryTooltips();

        if (minimal)
        {
            TaskChoice.IsDropDownOpen = false;
            StartMenu.IsOpen = false;
        }
    }
    private void UpdateSummaryTooltips()
    {
        NetEarnings.ToolTip = _minimal
            ? _earningsTooltip
            : null;
        DayProgress.ToolTip = _minimal
            ? _timeTooltip
            : null;
    }
    private void StartOptionsClick(object sender, RoutedEventArgs e)
    {
        TooltipLifetime.Dismiss();
        PrepareStartMenu();
        StartMenu.PlacementTarget = StartOptions;
        StartMenu.IsOpen = true;
    }
    private void StartMenuOpening(object sender, ContextMenuEventArgs e) => PrepareStartMenu();
    internal void PrepareStartMenu()
    {
        foreach (var key in MenuBrushes)
        {
            StartMenu.Resources[key] = Application.Current.FindResource(key);
        }
    }
    private void StartModeClick(object sender, RoutedEventArgs e) => ActionRequested?.Invoke((string)((MenuItem)sender).Tag);
    private void StartMenuClosed(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is TrayEarnerWindow tray)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (!tray.IsActive)
                {
                    tray.DismissOnDeactivate();
                }
            });
        }
    }
    private void ActionClick(object sender, RoutedEventArgs e) => ActionRequested?.Invoke((string)((Button)sender).Tag);
    private void TaskChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_populating)
        {
            ActionRequested?.Invoke("select-task");
        }
    }
    internal void Populate(AppSettings settings)
    {
        _populating = true;

        try
        {
            TaskChoice.ItemsSource = settings.Tasks;
            TaskChoice.SelectedItem = settings.SelectedTask;
            StartOvertimeItem.IsEnabled = settings.Overtime != OvertimePolicy.NotAllowed;
            StartOvertimeItem.ToolTip = !StartOvertimeItem.IsEnabled && settings.ShowTooltips
                ? Strings.Current["NotAllowedStopAtLimit"]
                : null;
            DayProgress.Visibility = settings.ShowProgress
                ? Visibility.Visible
                : Visibility.Collapsed;
            PersistenceNote.Text = settings.SaveWorkLog
                ? Strings.Current["AutoSaveOn"]
                : Strings.Current["AutoSaveOffUnsavedWork"];
            StartPause.ToolTip = settings.ShowTooltips
                ? Strings.Current["StartOrPauseThisTask"]
                : null;
        }
        finally
        {
            _populating = false;
        }
    }
    internal void SelectTask(string task)
    {
        _populating = true;

        try
        {
            TaskChoice.SelectedItem = task;
        }
        finally
        {
            _populating = false;
        }
    }
    internal void Render(TrackingSession session)
    {
        ButtonIcon.SetGlyph(StartPause, session.Running
            ? "\uE769"
            : "\uE768");

        var summary = session.Summary();
        var costs = summary.Gross.Keys.Concat(summary.Net.Keys).Distinct(StringComparer.Ordinal).ToDictionary(c => c, c => summary.Gross.GetValueOrDefault(c) - summary.Net.GetValueOrDefault(c));
        var gross = Money(summary.Gross, session.Settings.Currency);

        NetEarnings.Text = Money(summary.Net, session.Settings.Currency);
        GrossEarnings.Text = Strings.Current.Format("GrossAndCost", gross, Money(costs, session.Settings.Currency));
        WorkedTime.Text = ReportService.Duration(summary.RegularTicks + summary.OvertimeTicks);
        Overtime.Text = ReportService.Duration(summary.OvertimeTicks);
        _earningsTooltip = $"{Strings.Current["Gross"]}: {gross}";

        if (costs.Values.Any(cost => cost != 0))
        {
            _earningsTooltip += $"\n{Strings.Current["DailyCost"]}: {Money(costs.Where(pair => pair.Value != 0).ToDictionary(), session.Settings.Currency)}";
        }

        _timeTooltip = $"{Strings.Current["TimeWorked"]}: {WorkedTime.Text}";

        if (summary.OvertimeTicks > 0)
        {
            _timeTooltip += $"\n{Strings.Current["Overtime"]}: {Overtime.Text}";
        }

        UpdateSummaryTooltips();
        OvertimePay.Text = Strings.Current.Format("OvertimeEarned", Money(summary.OvertimeEarnings, session.Settings.Currency));
        DayProgress.Value = Math.Min(100, (double)((summary.RegularTicks + summary.OvertimeTicks) / (summary.TargetHours * TimeSpan.TicksPerHour) * 100));
        TargetText.Text = Strings.Current.Format("RegularDay", summary.TargetHours.ToString("0.##", CultureInfo.CurrentCulture), $"{session.Settings.HourlyRate:N2} {session.Settings.Currency}", session.Settings.Overtime == OvertimePolicy.NotAllowed
            ? Strings.Current["NotAllowedStopAtLimit"]
            : Strings.Current.Format("OvertimeRate", $"{session.Settings.OvertimeRate:N2} {session.Settings.Currency}"));
        StartPause.Content = session.Running
            ? Strings.Current["Pause"]
            : Strings.Current["Start"];
    }
    private static string Money(IReadOnlyDictionary<string, decimal> values, string fallback) => values.Count == 0
        ? $"0.00 {fallback}"
        : string.Join(" · ", values.Select(p => $"{p.Value:N2} {p.Key}"));
}
