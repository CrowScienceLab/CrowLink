using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using CrowLink.Models;
using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Settings;

namespace CrowLink.Services.Discovery;

public sealed class DeviceDiscoveryService : IDeviceDiscoveryService
{
    private static readonly TimeSpan AnnouncementInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(8);
    private readonly AppSettings _settings;
    private readonly LogService _log;
    private readonly ConcurrentDictionary<Guid, DeviceInfo> _devices = new();
    private CancellationTokenSource? _lifetimeCts;
    private UdpClient? _listener;
    private Task? _runTask;

    public DeviceDiscoveryService(AppSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    public event EventHandler<DeviceInfo>? DeviceDiscovered;
    public event EventHandler<Guid>? DeviceExpired;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null)
        {
            return Task.CompletedTask;
        }

        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runTask = RunAsync(_lifetimeCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_lifetimeCts is null)
        {
            return;
        }

        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        _listener?.Dispose();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runTask = null;
        _lifetimeCts.Dispose();
        _lifetimeCts = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _listener = new UdpClient(AddressFamily.InterNetwork);
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _settings.DiscoveryPort));
            _listener.EnableBroadcast = true;
            await _log.InfoAsync($"Discovery started on UDP {_settings.DiscoveryPort}").ConfigureAwait(false);

            var sendTask = BroadcastLoopAsync(cancellationToken);
            var receiveTask = ReceiveLoopAsync(cancellationToken);
            var pruneTask = PruneLoopAsync(cancellationToken);
            await Task.WhenAll(sendTask, receiveTask, pruneTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _log.ErrorAsync("Discovery stopped unexpectedly", exception).ConfigureAwait(false);
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        using var sender = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _settings.DiscoveryPort);
        var announcement = new DeviceInfoMessage("CrowLink", ProtocolSerializer.ProtocolVersion, _settings.DeviceId, _settings.DeviceName, _settings.TcpPort);
        var payload = JsonSerializer.SerializeToUtf8Bytes(announcement);

        using var timer = new PeriodicTimer(AnnouncementInterval);
        do
        {
            await sender.SendAsync(payload, endpoint, cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try
            {
                packet = await _listener!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                await _log.WarningAsync($"Invalid discovery datagram: {exception.SocketErrorCode}").ConfigureAwait(false);
                continue;
            }

            DeviceInfoMessage? announcement;
            try
            {
                announcement = JsonSerializer.Deserialize<DeviceInfoMessage>(packet.Buffer);
            }
            catch (JsonException)
            {
                continue;
            }

            if (announcement is null || announcement.App != "CrowLink" ||
                announcement.ProtocolVersion != ProtocolSerializer.ProtocolVersion ||
                announcement.DeviceId == _settings.DeviceId ||
                string.IsNullOrWhiteSpace(announcement.DeviceName) ||
                announcement.TcpPort is <= 0 or > 65535)
            {
                continue;
            }

            var isNew = false;
            var device = _devices.AddOrUpdate(
                announcement.DeviceId,
                _ =>
                {
                    isNew = true;
                    return new DeviceInfo(announcement.DeviceId, announcement.DeviceName, packet.RemoteEndPoint.Address, announcement.TcpPort, DateTimeOffset.UtcNow);
                },
                (_, existing) =>
                {
                    existing.UpdateFrom(announcement.DeviceName, packet.RemoteEndPoint.Address, announcement.TcpPort, DateTimeOffset.UtcNow);
                    return existing;
                });

            if (isNew)
            {
                await _log.InfoAsync($"Device discovered: {device.Name} ({device.Id})").ConfigureAwait(false);
            }

            DeviceDiscovered?.Invoke(this, device);
        }
    }

    private async Task PruneLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            var threshold = DateTimeOffset.UtcNow - DeviceTimeout;
            foreach (var entry in _devices)
            {
                if (entry.Value.LastSeen < threshold && _devices.TryRemove(entry.Key, out _))
                {
                    entry.Value.State = ConnectionState.Offline;
                    DeviceExpired?.Invoke(this, entry.Key);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _listener?.Dispose();
    }
}
