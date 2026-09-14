namespace VolturaEarner.Features.Updates;

internal enum UpdateStatus
{
    Idle,
    ManualUpdates,
    Checking,
    Downloading,
    Ready,
    Current,
    Installing,
    UpdateCheckFailed,
    UpdateDownloadFailed,
    UpdateVerificationFailed,
    UpdateInstallFailed,
}

// Publish the message and available action together, never as independent mutable fields.
internal sealed record UpdateState(UpdateStatus Status, string? Installer = null)
{
    public bool Ready => Status == UpdateStatus.Ready && Installer is not null;
    public bool Busy =>
        Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Installing;
}
