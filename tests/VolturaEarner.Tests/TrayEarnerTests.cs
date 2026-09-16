using System.Windows;
using System.Windows.Controls.Primitives;
using VolturaEarner.Features.Settings;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Platform;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class TrayEarnerTests(WpfTestFixture fixture)
{
    [Fact]
    public void MinimalViewResizesAndExpandsWithoutOpeningMainOrLosingPinAndTask()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            var mainRequested = false;
            var settings = new AppSettings();

            popup.OpenMainRequested += () => mainRequested = true;

            try
            {
                popup.LiveView.Populate(settings);
                popup.Open(new(0, 0, 1, 1));
                popup.LivePin.IsChecked = true;
                popup.UpdateLayout();

                var compactSize = popup.PreferredSize();
                var selectedTask = popup.LiveView.TaskChoice.SelectedItem;

                Assert.False(popup.CanDragFrom(popup.ShowMinimal.TranslatePoint(new(10, 10), popup)));
                popup.ShowMinimal.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                popup.UpdateLayout();
                Assert.True(popup.Height < compactSize.Height / 2);
                Assert.True(popup.Width < compactSize.Width);
                Assert.True(popup.LiveView.NetEarnings.IsVisible);
                Assert.True(popup.LiveView.DayProgress.IsVisible);
                Assert.False(popup.LiveView.TaskCard.IsVisible);
                Assert.False(popup.LiveView.NetEarningsHeading.IsVisible);
                Assert.False(popup.LiveView.GrossEarnings.IsVisible);
                Assert.False(popup.LiveView.TimeDetails.IsVisible);
                Assert.False(popup.LiveView.TargetText.IsVisible);
                Assert.False(popup.ShowFull.IsVisible);
                Assert.False(popup.ShowMinimal.IsVisible);
                Assert.True(popup.ShowCompact.IsVisible);
                Assert.False(popup.CanDragFrom(popup.ShowCompact.TranslatePoint(new(10, 10), popup)));
                popup.DismissOnDeactivate();
                Assert.True(popup.IsVisible);

                popup.ShowCompact.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                popup.UpdateLayout();
                Assert.Equal(compactSize, popup.PreferredSize());
                Assert.True(popup.LiveView.TaskCard.IsVisible);
                Assert.True(popup.LiveView.TimeDetails.IsVisible);
                Assert.True(popup.ShowFull.IsVisible);
                Assert.True(popup.ShowMinimal.IsVisible);
                Assert.False(popup.ShowCompact.IsVisible);
                Assert.True(popup.IsPinned);
                Assert.Same(selectedTask, popup.LiveView.TaskChoice.SelectedItem);
                Assert.False(mainRequested);
                popup.ShowFull.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(mainRequested);
            }
            finally
            {
                popup.Exit();
            }
        });
    }

    [Fact]
    public void SmallMinimalPreferenceHalvesTheWindowAndRestoresStandardSizing()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            var settings = new AppSettings();
            var session = new TrackingSession(settings, WorkData.Empty);

            try
            {
                popup.ApplySettings(settings);
                popup.Render(session);
                popup.SetMinimalView(true);
                popup.Open(new(0, 0, 1, 1));
                popup.UpdateLayout();

                var standardWidth = popup.Width;
                var standardHeight = popup.Height;

                popup.ApplySettings(settings with { MinimalViewSize = "small" });
                popup.UpdateLayout();
                Assert.InRange(popup.Width / standardWidth, 0.45, 0.55);
                Assert.InRange(popup.Height / standardHeight, 0.45, 0.55);
                Assert.True(popup.ShowCompact.IsVisible);

                session.Start(settings.SelectedTask);
                popup.Render(session);
                popup.UpdateLayout();
                Assert.InRange(Math.Abs(popup.Width / standardWidth - 0.5), 0, 0.05);

                popup.SetMinimalView(false);
                Assert.Equal(440, popup.Width);
                popup.SetMinimalView(true);
                Assert.InRange(popup.Width / standardWidth, 0.45, 0.55);

                popup.ApplySettings(settings);
                popup.UpdateLayout();
                Assert.Equal(standardWidth, popup.Width);
                Assert.Equal(standardHeight, popup.Height);
            }
            finally
            {
                popup.Exit();
            }
        });
    }

    [Fact]
    public void MinimalWidthFitsTrackingHeaderAndGrowsWithTheAmount()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            var clock = new TestClock();
            var session = new TrackingSession(new() { HourlyRate = 1000000000 }, WorkData.Empty, clock);

            try
            {
                popup.Render(session);
                popup.SetMinimalView(true);
                popup.LivePin.IsChecked = true;
                popup.Open(new(0, 0, 1, 1));
                popup.UpdateLayout();

                var smallWidth = popup.Width;

                Assert.True(smallWidth < 440);
                Assert.True(popup.TrackingToggle.IsVisible);
                Assert.True(popup.ShowCompact.IsVisible);

                var pausedPillWidth = popup.TrackingToggle.ActualWidth;
                var pausedSpace = popup.TrackingToggle.Margin.Right;

                session.Start(session.Settings.SelectedTask);
                popup.Render(session);
                popup.UpdateLayout();
                Assert.InRange(Math.Abs(popup.Width - smallWidth), 0, 0.5);
                Assert.True(popup.TrackingToggle.ActualWidth > pausedPillWidth);
                Assert.True(popup.TrackingToggle.Margin.Right < pausedSpace);

                session.Pause();
                popup.Render(session);
                popup.UpdateLayout();
                Assert.InRange(Math.Abs(popup.Width - smallWidth), 0, 0.5);
                session.Start(session.Settings.SelectedTask);

                foreach (FrameworkElement control in popup.LiveHeader.Children)
                {
                    if (control.IsVisible)
                    {
                        var available = control.ActualWidth;

                        control.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(control.DesiredSize.Width - control.Margin.Left - control.Margin.Right <= available + 1);
                    }
                }

                clock.Advance(TimeSpan.FromHours(1));
                session.Settle();
                popup.Render(session);
                popup.UpdateLayout();
                Assert.True(popup.Width > smallWidth);

                var amountWidth = popup.LiveView.NetEarnings.ActualWidth;

                popup.LiveView.NetEarnings.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(popup.LiveView.NetEarnings.DesiredSize.Width <= amountWidth + 1);
                popup.SetMinimalView(false);
                Assert.Equal(440, popup.Width);
            }
            finally
            {
                popup.Exit();
            }
        });
    }

    [Fact]
    public void MinimalHeaderWidthFollowsSelectedLanguageWithoutChangingBetweenStates()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            var session = new TrackingSession(new() { HourlyRate = 0 }, WorkData.Empty);

            try
            {
                popup.SetMinimalView(true);
                popup.LivePin.IsChecked = true;
                popup.Open(new(0, 0, 1, 1));

                var englishWidth = CheckLanguage("en");
                var germanWidth = CheckLanguage("de");

                Assert.True(germanWidth > englishWidth);
                Assert.InRange(Math.Abs(CheckLanguage("en") - englishWidth), 0, 0.5);
            }
            finally
            {
                popup.Exit();
                Strings.Current.SetLanguage("en");
            }

            double CheckLanguage(string language)
            {
                Strings.Current.SetLanguage(language);
                popup.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                session.Pause();
                popup.Render(session);
                popup.UpdateLayout();

                var pausedWidth = popup.Width;
                var pausedPreferred = popup.PreferredSize().Width;
                var pausedPill = popup.TrackingToggle.ActualWidth;
                var pausedSpace = popup.TrackingToggle.Margin.Right;

                Assert.Equal(Strings.Current["Paused"], popup.TrackingState.Text);
                session.Start(session.Settings.SelectedTask);
                popup.Render(session);
                popup.UpdateLayout();
                Assert.Equal("● " + Strings.Current["Tracking"], popup.TrackingState.Text);
                Assert.True(Math.Abs(popup.Width - pausedWidth) <= 0.5,
                    $"{language}: paused {pausedWidth}/{pausedPreferred}, tracking {popup.Width}/{popup.PreferredSize().Width}, pill {pausedPill}/{popup.TrackingToggle.ActualWidth}, space {pausedSpace}/{popup.TrackingToggle.Margin.Right}");

                return pausedWidth;
            }
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(50, 9)]
    public void MinimalTooltipsFollowLiveTotalsAndOmitAbsentCostAndOvertime(int dailyCost, int hours)
    {
        fixture.Run(() =>
        {
            var clock = new TestClock();
            var settings = new AppSettings { DailyCost = dailyCost, HourlyRate = 100 };
            var session = new TrackingSession(settings, WorkData.Empty, clock);
            var view = new LiveEarningsView();

            view.Populate(settings);
            view.SetMinimalView(true);
            session.Start(settings.SelectedTask);
            clock.Advance(TimeSpan.FromHours(hours));
            session.Settle();
            view.Render(session);

            var earnings = Assert.IsType<string>(view.NetEarnings.ToolTip);
            var time = Assert.IsType<string>(view.DayProgress.ToolTip);

            Assert.Contains($"{hours * 100:N2} USD", earnings);
            Assert.Equal(dailyCost > 0, earnings.Contains(Strings.Current["DailyCost"], StringComparison.Ordinal));
            Assert.Equal(hours > 8, time.Contains(Strings.Current["Overtime"], StringComparison.Ordinal));
            Assert.Contains($"{hours:00}:00:00", time);

            if (dailyCost > 0)
            {
                Assert.Contains($"{dailyCost:N2} USD", earnings);
                Assert.Contains("01:00:00", time);
            }

            clock.Advance(TimeSpan.FromMinutes(1));
            session.Settle();
            view.Render(session);
            Assert.Contains($"{hours:00}:01:00", Assert.IsType<string>(view.DayProgress.ToolTip));
            Assert.NotEqual(earnings, view.NetEarnings.ToolTip);
            view.SetMinimalView(false);
            Assert.Null(view.NetEarnings.ToolTip);
            Assert.Null(view.DayProgress.ToolTip);
        });
    }

    [Fact]
    public void PinKeepsPopupOpenAndHeaderDragExcludesControls()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };

            try
            {
                popup.Show();
                popup.UpdateLayout();
                Assert.True(popup.CanDragFrom(new(30, 30)));
                Assert.False(popup.CanDragFrom(popup.LivePin.TranslatePoint(new(10, 10), popup)));
                Assert.False(popup.CanDragFrom(new(30, 200)));
                popup.LivePin.IsChecked = true;
                popup.DismissOnDeactivate();
                Assert.True(popup.IsVisible);
                Assert.True(popup.Topmost);
                popup.LivePin.IsChecked = false;
                popup.DismissOnDeactivate();
                Assert.False(popup.IsVisible);
                popup.Show();
                popup.Close();
                Assert.False(popup.IsVisible);
                popup.Show(); // Closing hides the reusable popup.
                Assert.True(popup.IsVisible);
            }
            finally
            {
                popup.Exit();
            }
        });
    }

    [Fact]
    public void OpeningPopupDoesNotFocusSplitStartControls()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };

            try
            {
                popup.Open(new(0, 0, 1, 1));

                Assert.False(popup.LiveView.StartPause.IsKeyboardFocused);
                Assert.False(popup.LiveView.StartOptions.IsKeyboardFocused);
            }
            finally
            {
                popup.Exit();
            }
        });
    }

    [Theory]
    [InlineData(1800, 1050)]
    [InlineData(0, 0)]
    [InlineData(-1900, 200)]
    public void PopupStaysWithinWorkArea(int x, int y)
    {
        var work = new System.Drawing.Rectangle(-1920, 0, 1920, 1040);
        var bounds = TrayEarnerPlacement.CalculateBounds(work, new(x, y, 20, 20), 660, 840, 12);

        Assert.True(work.Contains(bounds));
        Assert.Equal(660, bounds.Width);
    }

    [Fact]
    public async Task LivePopupControlsShareSessionAndOnlyVisibleTrackingUsesDisplayTimer()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(root, false, true));
            runtime.Window.Opacity = 0;
            runtime.Window.ShowActivated = false;
            runtime.LiveWindow.Opacity = 0;
            runtime.LiveWindow.ShowActivated = false;
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() =>
            {
                runtime.LiveWindow.Show();
                Assert.False(runtime.DisplayTimerEnabled);
                operation = runtime.ExecuteAsync("toggle");
            });
            await operation;
            fixture.Run(() =>
            {
                Assert.True(runtime.DisplayTimerEnabled);
                Assert.Equal("Pause", runtime.LiveWindow.LiveView.StartPause.Content);
                runtime.LiveWindow.ShowMinimal.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(runtime.DisplayTimerEnabled);
                Assert.True(runtime.Session.Running);
                Assert.Equal(Visibility.Visible, runtime.Window.LiveView.TaskCard.Visibility);
                runtime.LiveWindow.LivePin.IsChecked = true;
                runtime.Window.Topmost = true;
                runtime.LiveWindow.ShowCompact.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                runtime.LiveWindow.ShowFull.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(runtime.LiveWindow.IsVisible);
                Assert.True(runtime.Window.IsVisible);
                Assert.True(runtime.DisplayTimerEnabled);
                Assert.True(runtime.Session.Running);
                Assert.True(runtime.Window.HeaderPinToggle.IsChecked);
                runtime.Window.ShowCompact.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(runtime.Window.IsVisible);
                Assert.True(runtime.LiveWindow.IsVisible);
                Assert.True(runtime.LiveWindow.LiveView.TaskCard.IsVisible);
                Assert.True(runtime.LiveWindow.IsPinned);
                Assert.True(runtime.Window.Topmost);
                Assert.True(runtime.DisplayTimerEnabled);
                runtime.LiveWindow.ShowMinimal.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                runtime.Open();
                runtime.Window.ShowCompact.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.False(runtime.Window.IsVisible);
                Assert.True(runtime.LiveWindow.LiveView.TaskCard.IsVisible);
                Assert.False(runtime.LiveWindow.ShowCompact.IsVisible);
                runtime.LiveWindow.Hide();
                Assert.False(runtime.DisplayTimerEnabled);
                Assert.True(runtime.CheckpointTimerEnabled);
                operation = runtime.ExecuteAsync("toggle");
            });
            await operation;
            fixture.Run(() => Assert.False(runtime.CheckpointTimerEnabled));
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
}
