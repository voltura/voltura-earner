using Microsoft.Win32;

namespace VolturaEarner.Platform;

public static class StartupRegistration
{
    private const string Name = "Voltura Earner";
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string Command => $"\"{Environment.ProcessPath}\" --autostart";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);

        if (enabled)
        {
            key.SetValue(Name, Command);
        }
        else if (
            string.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase)
        )
        {
            key.DeleteValue(Name, false);
        }
    }
}
