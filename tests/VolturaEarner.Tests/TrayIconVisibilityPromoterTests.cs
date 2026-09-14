using Microsoft.Win32;
using VolturaEarner.Platform;
using Xunit;

namespace VolturaEarner.Tests;

public sealed class TrayIconVisibilityPromoterTests
{
    [Fact]
    public void PromotesTheCurrentExecutableAfterExplorerRegistersIt()
    {
        var testKeyPath =
            $@"Software\Voltura\Earner\Tests\NotifyIconSettings\{Guid.NewGuid():N}";
        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "VolturaEarner",
            "VolturaEarner.exe"
        );

        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(testKeyPath, writable: true);

            using (var unrelatedEntry = root.CreateSubKey("unrelated", writable: true))
            {
                unrelatedEntry.SetValue(
                    "ExecutablePath",
                    Path.Combine(Path.GetTempPath(), "OtherApp.exe")
                );
            }

            Assert.False(
                TrayIconVisibilityPromoter.TryPromoteEntries(
                    root,
                    executablePath,
                    out var changedBeforeRegistration
                )
            );
            Assert.False(changedBeforeRegistration);

            using (var matchingEntry = root.CreateSubKey("matching", writable: true))
            {
                matchingEntry.SetValue(
                    "ExecutablePath",
                    Path.Combine(
                        Path.GetDirectoryName(executablePath)!,
                        ".",
                        "VolturaEarner.exe"
                    )
                );
            }

            Assert.True(
                TrayIconVisibilityPromoter.TryPromoteEntries(root, executablePath, out var changed)
            );
            Assert.True(changed);

            using var promotedEntry = root.OpenSubKey("matching", writable: false);
            using var unchangedEntry = root.OpenSubKey("unrelated", writable: false);

            Assert.Equal(1, promotedEntry!.GetValue("IsPromoted"));
            Assert.Null(unchangedEntry!.GetValue("IsPromoted"));

            Assert.True(
                TrayIconVisibilityPromoter.TryPromoteEntries(
                    root,
                    executablePath,
                    out var changedAgain
                )
            );
            Assert.False(changedAgain);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(testKeyPath, throwOnMissingSubKey: false);
        }
    }
}
