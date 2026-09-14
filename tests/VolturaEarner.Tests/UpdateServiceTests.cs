using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VolturaEarner.Features.Updates;
using VolturaEarner.Platform;

using Xunit;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class UpdateServiceTests(WpfTestFixture fixture) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "VolturaEarner-tests",
        Guid.NewGuid().ToString("N")
    );

    [Fact]
    public async Task RechecksReuseReadyInstallerButDownloadNewerOrMissingPackages()
    {
        using var key = RSA.Create(2048);
        var handler = new ReleaseHandler(key);
        await using var service = new UpdateService(
            new(_root, false, true),
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await service.CheckAsync();

        var installer = Directory
            .GetFiles(Path.Combine(_root, "Updates", "pending"), "*.exe")
            .Single();

        await service.CheckAsync();
        Assert.Equal(UpdateStatus.Ready, service.State.Status);
        Assert.Single(handler.Requests, name => name.EndsWith(".exe", StringComparison.Ordinal));
        File.Delete(installer);
        await service.CheckAsync();
        Assert.True(service.State.Ready);
        Assert.True(File.Exists(installer));
        Assert.Equal(
            2,
            handler.Requests.Count(name => name.EndsWith(".exe", StringComparison.Ordinal))
        );
        handler.SetRelease(key, "1.2.0");
        await service.CheckAsync();
        Assert.Equal(UpdateStatus.Ready, service.State.Status);
        Assert.EndsWith(
            "1.2.0-win-x64.exe",
            Directory.GetFiles(Path.Combine(_root, "Updates", "pending"), "*.exe").Single(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task CheckAndRestoreKeepIoAndVerificationOffDispatcher()
    {
        using var key = RSA.Create(2048);
        var handler = new ReleaseHandler(key);
        await using var service = new UpdateService(
            new(_root, false, true),
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );
        Task check = Task.CompletedTask;
        var dispatcherThread = 0;
        var callbackThreads = new List<int>();

        service.Changed += () => callbackThreads.Add(Environment.CurrentManagedThreadId);
        fixture.Run(() =>
        {
            dispatcherThread = Environment.CurrentManagedThreadId;
            check = service.CheckAsync();
        });
        await check;
        Assert.True(service.State.Ready);
        Assert.NotEmpty(callbackThreads);
        Assert.All(callbackThreads, thread => Assert.NotEqual(dispatcherThread, thread));
        Assert.All(handler.RequestThreads, thread => Assert.NotEqual(dispatcherThread, thread));
        callbackThreads.Clear();

        await using var restart = new UpdateService(
            new(_root, false, true),
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        restart.Changed += () => callbackThreads.Add(Environment.CurrentManagedThreadId);
        fixture.Run(() => check = restart.RestorePendingAsync());
        await check;
        Assert.Equal(UpdateStatus.Ready, restart.State.Status);
        Assert.Single(callbackThreads);
        Assert.NotEqual(dispatcherThread, callbackThreads[0]);
    }

    [Fact]
    public async Task ShutdownCancelsInFlightCheckAndConcurrentCheckDoesNotDuplicateRequest()
    {
        using var handler = new WaitingHandler();
        var service = new UpdateService(new(_root, false, true), handler, eligible: true);
        Task check = Task.CompletedTask;

        try
        {
            fixture.Run(() => check = service.CheckAsync());
            await handler.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await service.CheckAsync();
            Assert.Equal(1, handler.Requests);
        }
        finally
        {
            Task shutdown = Task.CompletedTask;

            fixture.Run(() => shutdown = service.DisposeAsync().AsTask());
            await shutdown.WaitAsync(TestContext.Current.CancellationToken);
        }

        await check;
        Assert.False(service.State.Ready);
    }

    [Fact]
    public async Task VerifiedDownloadRestoresAfterRestartAndCorruptContentIsRejected()
    {
        using var key = RSA.Create(2048);
        var paths = new AppPaths(_root, false, true);

        await using (
            var service = new UpdateService(
                paths,
                new ReleaseHandler(key),
                key.ExportSubjectPublicKeyInfoPem(),
                true,
                false,
            current: new(1, 0, 0)
            )
        )
        {
            await service.CheckAsync();
            Assert.True(service.State.Ready);
            Assert.Equal(UpdateStatus.Ready, service.State.Status);
        }

        await using var restored = new UpdateService(
            paths,
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await restored.RestorePendingAsync();
        Assert.True(restored.State.Ready);

        var installer = Directory
            .GetFiles(Path.Combine(_root, "Updates", "pending"), "*.exe")
            .Single();

        await File.WriteAllBytesAsync(installer, [9, 9, 9], TestContext.Current.CancellationToken);
        Assert.False(await restored.InstallAsync());
        Assert.Equal(UpdateStatus.UpdateVerificationFailed, restored.State.Status);
        Assert.False(restored.State.Ready);
    }

    [Theory]
    [InlineData("corrupt", "UpdateVerificationFailed")]
    [InlineData("oversized", "UpdateDownloadFailed")]
    [InlineData("bad-origin", "UpdateDownloadFailed")]
    [InlineData("malformed", "UpdateCheckFailed")]
    [InlineData("offline", "UpdateCheckFailed")]
    public async Task FailedDownloadNeverBecomesReady(string failure, string expectedStatus)
    {
        using var key = RSA.Create(2048);
        await using var service = new UpdateService(
            new(_root, false, true),
            new ReleaseHandler(key, failure),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await service.CheckAsync();
        Assert.False(service.State.Ready);
        Assert.Equal(expectedStatus, service.State.Status.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "Updates", "pending", "manifest.json")));
    }

    [Fact]
    public async Task InterruptedMetadataCannotRestoreButCanBeDownloadedAgain()
    {
        using var key = RSA.Create(2048);
        var pending = Path.Combine(_root, "Updates", "pending");

        Directory.CreateDirectory(pending);
        await File.WriteAllTextAsync(
            Path.Combine(pending, "manifest.pending"),
            "incomplete",
            TestContext.Current.CancellationToken
        );

        await using var service = new UpdateService(
            new(_root, false, true),
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await service.RestorePendingAsync();
        Assert.False(service.State.Ready);
        await service.CheckAsync();
        Assert.True(service.State.Ready);
    }

    [Fact]
    public async Task PortableCopyCannotDownloadOrBecomeEligible()
    {
        using var key = RSA.Create(2048);
        await using var service = new UpdateService(
            new(_root, true, false),
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem()
        );

        await service.CheckAsync();
        Assert.False(service.Eligible);
        Assert.False(service.State.Ready);
        Assert.False(await service.InstallAsync());
    }

    [Theory]
    [InlineData(false, "1.1.0")]
    [InlineData(true, "1.1.0")]
    [InlineData(false, "1.2.0")]
    [InlineData(true, "1.2.0")]
    public async Task InstalledUpgradeExpiresCacheAndOnlyOnlineCheckReportsCurrent(
        bool full,
        string installed
    )
    {
        using var key = RSA.Create(2048);
        var paths = new AppPaths(_root, false, true);

        await using (
            var previous = new UpdateService(
                paths,
                new ReleaseHandler(key),
                key.ExportSubjectPublicKeyInfoPem(),
                true,
                full,
            current: new(1, 0, 0)
            )
        )
        {
            await previous.CheckAsync();
            Assert.True(previous.State.Ready);
        }

        var handler = new ReleaseHandler(key);
        await using var upgraded = new UpdateService(
            paths,
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            full,
            Version.Parse(installed)
        );

        await upgraded.RestorePendingAsync();
        Assert.Equal(UpdateStatus.Idle, upgraded.State.Status);
        Assert.False(upgraded.State.Ready);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Updates", "pending")));
        Assert.Empty(handler.Requests);
        await upgraded.CheckAsync();
        Assert.Equal(UpdateStatus.Current, upgraded.State.Status);
        Assert.Equal("latest", Assert.Single(handler.Requests));
        await upgraded.RestorePendingAsync();
        Assert.Equal(UpdateStatus.Current, upgraded.State.Status);
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("signature.sig")]
    [InlineData("VolturaEarner-Setup-1.1.0-win-x64.exe")]
    public async Task DamagedCacheIsDiscardedOnStartupAndCanBeDownloadedAgain(string damaged)
    {
        using var key = RSA.Create(2048);
        var paths = new AppPaths(_root, false, true);

        await using (
            var previous = new UpdateService(
                paths,
                new ReleaseHandler(key),
                key.ExportSubjectPublicKeyInfoPem(),
                true,
                false,
            current: new(1, 0, 0)
            )
        )
        {
            await previous.CheckAsync();
        }

        await File.WriteAllBytesAsync(
            Path.Combine(_root, "Updates", "pending", damaged),
            [9, 9, 9],
            TestContext.Current.CancellationToken
        );

        await using var restarted = new UpdateService(
            paths,
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await restarted.RestorePendingAsync();
        Assert.Equal(UpdateStatus.Idle, restarted.State.Status);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Updates", "pending")));
        await restarted.CheckAsync();
        Assert.True(restarted.State.Ready);
    }

    [Fact]
    public async Task FailedRecheckAndCurrentReleaseNeverLeaveAnInstallAction()
    {
        using var key = RSA.Create(2048);
        var handler = new ReleaseHandler(key);
        await using var service = new UpdateService(
            new(_root, false, true),
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await service.CheckAsync();
        Assert.True(service.State.Ready);
        handler.Failure = "offline";
        await service.CheckAsync();
        Assert.Equal(UpdateStatus.UpdateCheckFailed, service.State.Status);
        Assert.Null(service.State.Installer);
        handler.Failure = "";
        await service.CheckAsync();
        Assert.True(service.State.Ready);
        Assert.Single(handler.Requests, name => name.EndsWith(".exe", StringComparison.Ordinal));
        handler.SetRelease(key, "1.0.0");
        await service.CheckAsync();
        Assert.Equal(UpdateStatus.Current, service.State.Status);
        Assert.Null(service.State.Installer);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Updates", "pending")));
    }

    [Fact]
    public async Task InterruptedReplacementCannotRestoreAMixedPackage()
    {
        using var key = RSA.Create(2048);
        var handler = new ReleaseHandler(key);
        var paths = new AppPaths(_root, false, true);

        await using (
            var service = new UpdateService(
                paths,
                handler,
                key.ExportSubjectPublicKeyInfoPem(),
                true,
                false,
            current: new(1, 0, 0)
            )
        )
        {
            await service.CheckAsync();
            handler.SetRelease(key, "1.2.0");
            handler.Failure = "oversized";
            await service.CheckAsync();
            Assert.Equal(UpdateStatus.UpdateDownloadFailed, service.State.Status);
            Assert.False(File.Exists(Path.Combine(_root, "Updates", "pending", "manifest.json")));
        }

        var retryHandler = new ReleaseHandler(key);

        retryHandler.SetRelease(key, "1.2.0");

        await using var restart = new UpdateService(
            paths,
            retryHandler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0)
        );

        await restart.RestorePendingAsync();
        Assert.Equal(UpdateStatus.Idle, restart.State.Status);
        await restart.CheckAsync();
        Assert.True(restart.State.Ready);
        Assert.EndsWith("1.2.0-win-x64.exe", restart.State.Installer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LockedObsoleteInstallerDoesNotFailCheckOrDeleteUnrelatedFiles()
    {
        using var key = RSA.Create(2048);
        var paths = new AppPaths(_root, false, true);
        string installer;

        await using (
            var previous = new UpdateService(
                paths,
                new ReleaseHandler(key),
                key.ExportSubjectPublicKeyInfoPem(),
                true,
                false,
            current: new(1, 0, 0)
            )
        )
        {
            await previous.CheckAsync();
            installer = previous.State.Installer!;
        }

        var unrelated = Path.Combine(Path.GetDirectoryName(installer)!, "keep.txt");

        await File.WriteAllTextAsync(unrelated, "user data", TestContext.Current.CancellationToken);

        await using var upgraded = new UpdateService(
            paths,
            new ReleaseHandler(key),
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            new(1, 1, 0)
        );

        using (File.Open(installer, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await upgraded.RestorePendingAsync();
            Assert.Equal(UpdateStatus.Idle, upgraded.State.Status);
            await upgraded.CheckAsync();
            Assert.Equal(UpdateStatus.Current, upgraded.State.Status);
        }

        await upgraded.CheckAsync();
        Assert.False(File.Exists(installer));
        Assert.Equal(
            "user data",
            await File.ReadAllTextAsync(unrelated, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task InstallerHandoffPreventsChecksUntilApplicationShutdown()
    {
        using var key = RSA.Create(2048);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        var handler = new ReleaseHandler(key);
        await using var service = new UpdateService(
            new(_root, false, true),
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0),
            install: async (path, token) =>
            {
                Assert.True(File.Exists(path));
                launches++;
                opened.TrySetResult();
                await closed.Task.WaitAsync(token);
            }
        );

        await service.CheckAsync();

        var install = service.InstallAsync();

        try
        {
            await opened.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            Assert.True(service.State.Busy);

            var requests = handler.Requests.Count;

            await service.CheckAsync();
            Assert.False(await service.InstallAsync());
            Assert.Equal(requests, handler.Requests.Count);
            Assert.Equal(1, launches);
        }
        finally
        {
            closed.TrySetResult();
        }

        Assert.True(await install);
        Assert.True(service.State.Busy);
        Assert.False(await service.InstallAsync());
        await service.CheckAsync();
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task ConcurrentOperationsDoNotQueueBeforeTheBusyStateIsPublished()
    {
        using var key = RSA.Create(2048);
        var handler = new ReleaseHandler(key);
        var launches = 0;
        await using var service = new UpdateService(
            new(_root, false, true),
            handler,
            key.ExportSubjectPublicKeyInfoPem(),
            true,
            false,
            current: new(1, 0, 0),
            install: (_, _) =>
            {
                launches++;

                return Task.CompletedTask;
            }
        );
        var gate = (SemaphoreSlim)typeof(UpdateService)
            .GetField(
                "_gate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .GetValue(service)!;
        // Hold the same gate as an operation which has acquired it but has not
        // yet resumed after ForceYielding to publish its busy state.

        await gate.WaitAsync(TestContext.Current.CancellationToken);

        Task check;
        Task<bool> install;
        bool returnedImmediately;

        try
        {
            check = service.CheckAsync();
            install = service.InstallAsync();
            returnedImmediately = check.IsCompleted && install.IsCompleted;
        }
        finally
        {
            gate.Release();
        }

        await check;

        var installed = await install;

        Assert.True(returnedImmediately);
        Assert.False(installed);
        Assert.Empty(handler.Requests);
        Assert.Equal(0, launches);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    internal sealed class ReleaseHandler : HttpMessageHandler
    {
        private string _version = "1.1.0";
        private string Installer => $"VolturaEarner-Setup-{_version}-win-x64.exe";
        private byte[] _manifest = [];
        private byte[] _signature = [];
        public string Failure { get; set; }
        public List<string> Requests { get; } = [];
        public List<int> RequestThreads { get; } = [];
        public ReleaseHandler(RSA key, string failure = "")
        {
            Failure = failure;
            SetRelease(key, _version);
        }

        public void SetRelease(RSA key, string version)
        {
            _version = version;
            _manifest = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schema = 1,
                    version = _version,
                    assets = new[]
                    {
                        Installer,
                        Installer.Replace(".exe", "-full.exe", StringComparison.Ordinal),
                    }.Select(name => new
                    {
                        name,
                        size = 3,
                        sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })),
                    }),
                }
            );
            _signature = key.SignData(_manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Failure == "offline")
            {
                throw new HttpRequestException("Simulated offline.");
            }

            var name = Path.GetFileName(request.RequestUri!.AbsolutePath);

            Requests.Add(name);
            RequestThreads.Add(Environment.CurrentManagedThreadId);

            byte[] body;

            if (name == "latest")
            {
                body =
                    Failure == "malformed"
                        ? "{}"u8.ToArray()
                        : JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            tag_name = "v" + _version,
                            draft = false,
                            prerelease = false,
                            assets = new[] { Installer, Installer.Replace(".exe", "-full.exe", StringComparison.Ordinal), $"VolturaEarner-Update-{_version}.json", $"VolturaEarner-Update-{_version}.sig", }.Select(asset => new
                            {
                                name = asset,
                                browser_download_url = $"https://{(Failure == "bad-origin"
                            ? "example.com"
                            : "github.com")}/voltura/voltura-earner/releases/download/v{_version}/{asset}",
                            }).ToArray(),
                        });
            }
            else if (name.EndsWith(".json", StringComparison.Ordinal))
            {
                body = _manifest;
            }
            else if (name.EndsWith(".sig", StringComparison.Ordinal))
            {
                body = _signature;
            }
            else
            {
                body = Failure switch
                {
                    "corrupt" => [4, 5, 6],

                    "oversized" => [1, 2, 3, 4],

                    _ => [1, 2, 3],
                };
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            );
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
