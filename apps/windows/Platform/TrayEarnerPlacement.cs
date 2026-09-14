using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace VolturaEarner.Platform;

internal static partial class TrayEarnerPlacement
{
    internal static Drawing.Rectangle CalculateBounds(Drawing.Rectangle work, Drawing.Rectangle anchor,
        int width, int height, int gap)
    {
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);

        var x = Math.Clamp(anchor.Right - width, work.Left, work.Right - width);
        var y = anchor.Top - height - gap;

        if (y < work.Top)
        {
            y = anchor.Bottom + gap;
        }

        return new(x, Math.Clamp(y, work.Top, work.Bottom - height), width, height);
    }

    internal static void Place(VolturaEarner.Ui.TrayEarnerWindow window, Drawing.Rectangle anchor)
    {
        var screen = Forms.Screen.FromRectangle(anchor);
        var point = new NativePoint { X = anchor.Left, Y = anchor.Top };
        var monitor = MonitorFromPoint(point, 2);
        var scale = GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0
            ? dpi / 96d
            : 1d;
        var size = window.PreferredSize();

        WindowWorkAreaPlacement.SetPreferredSize(window, size);

        var bounds = CalculateBounds(screen.WorkingArea, anchor,
            (int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), (int)Math.Round(8 * scale));
        var handle = new WindowInteropHelper(window).EnsureHandle();

        _ = SetWindowPos(handle, new nint(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);
    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int kind, out uint x, out uint y);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
