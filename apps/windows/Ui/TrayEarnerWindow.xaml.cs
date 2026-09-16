using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VolturaEarner.Platform;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Ui;

public partial class TrayEarnerWindow : Window
{
    private bool _exit;
    private bool _minimal;
    private bool _smallMinimal;
    private long _dismissedAt;
    internal bool IsPinned => LivePin.IsChecked == true;
    internal event Action? OpenMainRequested;
    internal Size PreferredSize()
    {
        WindowBorder.Measure(new(_minimal
            ? double.PositiveInfinity
            : 440, double.PositiveInfinity));

        var width = _minimal
            ? Math.Ceiling(WindowBorder.DesiredSize.Width)
            : 440;

        WindowBorder.Measure(new(width, double.PositiveInfinity));

        return new(width, Math.Ceiling(WindowBorder.DesiredSize.Height));
    }
    public TrayEarnerWindow()
    {
        InitializeComponent();
        TooltipLifetime.Attach(this);
        WindowCorners.Apply(this, WindowBorder);
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!IsActive)
            {
                DismissOnDeactivate();
            }
        });
        WindowWorkAreaPlacement.KeepVisibleAfterDisplayChanges(this);
    }
    internal void Toggle(System.Drawing.Rectangle anchor)
    {
        if (IsVisible)
        {
            Hide();

            return;
        }
        // A tray click deactivates the popup before NotifyIcon delivers its click.

        if (_dismissedAt != 0 && Environment.TickCount64 - _dismissedAt < 250)
        {
            return;
        }

        Open(anchor);
    }
    internal void Open(System.Drawing.Rectangle anchor)
    {
        TrayEarnerPlacement.Place(this, anchor);
        Show();
        TrayEarnerPlacement.Place(this, anchor);
        Activate();
        Keyboard.ClearFocus();
    }
    internal void DismissOnDeactivate()
    {
        if (IsVisible && !IsPinned && !LiveView.TaskChoice.IsDropDownOpen && !LiveView.IsStartMenuOpen)
        {
            _dismissedAt = Environment.TickCount64;
            Hide();
        }
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

        if (_minimal && IsVisible)
        {
            ResizeToContent();
        }
    }
    internal bool CanDragFrom(Point position)
    {
        var bottom = LiveHeader.TranslatePoint(new Point(0, LiveHeader.ActualHeight), this).Y;

        if (position.X < 0 || position.X >= ActualWidth || position.Y < 0 || position.Y > bottom)
        {
            return false;
        }

        for (var element = InputHitTest(position) as DependencyObject; element is not null && element != this; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return false;
            }
        }

        return true;
    }
    private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed && CanDragFrom(args.GetPosition(this)))
        {
            args.Handled = true;
            DragMove();
        }
    }
    private void OpenMainClick(object sender, RoutedEventArgs e) => OpenMainRequested?.Invoke();
    private void TrackingClick(object sender, RoutedEventArgs e) => LiveView.ToggleTracking();
    private void ShowMinimalClick(object sender, RoutedEventArgs e) => SetMinimalView(true);
    private void ShowCompactClick(object sender, RoutedEventArgs e) => SetMinimalView(false);
    internal void ApplySettings(AppSettings settings)
    {
        LiveView.Populate(settings);
        _smallMinimal = settings.MinimalViewSize == "small";

        if (_minimal)
        {
            ResizeToContent();
        }
    }
    internal void SetMinimalView(bool minimal)
    {
        TooltipLifetime.Dismiss();
        _minimal = minimal;
        LiveView.SetMinimalView(minimal);
        ShowMinimal.Visibility = ShowFull.Visibility = minimal
            ? Visibility.Collapsed
            : Visibility.Visible;
        ShowCompact.Visibility = minimal
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResizeToContent();
    }
    private void ResizeToContent()
    {
        WindowBorder.LayoutTransform = new ScaleTransform(_minimal && _smallMinimal
            ? 0.5
            : 1, _minimal && _smallMinimal
                ? 0.5
                : 1);

        var statusSpace = 0d;

        if (_minimal)
        {
            var currentStatus = TrackingState.Text;
            var pausedStatus = Strings.Current["Paused"];
            var trackingStatus = "● " + Strings.Current["Tracking"];
            var pausedWidth = MeasureStatus(pausedStatus);
            var trackingWidth = MeasureStatus(trackingStatus);
            var currentWidth = currentStatus == pausedStatus
                ? pausedWidth
                : currentStatus == trackingStatus
                    ? trackingWidth
                    : MeasureStatus(currentStatus);

            TrackingState.Text = currentStatus;
            statusSpace = Math.Max(0, Math.Max(pausedWidth, trackingWidth) - currentWidth);
        }

        TrackingToggle.Margin = new(6, 0, statusSpace, 0);
        WindowBorder.UpdateLayout();

        var size = PreferredSize();

        WindowWorkAreaPlacement.SetPreferredSize(this, size);

        if (IsVisible)
        {
            Left += Width - size.Width;
            Top += Height - size.Height;
        }

        Width = size.Width;
        Height = size.Height;
        WindowWorkAreaPlacement.EnsureVisibleOnCurrentMonitor(this);
    }
    private double MeasureStatus(string value)
    {
        TrackingState.Text = value;
        TrackingState.Measure(new(double.PositiveInfinity, double.PositiveInfinity));

        return TrackingState.DesiredSize.Width;
    }
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            Hide();
            args.Handled = true;
        }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exit)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
    internal void Exit()
    {
        _exit = true;
        Close();
    }
}
