using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Win32;

namespace VolturaEarner.Platform;

// Explorer owns this undocumented order. Save a preference once and let Explorer
// consume or discard it naturally; never refresh the shell to enforce placement.
internal sealed class TrayIconPlacement(ITrayIconPlacementStore store, string? executablePath)
{
    private bool _started;
    private bool _finished;

    internal void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _finished = true;

        try
        {
            _finished = string.IsNullOrEmpty(executablePath) || !store.TryClaimAttempt();
        }
        catch (Exception)
        {
            // Optional shell customization must never affect application startup.
        }
    }

    // False only while waiting for Explorer to register the icon in its order.
    internal bool TryPlace()
    {
        if (!_started || _finished)
        {
            return true;
        }

        try
        {
            var original = store.ReadOrder();

            if (original is null)
            {
                return false;
            }

            _finished = true;

            if (original.Length == 0 || original.Length > 64 * 1024 || original.Length % 8 != 0)
            {
                return true;
            }

            var identifiers = new HashSet<ulong>();
            var matchingOffset = -1;

            for (var offset = 0; offset < original.Length; offset += 8)
            {
                var identifier = BinaryPrimitives.ReadUInt64LittleEndian(original.AsSpan(offset, 8));

                if (!identifiers.Add(identifier))
                {
                    return true;
                }

                if (store.MatchesExecutable(identifier, executablePath!))
                {
                    if (matchingOffset >= 0)
                    {
                        return true;
                    }

                    matchingOffset = offset;
                }
            }

            if (matchingOffset < 0)
            {
                _finished = false;

                return false;
            }

            if (matchingOffset == original.Length - 8)
            {
                return true;
            }

            var reordered = (byte[])original.Clone();

            original.AsSpan(matchingOffset + 8).CopyTo(reordered.AsSpan(matchingOffset));
            original.AsSpan(matchingOffset, 8).CopyTo(reordered.AsSpan(reordered.Length - 8));

            // A changed snapshot belongs to Explorer/user activity. Do not retry.
            // The registry has no compare-and-swap; this minimizes, not eliminates,
            // the interval in which Explorer could make another change.

            if (store.ReadOrder() is { } current && original.AsSpan().SequenceEqual(current))
            {
                store.WriteOrder(reordered);
            }
        }
        catch (Exception)
        {
            _finished = true;
        }

        return true;
    }

    internal void Stop() => _finished = true;
}

internal interface ITrayIconPlacementStore
{
    bool TryClaimAttempt();
    byte[]? ReadOrder();
    bool MatchesExecutable(ulong identifier, string executablePath);
    void WriteOrder(byte[] order);
}

internal sealed class RegistryTrayIconPlacementStore(
    string orderKeyPath = @"Control Panel\NotifyIconSettings",
    string markerKeyPath = @"Software\Voltura\Earner"
) : ITrayIconPlacementStore
{
    private const string AttemptMarker = "TrayOrderPlacementAttempted";

    public bool TryClaimAttempt()
    {
        using var key = Registry.CurrentUser.CreateSubKey(markerKeyPath, writable: true);

        if (key.GetValueNames().Contains(AttemptMarker, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        key.SetValue(AttemptMarker, 1, RegistryValueKind.DWord);

        return Equals(key.GetValue(AttemptMarker), 1);
    }

    public byte[]? ReadOrder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(orderKeyPath);
        var value = key?.GetValue("UIOrderList");

        // Missing data may still be registering; a wrong value type is terminal.

        return value is null
            ? null
            : value as byte[] ?? [];
    }

    public bool MatchesExecutable(ulong identifier, string executablePath)
    {
        using var entry = Registry.CurrentUser.OpenSubKey(
            orderKeyPath + "\\" + identifier.ToString(CultureInfo.InvariantCulture)
        );

        return TrayIconVisibilityPromoter.PathsEqual(executablePath, entry?.GetValue("ExecutablePath") as string);
    }

    public void WriteOrder(byte[] order)
    {
        using var key = Registry.CurrentUser.OpenSubKey(orderKeyPath, writable: true);

        key?.SetValue("UIOrderList", order, RegistryValueKind.Binary);
    }
}
