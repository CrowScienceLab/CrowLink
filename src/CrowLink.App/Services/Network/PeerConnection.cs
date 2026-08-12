using System.Net.Sockets;
using CrowLink.Models;
using CrowLink.Protocol;

namespace CrowLink.Services.Network;

public sealed class PeerConnection : IAsyncDisposable
{
    private readonly TcpClient _client;

    public PeerConnection(DeviceInfo device, TcpClient client, ProtocolSerializer protocol)
    {
        Device = device;
        _client = client;
        Protocol = protocol;
    }

    public DeviceInfo Device { get; }
    public ProtocolSerializer Protocol { get; }
    public bool IsConnected => _client.Connected;

    public Task SendJsonAsync<T>(MessageType type, T payload, CancellationToken cancellationToken) =>
        Protocol.WriteJsonAsync(type, payload, cancellationToken);

    public Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken) =>
        Protocol.WriteAsync(type, payload, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Protocol.DisposeAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
