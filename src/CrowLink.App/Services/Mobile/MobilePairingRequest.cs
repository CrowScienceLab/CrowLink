using System.Net;
using CrowLink.Models;

namespace CrowLink.Services.Mobile;

public sealed record MobilePairingRequest(
    string DeviceName,
    IPAddress Address,
    DeviceType DeviceType = DeviceType.MobileBrowser);

public sealed record MobileSessionSnapshot(
    Guid SessionId,
    string DeviceName,
    IPAddress Address,
    DeviceType DeviceType,
    DateTimeOffset ConnectedAt);
