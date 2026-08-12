namespace CrowLink.Services.Explorer;

public sealed record ExplorerPackageSnapshot(
    Guid PackageId,
    Guid DeviceId,
    string DeviceName,
    bool IsIncoming,
    string Summary,
    string StatusText,
    IReadOnlyList<string> LocalPaths,
    bool CanDragToExplorer);

public sealed class ExplorerPackageChangedEventArgs(ExplorerPackageSnapshot package) : EventArgs
{
    public ExplorerPackageSnapshot Package { get; } = package;
}
