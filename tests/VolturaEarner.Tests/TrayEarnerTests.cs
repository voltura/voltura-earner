using System.Windows;
using VolturaEarner.Platform;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class TrayEarnerTests(WpfTestFixture fixture)
{
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
