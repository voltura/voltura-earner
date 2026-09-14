using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace VolturaEarner.Ui;

internal static class ThemeManager
{
    private static bool? _highContrast;

    internal static bool IsTaskbarDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        );

        return key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
    }

    internal static void Apply(string choice)
    {
        var app = System.Windows.Application.Current;
        var highContrast = SystemParameters.HighContrast;
        // Fluent theme selection is annotated experimental in .NET 10; isolate its use here.
#pragma warning disable WPF0001
        var mode = choice switch
        {
            "light" => ThemeMode.Light,

            "dark" => ThemeMode.Dark,

            _ => ThemeMode.System,
        };
        // WPF handles system colors; reload on mode/contrast transitions, not unrelated events.

        if (app.ThemeMode != mode || (_highContrast is { } previous && previous != highContrast))
        {
            app.ThemeMode = mode;
        }

#pragma warning restore WPF0001

        _highContrast = highContrast;

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        );
        var dark =
            choice == "dark"
            || (
                choice == "system" && key?.GetValue("AppsUseLightTheme") is int value && value == 0
            );

        ApplyPalette(dark, highContrast);
    }

    internal static void ApplyPalette(bool dark, bool highContrast)
    {
        // Resolve actual colors on every event, including palette changes while high contrast stays on.
        SetBrush(
            "WindowBrush",
            highContrast
                ? SystemColors.WindowColor
                : Rgb(dark
                    ? 0x111720u
                    : 0xF4F6FAu)
        );
        SetBrush(
            "SurfaceBrush",
            highContrast
                ? SystemColors.WindowColor
                : Rgb(dark
                    ? 0x1B2431u
                    : 0xFFFFFFu)
        );
        SetBrush(
            "TextBrush",
            highContrast
                ? SystemColors.WindowTextColor
                : Rgb(dark
                    ? 0xF1F5FBu
                    : 0x17253Bu)
        );
        SetBrush(
            "MutedBrush",
            highContrast
                ? SystemColors.WindowTextColor
                : Rgb(dark
                    ? 0xB4C2D3u
                    : 0x52627Au)
        );
        SetBrush(
            "AccentBrush",
            highContrast
                ? SystemColors.HighlightColor
                : Rgb(dark
                    ? 0x87B5FFu
                    : 0x245CB4u)
        );
        SetBrush(
            "AccentTextBrush",
            highContrast
                ? SystemColors.HighlightTextColor
                : Rgb(dark
                    ? 0x111720u
                    : 0xFFFFFFu)
        );
        SetBrush(
            "BorderBrush",
            highContrast
                ? SystemColors.WindowTextColor
                : Rgb(dark
                    ? 0x354155u
                    : 0xD9E0EBu)
        );
        SetBrush(
            "ErrorAccentBrush",
            highContrast
                ? SystemColors.HighlightColor
                : Rgb(dark
                    ? 0xA94850u
                    : 0xB84950u)
        );
    }

    private static Color Rgb(uint value) =>
        Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static void SetBrush(string key, Color color)
    {
        var resources = System.Windows.Application.Current.Resources;

        if (resources[key] is SolidColorBrush current && current.Color == color)
        {
            return;
        }

        var brush = new SolidColorBrush(color);

        brush.Freeze();
        resources[key] = brush;
    }
}
