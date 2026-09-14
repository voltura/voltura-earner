using System.Threading.Channels;

namespace VolturaEarner.Platform;

internal sealed class ApplicationLog : IAsyncDisposable
{
    private readonly Channel<string> _entries = Channel.CreateBounded<string>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        }
    );
    private readonly Task _worker;
    public bool Enabled { get; set; }
    public string FilePath { get; }
    public ApplicationLog(string data)
    {
        FilePath = Path.Combine(data, "application.log");
        _worker = WriteAsync();
    }

    public void Record(string operation, Exception? error = null)
    {
        if (Enabled)
        {
            _entries.Writer.TryWrite(
                $"{DateTimeOffset.UtcNow:O} {operation} {error?.GetType().Name}"
            );
        }
    }

    private async Task WriteAsync()
    {
        await foreach (var entry in _entries.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= 1024 * 1024)
                {
                    File.Move(FilePath, FilePath + ".1", true);
                }

                await File.AppendAllTextAsync(FilePath, entry + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { /* Logging cannot stop calendar work. */
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _entries.Writer.TryComplete();
        await _worker;
    }
}
