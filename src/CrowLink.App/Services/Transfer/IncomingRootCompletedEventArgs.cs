namespace CrowLink.Services.Transfer;

public sealed class IncomingRootCompletedEventArgs(
    Guid deviceId,
    Guid packageId,
    string rootPath) : EventArgs
{
    public Guid DeviceId { get; } = deviceId;
    public Guid PackageId { get; } = packageId;
    public string RootPath { get; } = rootPath;
}
