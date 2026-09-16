using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Platform;

namespace VolturaEarner.Ui;

// Adapted from WeekNumber's CalendarMessageWindow and shared theme resources.
internal sealed class ConfirmationWindow : Window
{
    internal Button CancelButton { get; } = new()
    {
        Content = Strings.Current["Cancel"],
        Width = double.NaN,
        MinWidth = 90,
        IsDefault = true,
        IsCancel = true,
    };
    internal Button ConfirmButton { get; } = new()
    {
        Width = double.NaN,
        MinWidth = 90,
        Margin = new(8, 0, 0, 0),
    };
    internal Border Surface { get; }

    internal ConfirmationWindow(string message, string action, string glyph)
    {
        Title = "Voltura Earner";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new("Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "WindowBrush");

        var content = new StackPanel();

        content.Children.Add(new TextBlock { Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 16) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        ButtonIcon.SetGlyph(CancelButton, "\uE711");
        CancelButton.Click += (_, _) => Close();
        ConfirmButton.Content = action;
        ButtonIcon.SetGlyph(ConfirmButton, glyph);
        ConfirmButton.Click += (_, _) => DialogResult = true;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 20, 0, 0),
        };

        actions.Children.Add(CancelButton);
        actions.Children.Add(ConfirmButton);
        content.Children.Add(actions);
        Surface = new() { Child = content, Padding = new(20), CornerRadius = new(10), BorderThickness = new(1) };

        Surface.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Surface.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        Content = Surface;
        WindowCorners.Apply(this, Surface);
        WindowWorkAreaPlacement.ConstrainAndCenterOnFirstLoad(this);
        Loaded += (_, _) => CancelButton.Focus();
    }

    internal static bool Confirm(Window owner, string message, string action, string glyph)
    {
        // The tray views are topmost independently of the main window's pin.
        var dialog = new ConfirmationWindow(message, action, glyph) { Owner = owner, Topmost = true };

        return dialog.ShowDialog() == true;
    }

    internal static bool ConfirmExit(Window main, Window live, string message, string action, string glyph)
    {
        var mainWasVisible = main.IsVisible;
        var liveWasVisible = live.IsVisible;
        var active = main.IsActive
            ? main
            : live.IsActive
                ? live
                : null;
        var confirmed = false;

        try
        {
            main.Hide();
            live.Hide();

            // The main window may never have been shown, so this dialog has no owner.

            var dialog = new ConfirmationWindow(message, action, glyph)
            {
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };

            confirmed = dialog.ShowDialog() == true;

            return confirmed;
        }
        finally
        {
            if (!confirmed)
            {
                if (mainWasVisible)
                {
                    main.Show();
                }

                if (liveWasVisible)
                {
                    live.Show();
                }

                active?.Activate();
            }
        }
    }
}
