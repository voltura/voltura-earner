using System.Windows;
using VolturaEarner.Platform;
using Xunit;
using WpfSize = System.Windows.Size;

namespace VolturaEarner.Tests;

public sealed class WindowWorkAreaPlacementTests
{
    [Theory]
    [InlineData(96u, 216u, true)]
    [InlineData(216u, 96u, true)]
    [InlineData(216u, 216u, false)]
    [InlineData(0u, 216u, false)]
    [InlineData(216u, 0u, false)]
    public void OnlyKnownMismatchedDpiRequiresRecovery(
        uint windowDpi,
        uint monitorDpi,
        bool expected
    ) => Assert.Equal(expected, WindowWorkAreaPlacement.NeedsDpiRecovery(windowDpi, monitorDpi));

    [Fact]
    public void OversizedWindowIsConstrainedToWorkArea()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(0, 0, 800, 600),
            new WpfSize(1160, 760)
        );

        Assert.Equal(new Rect(0, 0, 800, 600), bounds);
    }

    [Fact]
    public void SmallerWindowRetainsItsRequestedSize()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(0, 0, 1600, 900),
            new WpfSize(1160, 760)
        );

        Assert.Equal(new WpfSize(1160, 760), bounds.Size);
    }

    [Fact]
    public void SmallerWindowIsCenteredWithinWorkArea()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(100, 50, 1600, 900),
            new WpfSize(1160, 760)
        );

        Assert.Equal(new Rect(320, 120, 1160, 760), bounds);
    }

    [Fact]
    public void NegativeWorkAreaOriginIsPreservedWhenCentering()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(-1920, -100, 1920, 1080),
            new WpfSize(1160, 760)
        );

        Assert.Equal(new Rect(-1540, 60, 1160, 760), bounds);
    }

    [Fact]
    public void VisibleWindowPositionIsUnchanged()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(200, 100, 1160, 760),
            new Rect(0, 0, 1920, 1040)
        );

        Assert.Equal(new Point(200, 100), position);
    }

    [Fact]
    public void WindowOutsideRightAndBottomEdgesIsMovedIntoWorkArea()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(1000, 500, 1160, 760),
            new Rect(0, 0, 1920, 1040)
        );

        Assert.Equal(new Point(760, 280), position);
    }

    [Fact]
    public void WindowOutsideNegativeOriginWorkAreaIsMovedIntoWorkArea()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(-2100, -300, 1160, 760),
            new Rect(-1920, -100, 1920, 1040)
        );

        Assert.Equal(new Point(-1920, -100), position);
    }

    [Fact]
    public void WindowLargerThanWorkAreaKeepsItsControlsReachable()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(600, 400, 2610, 1710),
            new Rect(0, 0, 1920, 1040)
        );

        Assert.Equal(new Point(0, 0), position);
    }

    [Fact]
    public void AutomaticallyConstrainedWindowReturnsToPreferredSizeWhenWorkAreaRecovers()
    {
        var size = WindowWorkAreaPlacement.CalculateSizeAfterWorkAreaChange(
            new WpfSize(1160, 760),
            new WpfSize(1920, 1032)
        );

        Assert.Equal(new WpfSize(1160, 760), size);
    }

    [Fact]
    public void AutomaticallyConstrainedWindowTracksAnotherLimitedWorkArea()
    {
        var size = WindowWorkAreaPlacement.CalculateSizeAfterWorkAreaChange(
            new WpfSize(1160, 760),
            new WpfSize(1000, 700)
        );

        Assert.Equal(new WpfSize(1000, 700), size);
    }

    [Fact]
    public void PreferredWindowIsConstrainedWhenWorkAreaShrinks()
    {
        var size = WindowWorkAreaPlacement.CalculateSizeAfterWorkAreaChange(
            new WpfSize(1160, 760),
            new WpfSize(853, 459)
        );

        Assert.Equal(new WpfSize(853, 459), size);
    }

    [Fact]
    public void UserPreferredSizeIsRetainedWhenWorkAreaRecovers()
    {
        var size = WindowWorkAreaPlacement.CalculateSizeAfterWorkAreaChange(
            new WpfSize(700, 420),
            new WpfSize(1920, 1032)
        );

        Assert.Equal(new WpfSize(700, 420), size);
    }
}
