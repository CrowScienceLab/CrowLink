namespace CrowLink.Protocol;

public sealed record PairRequestMessage(Guid DeviceId, string DeviceName);

public sealed record PairResponseMessage(Guid DeviceId, string DeviceName);
