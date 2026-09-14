using System.Windows.Threading;

namespace VolturaEarner.Platform;

internal static class SessionEndingSave
{
    internal static bool Complete(Dispatcher dispatcher, Func<CancellationToken, Task<bool>> save, TimeSpan timeout)
    {
        // Windows needs a synchronous answer. Pump the dispatcher while the save awaits I/O,
        // and decline shutdown if it cannot complete within the Windows query budget.
        using var cancellation = new CancellationTokenSource();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = timeout };
        var saved = false;
        var timedOut = false;

        timer.Tick += Timeout;
        timer.Start();

        var completion = SaveAsync();

        try
        {
            if (!completion.IsCompleted)
            {
                Dispatcher.PushFrame(frame);
            }

            return saved && !timedOut;
        }
        finally
        {
            timer.Stop();
            timer.Tick -= Timeout;
            cancellation.Cancel();
        }

        void Timeout(object? sender, EventArgs args)
        {
            timedOut = true;
            frame.Continue = false;
        }

        async Task SaveAsync()
        {
            try
            {
                saved = await save(cancellation.Token);
            }
            catch (Exception)
            {
                // An unsuccessful callback can never authorize Windows to discard the session.
                saved = false;
            }
            finally
            {
                frame.Continue = false;
            }
        }
    }
}
