namespace VolturaEarner.Platform;

public sealed record AppPaths(string Data, bool Portable, bool Isolated)
{
    public static AppPaths Resolve(string[] args)
    {
        var testPath = ReadPathArgument(args, "--isolated-test-mode");

        if (testPath is not null)
        {
            return new(testPath, false, true);
        }

        var portable = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.marker"));

        return new(
            portable
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltura", "Earner"),
            portable,
            false
        );
    }

    internal static string? ReadPathArgument(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);

        if (index < 0)
        {
            return null;
        }

        if (
            index + 1 >= args.Length
            || string.IsNullOrWhiteSpace(args[index + 1])
            || args[index + 1].StartsWith("--", StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(VolturaEarner.Ui.Strings.Current.Format("PathRequired", option));
        }

        return Path.GetFullPath(args[index + 1]);
    }
}
