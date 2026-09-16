using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Platform;
using VolturaEarner.Ui;

namespace VolturaEarner;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Application lifetime is disposed by the awaited runtime shutdown, followed by Finish on the owning dispatcher.")]
public partial class App : System.Windows.Application
{
    private SingleInstance? _instance;
    private AppRuntime? _runtime;

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);

        if (!e.Cancel && _runtime is not null)
        {
            e.Cancel = !SessionEndingSave.Complete(Dispatcher, _runtime.PrepareForSessionEndAsync, TimeSpan.FromSeconds(4));
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Strings.Current.SetLanguage("system");
        ThemeManager.Apply("system");

        try
        {
            if (e.Args.Contains("--installer-health-check", StringComparer.Ordinal))
            {
                new AppSettings().Validate();
                WorkData.Empty.Validate();

                var window = new MainWindow();

                window.Close();
                Shutdown(0);

                return;
            }

            var paths = AppPaths.Resolve(e.Args);
            var review = AppPaths.ReadPathArgument(e.Args, "--render-review");

            if (review is not null && !paths.Isolated)
            {
                throw new InvalidOperationException("Render review requires --isolated-test-mode and its data folder.");
            }

            _instance = new(paths.Isolated || paths.Portable
                ? paths.Data
                : null);

            if (!_instance.IsFirst)
            {
                Finish();

                return;
            }

            _runtime = new(paths);
            _runtime.Closed += Finish;
            MainWindow = _runtime.Window;

            try
            {
                await _runtime.InitializeAsync();
            }
            finally
            {
                if (review is null)
                {
                    _instance.Listen(() => Dispatcher.BeginInvoke(_runtime.Open));
                }
            }

            if (review is null)
            {
                if (!e.Args.Contains("--autostart", StringComparer.Ordinal) || !_runtime.Session.Settings.MinimizeToTray)
                {
                    _runtime.Window.Show();
                }
            }

            if (review is not null)
            {
                await RenderReviewAsync(review);
                await _runtime.CloseAsync(false);
            }
        }
        catch (Exception error)
        {
            if (e.Args.Contains("--render-review", StringComparer.Ordinal))
            {
                Console.Error.WriteLine(error);

                if (_runtime is not null)
                {
                    await _runtime.DisposeAsync();
                }

                _instance?.Dispose();
                Shutdown(1);

                return;
            }

            if (_runtime is not null)
            {
                _runtime.ReportStartupError(error);
                _runtime.Window.Show();
            }
            else
            {
                System.Windows.MessageBox.Show(error.Message, "Voltura Earner", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
    private async Task RenderReviewAsync(string output)
    {
        Directory.CreateDirectory(output);

        var window = _runtime!.Window;

        foreach (var theme in new[] { "light", "dark" })
        {
            ThemeManager.Apply(theme);
            window.Render(_runtime.Session);

            for (var page = 0; page < 4; page++)
            {
                window.Pages.SelectedIndex = page;
                await RenderElementAsync(window.RootSurface, new(708, 764), output, $"{theme}-{page}");

                if (page == 0)
                {
                    await RenderDropDownAsync(window.TaskChoice, output, $"{theme}-tasks-menu");
                }

                if (page == 1)
                {
                    await RenderDropDownAsync(window.PeriodChoice, output, $"{theme}-period-menu");
                }

                if (page == 2)
                {
                    window.CurrencyInput.ApplyTemplate();

                    var menu = (FrameworkElement)window.CurrencyInput.Template.FindName("CurrencyMenu", window.CurrencyInput);

                    await RenderElementAsync(menu, new(window.CurrencyInput.ActualWidth, 300), output, $"{theme}-currency-menu");

                    var preferences = (System.Windows.Controls.ScrollViewer)((System.Windows.Controls.Grid)((System.Windows.Controls.TabItem)window.Pages.Items[2]).Content).Children[0];

                    preferences.ScrollToVerticalOffset(window.LanguageChoice.TranslatePoint(new(), preferences).Y - 40);
                    await RenderElementAsync(window.RootSurface, new(708, 764), output, $"{theme}-preferences-language");
                    await RenderDropDownAsync(window.LanguageChoice, output, $"{theme}-language-menu");
                    preferences.ScrollToBottom();
                    await RenderElementAsync(window.RootSurface, new(708, 764), output, $"{theme}-preferences-storage");
                    preferences.ScrollToTop();
                }

                await RenderElementAsync(window.RootSurface, new(528, 484), output, $"{theme}-{page}-compact");
            }

            var confirmation = new ConfirmationWindow(Strings.Current["CloseEarner"], Strings.Current["Close"], "\uE8BB");

            confirmation.Surface.CornerRadius = new(8);
            confirmation.Surface.Measure(new(420, double.PositiveInfinity));
            await RenderElementAsync(confirmation.Surface, new(420, confirmation.Surface.DesiredSize.Height), output, $"{theme}-confirmation");
            confirmation.Close();

            var tasks = new TaskManagerWindow(_runtime.Session.Settings);
            var tasksContent = (FrameworkElement)tasks.Content;

            tasksContent.Measure(new(440, double.PositiveInfinity));
            await RenderElementAsync(tasksContent, new(440, tasksContent.DesiredSize.Height), output, $"{theme}-task-manager");
            tasks.Close();

            var popup = _runtime.LiveWindow;

            window.LiveView.PrepareStartMenu();

            var startMenu = window.LiveView.StartMenu;

            startMenu.ApplyTemplate();

            var menuSurface = (FrameworkElement)startMenu.Template.FindName("MenuSurface", startMenu);

            menuSurface.Measure(new(340, double.PositiveInfinity));
            await RenderElementAsync(menuSurface, new(340, menuSurface.DesiredSize.Height), output, $"{theme}-start-menu");

            popup.Render(_runtime.Session);
            popup.WindowBorder.Background = (Brush)FindResource("WindowBrush");
            popup.WindowBorder.CornerRadius = new(8);
            await RenderElementAsync(popup.WindowBorder, popup.PreferredSize(), output, $"{theme}-tray");
            popup.SetMinimalView(true);
            await RenderElementAsync(popup.WindowBorder, popup.PreferredSize(), output, $"{theme}-tray-minimal");

            foreach (var (name, content) in new[] { ("earnings", popup.LiveView.NetEarnings.ToolTip), ("time", popup.LiveView.DayProgress.ToolTip) })
            {
                var tooltip = new System.Windows.Controls.ToolTip { Content = content };

                tooltip.Measure(new(340, double.PositiveInfinity));
                await RenderElementAsync(tooltip, tooltip.DesiredSize, output, $"{theme}-tray-minimal-{name}-tooltip");
            }

            popup.SetMinimalView(false);
        }
    }
    private static async Task RenderDropDownAsync(System.Windows.Controls.ComboBox choice, string output, string name)
    {
        choice.ApplyTemplate();

        var menu = (FrameworkElement)choice.Template.FindName("DropDownMenu", choice);

        await RenderElementAsync(menu, new(Math.Max(180, choice.ActualWidth), 300), output, name);
    }
    private static async Task RenderElementAsync(FrameworkElement element, System.Windows.Size size, string output, string name)
    {
        element.Measure(size);
        element.Arrange(new Rect(size));
        element.UpdateLayout();
        await element.Dispatcher.InvokeAsync(() => element.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ContextIdle);

        foreach (var scale in new[] { 1d, 1.5d, 2d })
        {
            var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);

            bitmap.Render(element);

            var encoder = new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            var suffix = scale == 1
                ? ""
                : $"-{scale * 100:0}pct";
            using var stream = File.Create(Path.Combine(output, name + suffix + ".png"));

            encoder.Save(stream);
        }
    }
    internal void Finish()
    {
        _instance?.Dispose();
        _instance = null;
        Shutdown();
    }
}
