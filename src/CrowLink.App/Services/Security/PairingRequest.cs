namespace CrowLink.Services.Security;

public sealed record PairingRequest(Guid DeviceId, string DeviceName, string Address);
