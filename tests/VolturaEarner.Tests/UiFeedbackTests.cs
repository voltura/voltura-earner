using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Platform;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class UiFeedbackTests(WpfTestFixture fixture)
{
    private static readonly string[] Themes = ["dark", "light"];
    [Fact]
    public void SplitStartMenuUsesCurrentThemeTranslationsAndOvertimePermission()
    {
        fixture.Run(() =>
        {
            var view = new LiveEarningsView();
            var actions = new List<string>();

            view.ActionRequested += actions.Add;

            try
            {
                view.Measure(new(400, double.PositiveInfinity));
                view.Arrange(new Rect(view.DesiredSize));
                view.UpdateLayout();

                var plus = (Grid)view.StartOptions.Content;
                var plusCenter = plus.TranslatePoint(new(8, 8), view.StartOptions);

                Assert.InRange(Math.Abs(plusCenter.Y - view.StartOptions.ActualHeight / 2), 0, 1);
                Assert.InRange(Math.Abs(plusCenter.X - view.StartOptions.ActualWidth / 2), 0, 1);

                foreach (var theme in Themes)
                {
                    ThemeManager.Apply(theme);

                    foreach (var language in LanguageCatalog.All)
                    {
                        Strings.Current.SetLanguage(language.Id);
                        view.Populate(new() { Overtime = OvertimePolicy.CustomRate });
                        view.PrepareStartMenu();
                        view.StartMenu.ApplyTemplate();

                        var surface = (Border)view.StartMenu.Template.FindName("MenuSurface", view.StartMenu);

                        surface.Measure(new(420, double.PositiveInfinity));
                        surface.Arrange(new Rect(surface.DesiredSize));
                        surface.UpdateLayout();
                        Assert.Equal(new CornerRadius(8), surface.CornerRadius);
                        Assert.Same(Application.Current.FindResource("SurfaceBrush"), surface.Background);
                        Assert.True(view.StartOvertimeItem.IsEnabled);

                        foreach (System.Windows.Shapes.Shape shape in ((Grid)view.StartOvertimeItem.Icon).Children)
                        {
                            Assert.Same(Application.Current.FindResource("TextBrush"), shape.Stroke);
                        }

                        foreach (MenuItem item in view.StartMenu.Items)
                        {
                            item.ApplyTemplate();

                            var header = (TextBlock)item.Template.FindName("HeaderText", item);
                            var icon = (Grid)item.Icon;
                            var iconPosition = icon.TranslatePoint(new(), item);

                            Assert.Equal(16, icon.ActualWidth);
                            Assert.Equal(16, icon.ActualHeight);
                            Assert.InRange(Math.Abs(iconPosition.X - 12), 0, 1);
                            Assert.InRange(Math.Abs(iconPosition.Y + 8 - item.ActualHeight / 2), 0, 1);

                            Assert.Equal(14, header.FontSize);
                            Assert.Same(Application.Current.FindResource("TextBrush"), header.Foreground);
                            Assert.Equal(Strings.Current[(string)item.Tag == "start-overtime"
                                ? "StartOvertime"
                                : "StartRegular"], header.Text);
                            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        }

                        view.Populate(new() { Overtime = OvertimePolicy.NotAllowed });
                        Assert.False(view.StartOvertimeItem.IsEnabled);
                        Assert.Equal(Strings.Current["NotAllowedStopAtLimit"], view.StartOvertimeItem.ToolTip);
                    }
                }

                Assert.Equal(84, actions.Count);
                Assert.All(actions.Where((_, index) => index % 2 == 0), action => Assert.Equal("start-overtime", action));
                Assert.All(actions.Where((_, index) => index % 2 == 1), action => Assert.Equal("start-regular", action));
            }
            finally
            {
                Strings.Current.SetLanguage("en");
                ThemeManager.Apply("system");
            }
        });
    }

    [Fact]
    public void ClosingTaskManagerKeepsAddedTasksWithoutChangingTheActiveTask()
    {
        fixture.Run(() =>
        {
            var original = new AppSettings { Tasks = ["Original"], SelectedTask = "Original" };
            var window = new TaskManagerWindow(original);

            Assert.True(window.AddTask("Added task"));
            Assert.False(window.AddTask("added TASK"));
            window.Close();
            Assert.Equal<string>(["Original", "Added task"], window.Result.Tasks);
            Assert.Equal("Original", window.Result.SelectedTask);
            Assert.Single(original.Tasks);

            var next = new TaskManagerWindow(window.Result);

            next.TasksList.SelectedItem = "Added task";
            next.UseSelected();
            Assert.Equal("Added task", next.Result.SelectedTask);
            next.RemoveSelected();
            Assert.Equal("Original", next.Result.SelectedTask);
            next.RemoveSelected();
            Assert.Single(next.Result.Tasks);
            next.Close();
        });
    }

    [Fact]
    public async Task MainWindowXKeepsTrackingAndCheckpointing()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(root, false, true));
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() => operation = runtime.ExecuteAsync("toggle"));
            await operation;
            fixture.Run(() =>
            {
                runtime.Window.Close();
                Assert.False(runtime.Window.IsVisible);
                Assert.True(runtime.Session.Running);
                Assert.True(runtime.CheckpointTimerEnabled);
                Assert.False(runtime.DisplayTimerEnabled);
                operation = runtime.ExecuteAsync("toggle");
            });
            await operation;
            Assert.False(runtime.Session.Running);
            Assert.True(File.Exists(Path.Combine(root, "work.json")));
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() => runtime.Window.Close());

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void CompactWindowRetainsContentHeightAfterWorkAreaPlacement()
    {
        fixture.Run(() =>
        {
            var window = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };

            try
            {
                var settings = new AppSettings { HourlyRate = 1000, Currency = "SEK" };

                window.LiveView.Populate(settings);
                window.Render(new TrackingSession(settings, WorkData.Empty));

                var work = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
                var anchor = new System.Drawing.Rectangle(work.Right - 10, work.Bottom, 1, 1);

                TrayEarnerPlacement.Place(window, anchor);
                window.Show();
                TrayEarnerPlacement.Place(window, anchor);
                WindowWorkAreaPlacement.EnsureVisibleOnCurrentMonitor(window);
                window.UpdateLayout();
                Assert.True(window.LiveScroll.ScrollableHeight < 1,
                    $"Height {window.ActualHeight}, desired {window.PreferredSize().Height}, scroll {window.LiveScroll.ScrollableHeight}");
                Assert.Equal(Visibility.Collapsed, window.LiveScroll.ComputedVerticalScrollBarVisibility);
            }
            finally
            {
                window.Exit();
            }
        });
    }

    [Fact]
    public void CompactViewFitsAllContentInEveryLanguage()
    {
        fixture.Run(() =>
        {
            var window = new TrayEarnerWindow();

            try
            {
                foreach (var language in LanguageCatalog.All)
                {
                    Strings.Current.SetLanguage(language.Id);

                    var settings = new AppSettings { Language = language.Id, HourlyRate = 650, Overtime = OvertimePolicy.CustomRate, CustomOvertimeRate = 1000, Currency = "USD" };

                    window.LiveView.Populate(settings);
                    window.Render(new TrackingSession(settings, WorkData.Empty));
                    Assert.Contains($"{650:N2} USD", window.LiveView.TargetText.Text, StringComparison.Ordinal);
                    Assert.Contains($"{1000:N2} USD", window.LiveView.TargetText.Text, StringComparison.Ordinal);
                    Assert.Contains("  |  ", window.LiveView.TargetText.Text, StringComparison.Ordinal);
                    Assert.DoesNotContain('\n', window.LiveView.TargetText.Text);

                    var size = window.PreferredSize();

                    window.WindowBorder.Arrange(new Rect(size));
                    window.WindowBorder.UpdateLayout();
                    Assert.True(window.LiveScroll.ScrollableHeight < 1, $"{language.Id}: scroll {window.LiveScroll.ScrollableHeight}");
                    Assert.InRange(size.Height, 300, 800);
                    Assert.Equal(440, size.Width);
                    window.LiveView.TaskChoice.ApplyTemplate();

                    var menu = (Border)window.LiveView.TaskChoice.Template.FindName("DropDownMenu", window.LiveView.TaskChoice);

                    Assert.Equal(new CornerRadius(8), menu.CornerRadius);
                    Assert.Same(Application.Current.FindResource("SurfaceBrush"), menu.Background);
                }
            }
            finally
            {
                window.Exit();
                Strings.Current.SetLanguage("en");
            }
        });
    }

    [Fact]
    public void DismissingTooltipsClosesTheActivePopup()
    {
        fixture.Run(() =>
        {
            var owner = new Window { Opacity = 0, ShowActivated = false, ShowInTaskbar = false, Width = 100, Height = 100 };

            TooltipLifetime.Attach(owner);

            var target = new Button { Content = "test", MinWidth = 0 };

            owner.Content = target;

            var tooltip = new ToolTip { Content = "test", Opacity = 0, IsHitTestVisible = false, PlacementTarget = target };

            try
            {
                owner.Show();
                tooltip.IsOpen = true;
                Assert.True(tooltip.IsOpen);
                tooltip.IsOpen = false;
                tooltip.IsOpen = true;
                Assert.True(tooltip.IsOpen);
                owner.Hide();
                Assert.False(tooltip.IsOpen);
                owner.Show();
                Assert.False(tooltip.IsOpen);
                tooltip.IsOpen = true;
                TooltipLifetime.Dismiss();
                Assert.False(tooltip.IsOpen);
            }
            finally
            {
                tooltip.IsOpen = false;
                owner.Close();
            }
        });
    }
}
