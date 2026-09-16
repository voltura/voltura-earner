using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using VolturaEarner.Ui;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed partial class ConfirmationWindowTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void ExitDialogHidesWindowsAndRestoresThemOnlyOnCancel(bool showMain, bool showLive, bool confirm)
    {
        fixture.Run(() =>
        {
            var main = new MainWindow { Opacity = 0, ShowActivated = false };
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            Exception? failure = null;

            try
            {
                main.Show();

                if (!showMain)
                {
                    main.Hide();
                }

                if (showLive)
                {
                    popup.LivePin.IsChecked = true;
                    popup.Open(new(0, 0, 1, 1));
                }

                main.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();

                    try
                    {
                        dialog.Opacity = 0;
                        Assert.False(main.IsVisible);
                        Assert.False(popup.IsVisible);
                        Assert.True(dialog.IsVisible);
                        Assert.True(dialog.Topmost);
                        Assert.True(dialog.CancelButton.IsVisible);
                        Assert.True(dialog.ConfirmButton.IsVisible);
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                    finally
                    {
                        var button = confirm
                            ? dialog.ConfirmButton
                            : dialog.CancelButton;

                        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }, DispatcherPriority.ContextIdle);

                Assert.Equal(confirm, ConfirmationWindow.ConfirmExit(main, popup, Strings.Current["CloseEarner"], Strings.Current["Close"], "\uE8BB"));

                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                Assert.Equal(!confirm && showMain, main.IsVisible);
                Assert.Equal(!confirm && showLive, popup.IsVisible);
                Assert.Equal(showLive, popup.IsPinned);
            }
            finally
            {
                popup.Exit();
                main.Close();
            }
        });
    }

    [Fact]
    public void ExitFromTrayWorksBeforeMainWindowHasEverBeenShown()
    {
        fixture.Run(() =>
        {
            var main = new MainWindow { Opacity = 0, ShowActivated = false };
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };

            try
            {
                popup.LivePin.IsChecked = true;
                popup.Open(new(0, 0, 1, 1));
                main.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();

                    dialog.Opacity = 0;
                    Assert.False(main.IsVisible);
                    Assert.False(popup.IsVisible);
                    dialog.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }, DispatcherPriority.ContextIdle);

                Assert.False(ConfirmationWindow.ConfirmExit(main, popup, Strings.Current["CloseEarner"], Strings.Current["Close"], "\uE8BB"));
                Assert.False(main.IsVisible);
                Assert.True(popup.IsVisible);
            }
            finally
            {
                popup.Exit();
                main.Close();
            }
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ExitConfirmationStaysAbovePinnedEarnerViews(bool showMain, bool pinMain, bool minimal)
    {
        fixture.Run(() =>
        {
            var main = new MainWindow { Opacity = 0, ShowActivated = false, Topmost = pinMain };
            var popup = new TrayEarnerWindow { Opacity = 0, ShowActivated = false };
            Exception? failure = null;

            try
            {
                main.Show();

                if (!showMain)
                {
                    main.Hide();
                }

                popup.SetMinimalView(minimal);
                popup.LivePin.IsChecked = true;
                popup.Open(new(0, 0, 1, 1));
                main.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single();

                    try
                    {
                        dialog.Opacity = 0;
                        dialog.UpdateLayout();
                        Assert.True(dialog.Topmost);
                        AssertAbove(dialog, popup);

                        if (showMain)
                        {
                            AssertAbove(dialog, main);
                        }

                        Assert.True(dialog.CancelButton.IsVisible);
                        Assert.True(dialog.ConfirmButton.IsVisible);
                        Assert.True(dialog.CancelButton.IsEnabled);
                        Assert.True(dialog.ConfirmButton.IsEnabled);
                    }
                    catch (Exception error)
                    {
                        failure = error;
                    }
                    finally
                    {
                        dialog.CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    }
                }, DispatcherPriority.ContextIdle);

                Assert.False(ConfirmationWindow.Confirm(main, Strings.Current["CloseEarner"], Strings.Current["Close"], "\uE8BB"));

                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                Assert.True(popup.IsVisible);
                Assert.True(popup.IsPinned);
                Assert.Equal(pinMain, main.Topmost);
            }
            finally
            {
                popup.Exit();
                main.Close();
            }
        });
    }

    private static void AssertAbove(Window dialog, Window other)
    {
        var dialogHandle = new WindowInteropHelper(dialog).Handle;

        for (var handle = GetWindow(new WindowInteropHelper(other).Handle, 3); handle != 0; handle = GetWindow(handle, 3))
        {
            if (handle == dialogHandle)
            {
                return;
            }
        }

        Assert.Fail("The confirmation must be above the other Earner window in native window order.");
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);
}
