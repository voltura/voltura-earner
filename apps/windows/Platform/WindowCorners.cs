using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace VolturaEarner.Platform;

internal static partial class WindowCorners
{
    internal static void Apply(Window window, Border border)
    {
        border.CornerRadius = new CornerRadius(0);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        window.SourceInitialized += OnSourceInitialized;

        void OnSourceInitialized(object? sender, EventArgs args)
        {
            window.SourceInitialized -= OnSourceInitialized;

            const int windowCornerPreference = 33;
            var round = 2; // DWMWCP_ROUND: the standard Windows 11 corner radius is 8 DIPs.
            var result = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle,
                windowCornerPreference, in round, sizeof(int));

            if (result >= 0)
            {
                border.CornerRadius = new CornerRadius(8);
            }
        }
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, in int value, int size);
}
