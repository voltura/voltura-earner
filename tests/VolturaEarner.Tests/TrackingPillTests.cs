using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class TrackingPillTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("main")]
    [InlineData("compact")]
    [InlineData("minimal")]
    public async Task PillStartsAndPausesSharedTrackingWithMatchingTooltip(string view)
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        AppRuntime runtime = null!;
        Button pill = null!;
        Task operation = Task.CompletedTask;

        fixture.Run(() =>
        {
            runtime = new(new(root, false, true));
            runtime.Window.Opacity = runtime.LiveWindow.Opacity = 0;
            runtime.Window.ShowActivated = runtime.LiveWindow.ShowActivated = false;
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() =>
            {
                if (view == "main")
                {
                    runtime.Open();
                    pill = runtime.Window.TrackingToggle;
                }
                else
                {
                    runtime.LiveWindow.SetMinimalView(view == "minimal");
                    runtime.LiveWindow.LivePin.IsChecked = true;
                    runtime.LiveWindow.Open(new(0, 0, 1, 1));
                    runtime.LiveWindow.UpdateLayout();
                    pill = runtime.LiveWindow.TrackingToggle;
                    Assert.False(runtime.LiveWindow.CanDragFrom(pill.TranslatePoint(new(5, 5), runtime.LiveWindow)));
                }

                Assert.Equal(Strings.Current["PressToStart"], pill.ToolTip);
            });
            await ClickAndWait(true);
            await ClickAndWait(false);
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

        async Task ClickAndWait(bool running)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var property = DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty, typeof(Button));
            EventHandler changed = (_, _) => completed.TrySetResult();

            fixture.Run(() =>
            {
                property.AddValueChanged(pill, changed);
                pill.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            });

            try
            {
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                fixture.Run(() =>
                {
                    Assert.Equal(running, runtime.Session.Running);
                    Assert.Equal(Strings.Current[running
                        ? "PressToPause"
                        : "PressToStart"], pill.ToolTip);
                    Assert.Equal(running, runtime.DisplayTimerEnabled);
                });
            }
            finally
            {
                fixture.Run(() => property.RemoveValueChanged(pill, changed));
            }
        }
    }
}
