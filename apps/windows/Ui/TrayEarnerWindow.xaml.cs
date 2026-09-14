using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VolturaEarner.Platform;
using VolturaEarner.Features.Tracking;

namespace VolturaEarner.Ui;

public partial class TrayEarnerWindow : Window
{
    private bool _exit;
    private long _dismissedAt;
    internal bool IsPinned => LivePin.IsChecked == true;
    internal event Action? OpenMainRequested;
    internal Size PreferredSize()
    {
        WindowBorder.Measure(new(440, double.PositiveInfinity));

        return new(440, Math.Ceiling(WindowBorder.DesiredSize.Height));
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
        LiveView.StartPause.Focus();
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
