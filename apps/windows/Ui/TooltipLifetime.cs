using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VolturaEarner.Ui;

internal static class TooltipLifetime
{
    private static WeakReference<ToolTip>? _open;

    static TooltipLifetime()
    {
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (_open?.TryGetTarget(out var previous) == true && previous != sender)
            {
                previous.IsOpen = false;
            }

            _open = new((ToolTip)sender);
        }));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.ClosedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (_open?.TryGetTarget(out var previous) == true && previous == sender)
            {
                _open = null;
            }
        }));
    }

    internal static void Attach(Window window)
    {
        window.Deactivated += (_, _) => Dismiss();
        window.IsVisibleChanged += (_, _) => Dismiss();
        window.PreviewMouseDown += (_, _) => Dismiss();
        window.PreviewKeyDown += (_, _) => Dismiss();
    }

    internal static void Dismiss()
    {
        if (_open?.TryGetTarget(out var tooltip) == true)
        {
            tooltip.IsOpen = false;
        }

        _open = null;
    }
}
