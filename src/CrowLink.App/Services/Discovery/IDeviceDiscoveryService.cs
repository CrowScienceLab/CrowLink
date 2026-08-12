using CrowLink.Models;

namespace CrowLink.Services.Discovery;

public interface IDeviceDiscoveryService : IAsyncDisposable
{
    event EventHandler<DeviceInfo>? DeviceDiscovered;
    event EventHandler<Guid>? DeviceExpired;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
