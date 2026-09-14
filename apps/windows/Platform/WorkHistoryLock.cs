namespace VolturaEarner.Platform;

internal static class WorkHistoryLock
{
    internal static FileStream Acquire(string workPath, bool requireDirectory = false)
    {
        var directory = Path.GetDirectoryName(workPath)!;

        if (!requireDirectory)
        {
            Directory.CreateDirectory(directory);
        }

        // Keep this file in place: deleting it after release could remove a new owner's lock.
        // File sharing also protects paths that reach the same folder through an alias.

        return new FileStream(Path.Combine(directory, ".earner-work.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}
