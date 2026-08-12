namespace CrowLink.Protocol;

public sealed record DeviceInfoMessage(
    string App,
    int ProtocolVersion,
    Guid DeviceId,
    string DeviceName,
    int TcpPort);

public sealed record HelloMessage(
    int ProtocolVersion,
    Guid DeviceId,
    string DeviceName);
