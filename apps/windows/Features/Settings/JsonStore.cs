using VolturaEarner.Ui;
using System.Text.Json;

namespace VolturaEarner.Features.Settings;

public sealed class JsonStore<T>(string path, Action<T> validate) : IDisposable
{
    private const long MaximumBytes = 128 * 1024 * 1024;
    private readonly SemaphoreSlim _write = new(1, 1);
    private bool _loadedMissing;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 32 };
    public string FilePath => path;

    public Task<T?> LoadAsync(CancellationToken token = default) => LoadAsync(false, token);

    public Task<T?> LoadAsync(bool requireDirectory, CancellationToken token = default) => Task.Run(async () =>
    {
        FileStream stream;

        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        }
        catch (FileNotFoundException)
        {
            _loadedMissing = true;

            return default;
        }
        catch (DirectoryNotFoundException) when (!requireDirectory)
        {
            _loadedMissing = true;

            return default;
        }

        await using (stream)
        {
            if (stream.Length > MaximumBytes)
            {
                throw new InvalidDataException(Strings.Current["TheDataFileExceedsTheSupported128MiBLimitTheOriginalFileHasBeenPreserved"]);
            }

            var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, token) ?? throw new InvalidDataException(Strings.Current["TheDataFileIsEmptyOrInvalid"]);

            validate(value);
            _loadedMissing = false;

            return value;
        }
    }, token);

    public Task SaveAsync(T value, CancellationToken token = default) => SaveAsync(value, true, token);

    public async Task SaveAsync(T value, bool overwrite, CancellationToken token = default)
    {
        await _write.WaitAsync(token);

        try
        {
            await Task.Run(async () =>
            {
                validate(value);

                var directory = Path.GetDirectoryName(path)!;

                Directory.CreateDirectory(directory);

                var pending = Path.Combine(directory, ".earner-" + Guid.NewGuid().ToString("N") + ".pending");

                try
                {
                    await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    {
                        await JsonSerializer.SerializeAsync(stream, value, Options, token);

                        if (stream.Length > MaximumBytes)
                        {
                            throw new InvalidDataException(Strings.Current["WorkDataExceedsTheSupportedFileSizeExportAndArchiveOlderWorkBeforeContin"]);
                        }

                        await stream.FlushAsync(token);
                        stream.Flush(true);
                    }

                    token.ThrowIfCancellationRequested();
                    // A file that appeared after an absent-file load belongs to another history.
                    File.Move(pending, path, overwrite && !_loadedMissing);
                    _loadedMissing = false;
                }
                finally
                {
                    if (File.Exists(pending))
                    {
                        File.Delete(pending);
                    }
                }
            }, token);
        }
        finally
        {
            _write.Release();
        }
    }

    public void Dispose() => _write.Dispose();
}
