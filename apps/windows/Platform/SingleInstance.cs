using System.Security.Cryptography;
using System.Text;

namespace VolturaEarner.Platform;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\VolturaEarner";
    private const string ActivationEventName = @"Local\VolturaEarner-Activate";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _wait;
    public bool IsFirst { get; }
    public SingleInstance(string? isolatedDirectory = null)
    {
        var suffix = isolatedDirectory is null
            ? string.Empty
            : "-Isolated-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolatedDirectory)).ToUpperInvariant())));

        _mutex = new Mutex(true, MutexName + suffix, out var created);
        IsFirst = created;
        _activation = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ActivationEventName + suffix
        );

        if (!created)
        {
            _activation.Set();
        }
    }

    public void Listen(Action activate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _activation,
            (_, _) => activate(),
            null,
            Timeout.Infinite,
            false
        );

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activation.Dispose();

        if (IsFirst)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
