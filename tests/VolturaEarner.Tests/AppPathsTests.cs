using VolturaEarner.Platform;
using Xunit;

namespace VolturaEarner.Tests;

public sealed class AppPathsTests
{
    [Theory]
    [InlineData("--measure-idle")]
    [InlineData("--render-review")]
    [InlineData("--isolated-test-mode")]
    public void MissingPathNamesTheOptionInsteadOfReadingPastArguments(string option)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            AppPaths.ReadPathArgument([option], option)
        );

        Assert.Equal($"A path is required after {option}.", error.Message);
    }

    [Theory]
    [InlineData("--measure-idle", "--autostart")]
    [InlineData("--render-review", "--trace-dpi")]
    [InlineData("--isolated-test-mode", "--render-review")]
    [InlineData("--measure-idle", "")]
    [InlineData("--render-review", " ")]
    public void EmptyPathsAndFollowingOptionsAreNotTreatedAsDirectories(string option, string value)
    {
        Assert.Throws<ArgumentException>(() =>
            AppPaths.ReadPathArgument([option, value], option)
        );
    }

    [Fact]
    public void ValidPathsResolveWithoutCreatingFilesAndAbsentOptionsRemainOptional()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "VolturaEarner-tests",
            Guid.NewGuid().ToString("N")
        );
        string[] args = ["--isolated-test-mode", root, "--render-review", "review images"];

        Assert.Equal(new AppPaths(root, false, true), AppPaths.Resolve(args));
        Assert.Equal(
            Path.GetFullPath("review images"),
            AppPaths.ReadPathArgument(args, "--render-review")
        );
        Assert.Null(AppPaths.ReadPathArgument(args, "--measure-idle"));
        Assert.False(Directory.Exists(root));
    }
}
