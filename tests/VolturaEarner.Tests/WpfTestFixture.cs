using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using Xunit;

namespace VolturaEarner.Tests;

[CollectionDefinition("WPF")]
public sealed class WpfTestGroup : ICollectionFixture<WpfTestFixture>;

public sealed partial class WpfTestFixture : IDisposable
{
    private readonly Thread _thread;
    private readonly Application _app;

    public WpfTestFixture()
    {
        Application? app = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            var previous = SetThreadDpiAwarenessContext(new nint(-4));

            try
            {
                // Use the real resource declarations with a plain Application:
                // pumping WPF must not start the product runtime or normal profile.
                var markup = XDocument.Load(
                    Path.Combine(AppContext.BaseDirectory, "UiTestApp.xaml")
                );

                markup
                    .Root!.Attribute(
                        XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml")
                    )!
                    .Remove();
                app = (Application)XamlReader.Parse(markup.ToString().Replace("clr-namespace:VolturaEarner.Ui", "clr-namespace:VolturaEarner.Ui;assembly=VolturaEarner", StringComparison.Ordinal));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ready.Set();
            }

            try
            {
                if (failure is null)
                {
                    Dispatcher.Run();
                }
            }
            finally
            {
                _ = SetThreadDpiAwarenessContext(previous);
            }
        })
        {
            IsBackground = true,
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        _app = app!;
    }

    public void Run(Action action) => _app.Dispatcher.Invoke(action);

    public void Dispose()
    {
        _app.Dispatcher.Invoke(() =>
        {
            _app.Shutdown();
            _app.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        });

        if (!_thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("The WPF test dispatcher did not stop.");
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);
}
