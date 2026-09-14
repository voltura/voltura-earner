using VolturaEarner.Ui;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using VolturaEarner.Platform;

namespace VolturaEarner.Features.Updates;

internal sealed class UpdateService : IAsyncDisposable
{
    public const string ProjectUrl = "https://github.com/voltura/voltura-earner";
    public const string ProductUrl = "https://voltura.github.io/voltura-earner/";
    private static readonly string[] MetadataFiles =
    [
        "manifest.json",
        "manifest.pending",
        "signature.sig",
        "installer.pending",
    ];
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _pending;
    private readonly string _publicKey;
    private readonly bool _full;
    private readonly Version _current;
    private readonly ApplicationLog? _log;
    private readonly Func<string, CancellationToken, Task> _install;
    private Task? _worker;
    private volatile bool _automatic;
    private volatile UpdateState _state = new(UpdateStatus.Idle);
    public UpdateState State => _state;
    public bool Eligible { get; }
    public event Action? Changed;

    internal UpdateService(
        AppPaths paths,
        HttpMessageHandler? handler = null,
        string? publicKey = null,
        bool? eligible = null,
        bool? full = null,
        Version? current = null,
        ApplicationLog? log = null,
        Func<string, CancellationToken, Task>? install = null
    )
    {
        _pending = Path.Combine(paths.Data, "Updates", "pending");
        _log = log;
        _install = install ?? LaunchInstallerAsync;
        _current = current ?? typeof(UpdateService).Assembly.GetName().Version ?? new(1, 0, 0);
        _http = new HttpClient(
            handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }
        )
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VolturaEarner"
        );
        var location = key?.GetValue("InstallLocation") as string;

        Eligible =
            eligible
            ?? (
                !paths.Portable
                && !paths.Isolated
                && location is not null
                && Path.GetFullPath(location)
                    .TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(
                        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase
                    )
            );
        _full = full ?? (key?.GetValue("PackageVariant") as string == "full");

        using var resource = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream(
                "VolturaEarner.Features.Updates.update-signing-public.pem"
            );
        using var reader = new StreamReader(
            resource
                ?? throw new InvalidOperationException(
                    "Release verification key is not configured."
                )
        );

        _publicKey = publicKey ?? reader.ReadToEnd();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("VolturaEarner/1.0");

        if (!Eligible)
        {
            _state = new(UpdateStatus.ManualUpdates);
        }
    }

    public void Start(bool automatic)
    {
        _automatic = automatic;
        _worker ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            if (!Eligible)
            {
                return;
            }

            await RestorePendingAsync();
            await Task.Delay(TimeSpan.FromMinutes(2), _stop.Token);

            while (!_stop.IsCancellationRequested)
            {
                if (_automatic && !State.Ready)
                {
                    await CheckAsync();
                }

                await Task.Delay(TimeSpan.FromDays(1), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task CheckAsync()
    {
        if (!Eligible || State.Busy || !await _gate.WaitAsync(0, _stop.Token))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);

        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        var token = timeout.Token;
        var failure = UpdateStatus.UpdateCheckFailed;

        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            SetState(UpdateStatus.Checking);

            var metadata = await DownloadBytesAsync(
                new Uri("https://api.github.com/repos/voltura/voltura-earner/releases/latest"),
                256 * 1024,
                token
            );
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;

            if (
                root.GetProperty("draft").GetBoolean()
                || root.GetProperty("prerelease").GetBoolean()
                || !tag.StartsWith('v')
                || !UpdateVerifier.TryVersion(tag[1..], out var latest)
            )
            {
                throw new InvalidDataException(Strings.Current["InvalidRelease"]);
            }

            if (latest <= _current)
            {
                DiscardPending();
                SetState(UpdateStatus.Current);

                return;
            }

            var cached = await RecoverPendingAsync(token);
            var expected =
                $"VolturaEarner-Setup-{tag[1..]}-win-x64{(_full
                    ? "-full"
                    : "")}.exe";

            if (cached is not null && Path.GetFileName(cached) == expected)
            {
                SetState(UpdateStatus.Ready, cached);

                return;
            }

            var assets = root.GetProperty("assets").EnumerateArray().ToArray();

            Uri AssetUri(string name)
            {
                var matches = assets
                    .Where(value => value.GetProperty("name").GetString() == name)
                    .ToArray();

                if (matches.Length != 1)
                {
                    throw new InvalidDataException(Strings.Current["MissingReleaseAsset"]);
                }

                var uri = new Uri(matches[0].GetProperty("browser_download_url").GetString()!);

                if (
                    uri.Host != "github.com"
                    || !uri.AbsolutePath.StartsWith(
                        "/voltura/voltura-earner/releases/download/",
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidDataException(Strings.Current["UnexpectedReleaseOrigin"]);
                }

                return uri;
            }

            failure = UpdateStatus.UpdateDownloadFailed;

            var manifest = await DownloadBytesAsync(
                AssetUri($"VolturaEarner-Update-{tag[1..]}.json"),
                64 * 1024,
                token
            );
            var signature = await DownloadBytesAsync(
                AssetUri($"VolturaEarner-Update-{tag[1..]}.sig"),
                1024,
                token
            );

            failure = UpdateStatus.UpdateVerificationFailed;

            var verified = UpdateVerifier.Verify(manifest, signature, _publicKey, _full);

            if (verified.Version != tag[1..])
            {
                throw new InvalidDataException(Strings.Current["ReleaseVersionMismatch"]);
            }

            failure = UpdateStatus.UpdateDownloadFailed;
            SetState(UpdateStatus.Downloading);
            Directory.CreateDirectory(_pending);
            // One package, with the manifest as its commit marker. Invalidate before replacement.
            File.Delete(Path.Combine(_pending, "manifest.json"));
            DiscardPending();

            var part = Path.Combine(_pending, "installer.pending");

            await using (
                var output = new FileStream(
                    part,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true
                )
            )
            {
                await DownloadAsync(
                    AssetUri(verified.Asset.Name),
                    output,
                    verified.Asset.Size,
                    token
                );
            }

            failure = UpdateStatus.UpdateVerificationFailed;
            await UpdateVerifier.VerifyFileAsync(part, verified.Asset, token);
            failure = UpdateStatus.UpdateDownloadFailed;

            var installer = Path.Combine(_pending, verified.Asset.Name);

            File.Move(part, installer, true);
            await File.WriteAllBytesAsync(
                Path.Combine(_pending, "signature.sig"),
                signature,
                token
            );
            await File.WriteAllBytesAsync(
                Path.Combine(_pending, "manifest.pending"),
                manifest,
                token
            );
            File.Move(
                Path.Combine(_pending, "manifest.pending"),
                Path.Combine(_pending, "manifest.json"),
                true
            );
            SetState(UpdateStatus.Ready, installer);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (IsUpdateError(error))
        {
            Fail(failure, error);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task RestorePendingAsync()
    {
        if (!Eligible)
        {
            return;
        }

        await _gate.WaitAsync(_stop.Token);

        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

            if (State.Status != UpdateStatus.Idle)
            {
                return; // Do not overwrite a completed manual check.
            }

            var installer = await RecoverPendingAsync(_stop.Token);

            SetState(installer is null
                ? UpdateStatus.Idle
                : UpdateStatus.Ready, installer);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> RecoverPendingAsync(CancellationToken token)
    {
        try
        {
            return await ReadPendingAsync(token);
        }
        catch (Exception error)
            when (IsUpdateError(error) && error is not OperationCanceledException)
        {
            _log?.Record("Discard unusable update cache", error);
            DiscardPending();

            return null;
        }
    }

    private async Task<string?> ReadPendingAsync(CancellationToken token)
    {
        if (!File.Exists(Path.Combine(_pending, "manifest.json")))
        {
            DiscardPending();

            return null;
        }

        var manifest = await ReadBoundedAsync(
            Path.Combine(_pending, "manifest.json"),
            64 * 1024,
            token
        );
        var signature = await ReadBoundedAsync(
            Path.Combine(_pending, "signature.sig"),
            1024,
            token
        );
        var verified = UpdateVerifier.Verify(manifest, signature, _publicKey, _full);
        // A valid package already installed is ordinary cache expiry, not a verification error.

        if (Version.Parse(verified.Version) <= _current)
        {
            DiscardPending();

            return null;
        }

        var installer = Path.Combine(_pending, verified.Asset.Name);

        await UpdateVerifier.VerifyFileAsync(installer, verified.Asset, token);

        return installer;
    }

    public async Task<bool> InstallAsync()
    {
        if (!Eligible || State.Busy || !await _gate.WaitAsync(0, _stop.Token))
        {
            return false;
        }

        var failure = UpdateStatus.UpdateVerificationFailed;

        try
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            SetState(UpdateStatus.Installing);

            var installer =
                await ReadPendingAsync(_stop.Token)
                ?? throw new InvalidDataException(Strings.Current["NoNewerCachedUpdate"]);

            failure = UpdateStatus.UpdateInstallFailed;
            // Keep the service busy after handoff; AppRuntime owns the saved shutdown.
            await _install(installer, _stop.Token);

            return true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
            when (IsUpdateError(error) || error is System.ComponentModel.Win32Exception)
        {
            Fail(failure, error);

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Task LaunchInstallerAsync(string installer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        using var process =
            Process.Start(
                new ProcessStartInfo(installer)
                {
                    UseShellExecute = true,
                }
            ) ?? throw new InvalidOperationException(Strings.Current["InstallerDidNotStart"]);

        return Task.CompletedTask;
    }

    private void DiscardPending()
    {
        // Nonrecursive and limited to updater-owned files. Cleanup must not break a valid check.
        try
        {
            if (!Directory.Exists(_pending))
            {
                return;
            }

            var paths = Directory
                .EnumerateFiles(_pending, "VolturaEarner-Setup-*.exe")
                .Concat(MetadataFiles.Select(name => Path.Combine(_pending, name)));

            foreach (var path in paths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    _log?.Record("Clean update cache", error);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log?.Record("Clean update cache", error);
        }
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (var redirects = 0; redirects < 4; redirects++)
        {
            if (
                uri.Scheme != "https"
                || (
                    uri.Host
                    is not (
                        "github.com"
                        or "api.github.com"
                        or "release-assets.githubusercontent.com"
                        or "objects.githubusercontent.com"
                    )
                )
            )
            {
                throw new InvalidDataException(Strings.Current["UnexpectedDownloadOrigin"]);
            }

            var response = await _http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                token
            );

            if (
                (int)response.StatusCode is >= 300 and < 400
                && response.Headers.Location is { } location
            )
            {
                uri = location.IsAbsoluteUri
                    ? location
                    : new Uri(uri, location);
                response.Dispose();
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;

                response.Dispose();
                throw new HttpRequestException(Strings.Current["DownloadFailed"], null, status);
            }

            return response;
        }

        throw new InvalidDataException(Strings.Current["TooManyRedirects"]);
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream();

        await DownloadAsync(uri, output, maximum, token);

        return output.ToArray();
    }

    private async Task DownloadAsync(Uri uri, Stream output, long maximum, CancellationToken token)
    {
        using var response = await GetAsync(uri, token);
        await using var input = await response.Content.ReadAsStreamAsync(token);

        await CopyBoundedAsync(input, output, maximum, token);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string path,
        int maximum,
        CancellationToken token
    )
    {
        await using var file = File.OpenRead(path);
        using var output = new MemoryStream();

        await CopyBoundedAsync(file, output, maximum, token);

        return output.ToArray();
    }

    private static async Task CopyBoundedAsync(
        Stream input,
        Stream output,
        long maximum,
        CancellationToken token
    )
    {
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, token);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > maximum)
            {
                throw new InvalidDataException(Strings.Current["DownloadExceedsSizeLimit"]);
            }

            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static bool IsUpdateError(Exception error) =>
        error
            is InvalidDataException
                or HttpRequestException
                or IOException
                or UnauthorizedAccessException
                or JsonException
                or System.Security.Cryptography.CryptographicException
                or InvalidOperationException
                or KeyNotFoundException
                or FormatException
                or OperationCanceledException
                or ArgumentException;

    private void Fail(UpdateStatus status, Exception error)
    {
        _log?.Record(status.ToString(), error);
        SetState(status);
    }

    private void SetState(UpdateStatus status, string? installer = null)
    {
        _state = new(status, installer);
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_worker is not null)
        {
            await _worker;
        }

        await _gate.WaitAsync();
        _gate.Release();
        _http.Dispose();
        _gate.Dispose();
        _stop.Dispose();
    }
}
