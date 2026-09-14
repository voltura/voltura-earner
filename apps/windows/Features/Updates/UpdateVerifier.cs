using VolturaEarner.Ui;
using System.Security.Cryptography;
using System.Text.Json;

namespace VolturaEarner.Features.Updates;

public sealed record UpdateAsset(string Name, long Size, string Sha256);

public sealed record VerifiedUpdate(string Version, UpdateAsset Asset);

public static class UpdateVerifier
{
    public const long MaximumInstallerBytes = 250 * 1024 * 1024;

    public static VerifiedUpdate Verify(
        byte[] manifest,
        byte[] signature,
        string publicKey,
        bool full
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Length > 64 * 1024)
        {
            throw new InvalidDataException(Strings.Current["UpdateManifestTooLarge"]);
        }

        using var rsa = RSA.Create();

        rsa.ImportFromPem(publicKey);

        if (!rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        {
            throw new InvalidDataException(Strings.Current["InvalidSignature"]);
        }

        using var json = JsonDocument.Parse(manifest);
        var root = json.RootElement;

        if (root.GetProperty("schema").GetInt32() != 1)
        {
            throw new InvalidDataException(Strings.Current["UnsupportedManifest"]);
        }

        var versionText = root.GetProperty("version").GetString() ?? string.Empty;

        if (!TryVersion(versionText, out _))
        {
            throw new InvalidDataException(Strings.Current["InvalidUpdateVersion"]);
        }

        var expected = $"VolturaEarner-Setup-{versionText}-win-x64{(full
            ? "-full"
            : "")}.exe";
        var matches = root.GetProperty("assets")
            .EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == expected)
            .ToArray();

        if (matches.Length != 1)
        {
            throw new InvalidDataException(Strings.Current["InstallerMissingOrDuplicated"]);
        }

        var entry = matches[0];
        var size = entry.GetProperty("size").GetInt64();
        var hash = entry.GetProperty("sha256").GetString() ?? string.Empty;

        if (
            size <= 0
            || size > MaximumInstallerBytes
            || hash.Length != 64
            || !hash.All(Uri.IsHexDigit)
        )
        {
            throw new InvalidDataException(Strings.Current["InvalidAssetMetadata"]);
        }

        return new(versionText, new(expected, size, hash));
    }

    public static bool TryVersion(string text, out Version version)
    {
        version = new();

        var parts = text.Split('.');

        return parts.Length == 3
            && parts.All(part =>
                part.Length > 0
                && (part.Length == 1 || part[0] != '0')
                && part.All(char.IsAsciiDigit)
            )
            && Version.TryParse(text, out version!);
    }

    public static async Task VerifyFileAsync(
        string path,
        UpdateAsset asset,
        CancellationToken token
    )
    {
        await using var stream = File.OpenRead(path);

        if (stream.Length != asset.Size)
        {
            throw new InvalidDataException(Strings.Current["IncorrectInstallerSize"]);
        }

        var hash = await SHA256.HashDataAsync(stream, token);

        if (!Convert.ToHexString(hash).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Strings.Current["IncorrectInstallerHash"]);
        }
    }
}
