using VolturaEarner.Ui;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Reports;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Features.Updates;
using VolturaEarner.Platform;

namespace VolturaEarner;

public partial class MainWindow : Window
{
    internal event Action<string>? ActionRequested;
    private bool _populating;
    public MainWindow()
    {
        InitializeComponent();
        TooltipLifetime.Attach(this);
        CurrencyInput.ItemsSource = CurrencyChoice.All;
        LiveView.ActionRequested += action => ActionRequested?.Invoke(action);
        VersionText.Text = "Voltura Earner " + typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
        WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(this);
        WindowWorkAreaPlacement.KeepVisibleAfterDisplayChanges(this);
    }
    private void ActionClick(object sender, RoutedEventArgs e) => ActionRequested?.Invoke((string)((Button)sender).Tag);
    private void AlwaysOnTopClick(object sender, RoutedEventArgs e) => ActionRequested?.Invoke("topmost");
    private void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Pages && Pages.SelectedIndex == 1)
        {
            ActionRequested?.Invoke("refresh-history");
        }
    }
    private void PeriodChanged(object sender, SelectionChangedEventArgs e) => ActionRequested?.Invoke("refresh-history");
    private void AutomaticUpdatesChanged(object sender, RoutedEventArgs e)
    {
        if (!_populating)
        {
            ActionRequested?.Invoke("automatic-updates");
        }
    }
    internal ComboBox TaskChoice => LiveView.TaskChoice;
    internal ReportPeriod Period => (ReportPeriod)Math.Max(0, PeriodChoice.SelectedIndex);
    internal void Populate(AppSettings settings)
    {
        _populating = true;

        try
        {
            LanguageChoice.ItemsSource = new[] { new LanguageOption("system", Strings.Current["FollowWindows"]) }
                .Concat(LanguageCatalog.All.Select(language => new LanguageOption(language.Id, language.NativeName)));
            LanguageChoice.SelectedValue = settings.Language;

            CurrencyInput.ItemsSource = CurrencyChoice.All.Select(currency => currency with { Name = Strings.Current[CurrencyChoice.NameKeys[currency.Code]] }).ToArray();
            RateInput.Text = settings.HourlyRate.ToString(CultureInfo.CurrentCulture);
            CostInput.Text = settings.DailyCost.ToString(CultureInfo.CurrentCulture);
            HoursInput.Text = settings.DailyHours.ToString(CultureInfo.CurrentCulture);
            OvertimeChoice.SelectedIndex = (int)settings.Overtime;
            OvertimeRateInput.Text = settings.CustomOvertimeRate.ToString(CultureInfo.CurrentCulture);
            CurrencyInput.SelectedItem = CurrencyInput.Items.Cast<CurrencyChoice>().FirstOrDefault(currency => currency.Code == settings.Currency);
            CurrencyInput.Text = settings.Currency;
            TopmostCheck.IsChecked = settings.AlwaysOnTop;
            TrayCheck.IsChecked = settings.MinimizeToTray;
            StartupCheck.IsChecked = settings.StartWithWindows;
            ProgressCheck.IsChecked = settings.ShowProgress;
            TooltipsCheck.IsChecked = settings.ShowTooltips;
            SoundsCheck.IsChecked = settings.PlaySounds;
            ConfirmCheck.IsChecked = settings.ConfirmExit;
            SaveLogCheck.IsChecked = settings.SaveWorkLog;
            AutoReportCheck.IsChecked = settings.AutoShowReport;
            LoggingCheck.IsChecked = settings.Logging;
            ErrorsCheck.IsChecked = settings.ShowErrors;
            ExportFolderInput.Text = settings.ExportDirectory;
            DataFolderInput.Text = settings.WorkDirectory;
            ThemeChoice.SelectedIndex = settings.Theme == "light"
                ? 1
                : settings.Theme == "dark"
                    ? 2
                    : 0;
            ApplyTodayCheck.IsChecked = false;
        }
        finally
        {
            _populating = false;
        }

        ApplySettings(settings);
    }
    internal void ApplySettings(AppSettings settings)
    {
        _populating = true;

        try
        {
            AutomaticUpdatesCheck.IsChecked = settings.AutomaticUpdates;
            LiveView.Populate(settings);
            System.Windows.Application.Current.Resources["TooltipsEnabled"] = settings.ShowTooltips;
            Topmost = settings.AlwaysOnTop;
        }
        finally
        {
            _populating = false;
        }
    }
    internal AppSettings ReadSettings(AppSettings previous)
    {
        if (!decimal.TryParse(RateInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rate) || !decimal.TryParse(CostInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var cost) || !decimal.TryParse(HoursInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var hours))
        {
            throw new InvalidDataException(Strings.Current["CheckTheRateDailyCostAndHours"]);
        }

        var settings = previous with
        {
            Language = (string)LanguageChoice.SelectedValue,
            HourlyRate = rate,
            DailyCost = cost,
            DailyHours = hours,
            Overtime = (OvertimePolicy)OvertimeChoice.SelectedIndex,
            CustomOvertimeRate = OvertimeChoice.SelectedIndex == 1
                ? ParseOvertimeRate()
                : previous.CustomOvertimeRate,
            Currency = CurrencyInput.Text.Trim(),
            AlwaysOnTop = TopmostCheck.IsChecked == true,
            MinimizeToTray = TrayCheck.IsChecked == true,
            StartWithWindows = StartupCheck.IsChecked == true,
            ShowProgress = ProgressCheck.IsChecked == true,
            ShowTooltips = TooltipsCheck.IsChecked == true,
            PlaySounds = SoundsCheck.IsChecked == true,
            ConfirmExit = ConfirmCheck.IsChecked == true,
            SaveWorkLog = SaveLogCheck.IsChecked == true,
            AutoShowReport = AutoReportCheck.IsChecked == true,
            Logging = LoggingCheck.IsChecked == true,
            ShowErrors = ErrorsCheck.IsChecked == true,
            ExportDirectory = ExportFolderInput.Text.Trim(),
            WorkDirectory = DataFolderInput.Text.Trim(),
            Theme = (string)((ComboBoxItem)ThemeChoice.SelectedItem).Tag,
            FirstRun = false,
        };

        settings.Validate();

        return settings;
    }
    private decimal ParseOvertimeRate()
    {
        if (!decimal.TryParse(OvertimeRateInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rate))
        {
            throw new InvalidDataException(Strings.Current["EnterAValidOvertimeHourlyRate"]);
        }

        return rate;
    }
    internal void Render(TrackingSession session)
    {
        LiveView.Render(session);
        TrackingState.Text = session.Running
            ? "● " + Strings.Current["Tracking"]
            : Strings.Current["Paused"];
        TrackingToggle.ToolTip = session.Running
            ? Strings.Current["PressToPause"]
            : Strings.Current["PressToStart"];
    }
    internal void UpdateState(UpdateState state, bool eligible)
    {
        UpdateStatusText.Text = state.Status switch
        {
            UpdateStatus.ManualUpdates => Strings.Current["DownloadUpdatesManuallyForThisCopy"],
            UpdateStatus.Checking => Strings.Current["CheckingForUpdatesMore"],
            UpdateStatus.Downloading => Strings.Current["DownloadingUpdateMore"],
            UpdateStatus.Ready => Strings.Current["UpdateReady"],
            UpdateStatus.Current => Strings.Current["UpToDate"],
            UpdateStatus.Installing => Strings.Current["StartingInstallationMore"],
            UpdateStatus.UpdateCheckFailed => Strings.Current["UpdateCheckFailedTryAgain"],
            UpdateStatus.UpdateDownloadFailed => Strings.Current["DownloadFailedTryAgain"],
            UpdateStatus.UpdateVerificationFailed => Strings.Current["UpdateVerificationFailedInstallationBlocked"],
            UpdateStatus.UpdateInstallFailed => Strings.Current["InstallationFailedTryAgain"],
            _ => Strings.Current["ReadyToCheck"],
        };

        CheckUpdateButton.Content = eligible
            ? Strings.Current["CheckForUpdates"]
            : Strings.Current["OpenDownloads"];
        CheckUpdateButton.IsEnabled = !state.Busy;
        InstallButton.Visibility = state.Ready
            ? Visibility.Visible
            : Visibility.Collapsed;
        AutomaticUpdatesCheck.IsEnabled = eligible;
    }
}

internal sealed record LanguageOption(string Id, string Name);
