using System.Windows;
using System.Windows.Threading;
using VolturaEarner.Features.Localization;
using VolturaEarner.Features.Tracking;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class ViewReviewTests(WpfTestFixture fixture)
{
    [Fact]
    public void CompactHeaderFitsEveryLanguageWhileTracking()
    {
        fixture.Run(() =>
        {
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            var session = new TrackingSession(new(), WorkData.Empty);

            try
            {
                session.Start(session.Settings.SelectedTask);
                popup.LivePin.IsChecked = true;
                popup.Open(new(0, 0, 1, 1));

                foreach (var language in LanguageCatalog.All)
                {
                    Strings.Current.SetLanguage(language.Id);
                    popup.Render(session);
                    popup.UpdateLayout();

                    var title = (FrameworkElement)popup.LiveHeader.Children[0];
                    var available = popup.TrackingToggle.TranslatePoint(new(), popup.LiveHeader).X;

                    title.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
                    Assert.True(title.DesiredSize.Width <= available + 1,
                        $"{language.Id}: title needs {title.DesiredSize.Width}, tracking starts at {available}");
                }
            }
            finally
            {
                popup.Exit();
                Strings.Current.SetLanguage("en");
            }
        });
    }

    [Fact]
    public async Task IsolatedExitDoesNotOpenAConfirmationOrHideTheView()
    {
        var root = Path.Combine(Path.GetTempPath(), "VolturaEarner-tests", Guid.NewGuid().ToString("N"));
        AppRuntime runtime = null!;
        Task operation = Task.CompletedTask;
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.Run(() =>
        {
            runtime = new(new(root, false, true));
            runtime.Window.Opacity = 0;
            runtime.Window.ShowActivated = false;
            operation = runtime.InitializeAsync();
        });
        await operation;

        try
        {
            fixture.Run(() =>
            {
                runtime.Open();
                _ = runtime.ExecuteAsync("exit");
                runtime.Window.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().SingleOrDefault();

                    dialog?.Close();
                    observed.TrySetResult(dialog is not null);
                }, DispatcherPriority.ContextIdle);
            });
            Assert.False(await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            fixture.Run(() => Assert.True(runtime.Window.IsVisible));
        }
        finally
        {
            fixture.Run(() => operation = runtime.DisposeAsync().AsTask());
            await operation;
            fixture.Run(() => runtime.Window.Close());
            Directory.Delete(root, true);
        }
    }
}
