using System.Buffers.Binary;
using System.Security;
using System.Windows.Threading;
using Microsoft.Win32;
using VolturaEarner.Platform;
using Xunit;

namespace VolturaEarner.Tests;

[Collection("WPF")]
public sealed class TrayIconPlacementTests(WpfTestFixture fixture)
{
    private static readonly string Executable = Path.Combine(Path.GetTempPath(), "VolturaEarner.exe");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MovesOnlyTheMatchingIdentifierToTheRightmostEnd(int index)
    {
        ulong[] identifiers = [ulong.MaxValue, 0x0102030405060708, 42];
        var store = new FakeStore { Order = Encode(identifiers), Matches = [identifiers[index]] };
        var original = (byte[])store.Order.Clone();
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(Encode(identifiers.Where((_, i) => i != index).Append(identifiers[index]).ToArray()), store.Order);
        Assert.Equal(index == 2
            ? 0
            : 1, store.Writes);
        Assert.Equal(original, Encode(identifiers));
    }

    [Fact]
    public void WaitsForRegistrationWithinTheSameAttempt()
    {
        var store = new FakeStore { Order = null, Matches = [] };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.False(placement.TryPlace());

        store.Order = Encode(1, 2);

        Assert.False(placement.TryPlace());

        store.Matches.Add(1);

        Assert.True(placement.TryPlace());
        Assert.Equal(Encode(2, 1), store.Order);
        Assert.Equal(1, store.Claims);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("unaligned")]
    [InlineData("oversized")]
    [InlineData("duplicate")]
    [InlineData("ambiguous")]
    public void InvalidOrAmbiguousDataIsNeverWrittenOrRetried(string scenario)
    {
        var order = scenario switch
        {
            "empty" => [],
            "unaligned" => new byte[9],
            "oversized" => new byte[65544],
            "duplicate" => Encode(1, 2, 1),
            _ => Encode(1, 2, 3),
        };
        var store = new FakeStore
        {
            Order = order,
            Matches = scenario == "ambiguous"
                ? [1, 2]
                : [1]
        };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(order, store.Order);

        store.Order = Encode(1, 2);
        store.Matches = [1];

        Assert.True(placement.TryPlace());
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbandonsAChangedOrRemovedSnapshot(bool removed)
    {
        var store = new FakeStore();
        var placement = new TrayIconPlacement(store, Executable);

        store.BeforeRead = count =>
        {
            if (count == 2)
            {
                store.Order = removed
                    ? null
                    : Encode(3, 1, 2);
            }
        };

        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.True(placement.TryPlace());
        Assert.Equal(2, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("read")]
    [InlineData("match")]
    [InlineData("write")]
    public void FailuresNeverEscapeOrRetryInTheSameSession(string operation)
    {
        var store = new FakeStore { FailingOperation = operation };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.True(placement.TryPlace());

        store.FailingOperation = null;
        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewLaunchDoesNotOverrideUserOrderAfterSuccessOrFailure(bool failed)
    {
        var store = new FakeStore
        {
            FailingOperation = failed
                ? "write"
                : null
        };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.True(placement.TryPlace());

        var writes = store.Writes;

        store.Order = Encode(1, 3, 2);
        store.FailingOperation = null;
        placement = new TrayIconPlacement(store, Executable);
        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(writes, store.Writes);
        Assert.Equal(Encode(1, 3, 2), store.Order);
    }

    [Fact]
    public void AnUnpersistedMarkerPreventsAllOrderAccess()
    {
        var store = new FakeStore { ClaimAllowed = false };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void StoppingRegistrationPreventsLaterAttemptsIncludingExplorerRecreation()
    {
        var store = new FakeStore { Order = null };
        var placement = new TrayIconPlacement(store, Executable);

        placement.Start();

        Assert.False(placement.TryPlace());

        placement.Stop();
        store.Order = Encode(1, 2);
        placement.Start();

        Assert.True(placement.TryPlace());
        Assert.Equal(0, store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibilityRefreshPrecedesPlacementAndContinuesDespitePlacementFailure(bool failed)
    {
        fixture.Run(() =>
        {
            var store = new FakeStore
            {
                FailingOperation = failed
                    ? "write"
                    : null
            };
            var placement = new TrayIconPlacement(store, Executable);
            var refreshed = 0;

            store.BeforeRead = _ => Assert.Equal(1, refreshed);

            using var promoter = new TrayIconVisibilityPromoter(
                Dispatcher.CurrentDispatcher,
                () => refreshed++,
                placement,
                (out bool changed) =>
                {
                    changed = true;

                    return true;
                }
            );

            promoter.Start();

            Assert.True(promoter.Advance());
            Assert.Equal(1, refreshed);
            Assert.Equal(failed
                ? 0
                : 1, store.Writes);
        });
    }

    [Fact]
    public void RegistrationRetriesAreBoundedWithoutRepeatingVisibilityRefresh()
    {
        fixture.Run(() =>
        {
            var store = new FakeStore { Order = null };
            var placement = new TrayIconPlacement(store, Executable);
            var refreshes = 0;
            using var promoter = new TrayIconVisibilityPromoter(
                Dispatcher.CurrentDispatcher,
                () => refreshes++,
                placement,
                (out bool changed) =>
                {
                    changed = true;

                    return true;
                }
            );

            promoter.Start();

            for (var tick = 1; tick < 20; tick++)
            {
                Assert.False(promoter.Advance());
            }

            Assert.True(promoter.Advance());
            Assert.Equal(1, refreshes);
            Assert.Equal(20, store.Reads);

            store.Order = Encode(1, 2);
            promoter.Start();

            Assert.True(promoter.Advance());
            Assert.Equal(0, store.Writes);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistryAdapterUsesOnlyItsSuppliedCurrentUserKeysAndPersistsTheAttempt(bool wrongType)
    {
        var testPath = $@"Software\Voltura\Earner\Tests\TrayPlacement\{Guid.NewGuid():N}";

        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(testPath + "\\Order");
            using var match = root.CreateSubKey("1");
            using var other = root.CreateSubKey("2");

            match.SetValue("ExecutablePath", Path.Combine(Path.GetDirectoryName(Executable)!, ".", Path.GetFileName(Executable)));
            match.SetValue("IsPromoted", 1);
            other.SetValue("ExecutablePath", Path.Combine(Path.GetTempPath(), "Other.exe"));

            if (wrongType)
            {
                root.SetValue("UIOrderList", "invalid", RegistryValueKind.String);
            }
            else
            {
                root.SetValue("UIOrderList", Encode(1, 2), RegistryValueKind.Binary);
            }

            var store = new RegistryTrayIconPlacementStore(testPath + "\\Order", testPath + "\\Marker");
            var placement = new TrayIconPlacement(store, Executable);

            placement.Start();

            Assert.True(placement.TryPlace());

            using var marker = Registry.CurrentUser.OpenSubKey(testPath + "\\Marker");

            Assert.Equal(1, marker!.GetValue("TrayOrderPlacementAttempted"));
            Assert.Equal(1, match.GetValue("IsPromoted"));
            Assert.Null(other.GetValue("IsPromoted"));

            if (wrongType)
            {
                Assert.Equal("invalid", root.GetValue("UIOrderList"));
            }
            else
            {
                Assert.Equal(Encode(2, 1), Assert.IsType<byte[]>(root.GetValue("UIOrderList")));
            }

            root.SetValue("UIOrderList", Encode(1, 2), RegistryValueKind.Binary);
            placement = new TrayIconPlacement(store, Executable);
            placement.Start();

            Assert.True(placement.TryPlace());
            Assert.Equal(Encode(1, 2), Assert.IsType<byte[]>(root.GetValue("UIOrderList")));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testPath, throwOnMissingSubKey: false);
        }
    }

    private static byte[] Encode(params ulong[] identifiers)
    {
        var bytes = new byte[identifiers.Length * 8];

        for (var index = 0; index < identifiers.Length; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(index * 8, 8), identifiers[index]);
        }

        return bytes;
    }

    private sealed class FakeStore : ITrayIconPlacementStore
    {
        public byte[]? Order { get; set; } = Encode(1, 2, 3);
        public HashSet<ulong> Matches { get; set; } = [1];
        public bool ClaimAllowed { get; init; } = true;
        public bool Claimed { get; private set; }
        public int Claims { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string? FailingOperation { get; set; }
        public Action<int>? BeforeRead { get; set; }

        public bool TryClaimAttempt()
        {
            Claims++;

            if (FailingOperation == "claim")
            {
                throw new UnauthorizedAccessException();
            }

            if (Claimed || !ClaimAllowed)
            {
                return false;
            }

            Claimed = true;

            return true;
        }

        public byte[]? ReadOrder()
        {
            Reads++;
            BeforeRead?.Invoke(Reads);

            if (FailingOperation == "read")
            {
                throw new SecurityException();
            }

            return Order is null
                ? null
                : (byte[])Order.Clone();
        }

        public bool MatchesExecutable(ulong identifier, string executablePath)
        {
            Assert.True(Claimed);
            Assert.Equal(Executable, executablePath);

            if (FailingOperation == "match")
            {
                throw new InvalidOperationException();
            }

            return Matches.Contains(identifier);
        }

        public void WriteOrder(byte[] order)
        {
            if (FailingOperation == "write")
            {
                throw new IOException();
            }

            Writes++;
            Order = order;
        }
    }
}
