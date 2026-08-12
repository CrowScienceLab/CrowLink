using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using CrowLink.Models;
using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Security;
using CrowLink.Services.Settings;

namespace CrowLink.Services.Network;

public sealed class ConnectionService : IAsyncDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);
    private readonly AppSettings _settings;
    private readonly PairingService _pairing;
    private readonly LogService _log;
    private readonly TcpClientService _clientService = new();
    private readonly TcpServerService _serverService;
    private readonly ConcurrentDictionary<Guid, PeerConnection> _connections = new();
    private readonly ConcurrentDictionary<Guid, Task> _receiveTasks = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    public ConnectionService(AppSettings settings, PairingService pairing, LogService log)
    {
        _settings = settings;
        _pairing = pairing;
        _log = log;
        _serverService = new TcpServerService(settings.TcpPort, log);
    }

    public event EventHandler<PeerConnection>? DeviceConnected;
    public event EventHandler<DeviceInfo>? DeviceDisconnected;
    public event Func<PeerMessageEventArgs, Task>? MessageReceived;

    public IReadOnlyCollection<PeerConnection> Connections => _connections.Values.ToArray();

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _serverService.StartAsync(HandleIncomingAsync, cancellationToken);

    public bool TryGetConnection(Guid deviceId, out PeerConnection? connection) =>
        _connections.TryGetValue(deviceId, out connection);

    public async Task<bool> DisconnectAsync(Guid deviceId)
    {
        if (!_connections.TryRemove(deviceId, out var connection))
        {
            return false;
        }

        connection.Device.State = ConnectionState.Available;
        DeviceDisconnected?.Invoke(this, connection.Device);
        await _log.InfoAsync($"Disconnected by user: {connection.Device.Name} ({connection.Device.Id})").ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<PeerConnection> ConnectAsync(DeviceInfo device, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(device.Id, out var existing))
        {
            return existing;
        }

        device.State = ConnectionState.Connecting;
        TcpClient? client = null;
        ProtocolSerializer? protocol = null;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        timeoutCts.CancelAfter(HandshakeTimeout);

        try
        {
            client = await _clientService.ConnectAsync(device.Address, device.TcpPort, timeoutCts.Token).ConfigureAwait(false);
            protocol = new ProtocolSerializer(client.GetStream());
            await protocol.WriteJsonAsync(MessageType.Hello, CreateHello(), timeoutCts.Token).ConfigureAwait(false);

            var helloFrame = await protocol.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            EnsureType(helloFrame, MessageType.Hello);
            var hello = ProtocolSerializer.Deserialize<HelloMessage>(helloFrame);
            ValidateHello(hello, device.Id);

            await protocol.WriteJsonAsync(
                MessageType.PairRequest,
                new PairRequestMessage(_settings.DeviceId, _settings.DeviceName),
                timeoutCts.Token).ConfigureAwait(false);

            var response = await protocol.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            if (response.Type == MessageType.PairReject)
            {
                device.State = ConnectionState.Rejected;
                throw new UnauthorizedAccessException($"{device.Name}에서 연결 요청을 거부했습니다.");
            }

            EnsureType(response, MessageType.PairAccept);
            var pairResponse = ProtocolSerializer.Deserialize<PairResponseMessage>(response);
            if (pairResponse.DeviceId != hello.DeviceId)
            {
                throw new InvalidDataException("Pair response identity does not match HELLO.");
            }

            await _pairing.TrustAsync(hello.DeviceId).ConfigureAwait(false);

            var connection = new PeerConnection(device, client, protocol);
            client = null;
            protocol = null;
            return Register(connection);
        }
        catch
        {
            device.State = device.State == ConnectionState.Rejected ? ConnectionState.Rejected : ConnectionState.Error;
            if (protocol is not null)
            {
                await protocol.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                client?.Dispose();
            }

            throw;
        }
    }

    private async Task HandleIncomingAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var protocol = new ProtocolSerializer(client.GetStream());
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        timeoutCts.CancelAfter(HandshakeTimeout);
        var ownsConnection = true;

        try
        {
            var helloFrame = await protocol.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            EnsureType(helloFrame, MessageType.Hello);
            var hello = ProtocolSerializer.Deserialize<HelloMessage>(helloFrame);
            ValidateHello(hello);
            await protocol.WriteJsonAsync(MessageType.Hello, CreateHello(), timeoutCts.Token).ConfigureAwait(false);

            var pairFrame = await protocol.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            EnsureType(pairFrame, MessageType.PairRequest);
            var pairRequest = ProtocolSerializer.Deserialize<PairRequestMessage>(pairFrame);
            if (pairRequest.DeviceId != hello.DeviceId || !string.Equals(pairRequest.DeviceName, hello.DeviceName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Pair request identity does not match HELLO.");
            }

            var endpoint = (IPEndPoint?)client.Client.RemoteEndPoint;
            var approved = await _pairing.RequestApprovalAsync(
                new PairingRequest(hello.DeviceId, hello.DeviceName, endpoint?.Address.ToString() ?? "unknown")).ConfigureAwait(false);

            if (!approved)
            {
                await protocol.WriteJsonAsync(
                    MessageType.PairReject,
                    new PairResponseMessage(_settings.DeviceId, _settings.DeviceName),
                    timeoutCts.Token).ConfigureAwait(false);
                return;
            }

            await protocol.WriteJsonAsync(
                MessageType.PairAccept,
                new PairResponseMessage(_settings.DeviceId, _settings.DeviceName),
                timeoutCts.Token).ConfigureAwait(false);

            var device = new DeviceInfo(
                hello.DeviceId,
                hello.DeviceName,
                endpoint?.Address ?? IPAddress.None,
                0,
                DateTimeOffset.UtcNow);
            var connection = new PeerConnection(device, client, protocol);
            ownsConnection = false;
            Register(connection);
        }
        finally
        {
            if (ownsConnection)
            {
                await protocol.DisposeAsync().ConfigureAwait(false);
                client.Dispose();
            }
        }
    }

    private PeerConnection Register(PeerConnection connection)
    {
        if (!_connections.TryAdd(connection.Device.Id, connection))
        {
            connection.Device.State = ConnectionState.Connected;
            _ = connection.DisposeAsync();
            return _connections[connection.Device.Id];
        }

        connection.Device.State = ConnectionState.Connected;
        _ = _log.InfoAsync($"TCP connected: {connection.Device.Name} ({connection.Device.Id})");
        DeviceConnected?.Invoke(this, connection);
        _receiveTasks[connection.Device.Id] = ReceiveLoopAsync(connection, _lifetimeCts.Token);
        return connection;
    }

    private async Task ReceiveLoopAsync(PeerConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await connection.Protocol.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (message.Type == MessageType.Ping)
                {
                    await connection.SendAsync(MessageType.Pong, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var handlers = MessageReceived?.GetInvocationList().Cast<Func<PeerMessageEventArgs, Task>>().ToArray();
                if (handlers is null)
                {
                    continue;
                }

                var args = new PeerMessageEventArgs(connection, message);
                foreach (var handler in handlers)
                {
                    await handler(args).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (EndOfStreamException)
        {
            await _log.WarningAsync($"Connection lost: {connection.Device.Name}").ConfigureAwait(false);
        }
        catch (IOException) when (!_connections.ContainsKey(connection.Device.Id))
        {
            // A local user disconnect disposes the stream to release the pending read.
        }
        catch (IOException exception)
        {
            await _log.ErrorAsync($"Connection lost: {connection.Device.Name}", exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _log.ErrorAsync($"Protocol error from {connection.Device.Name}", exception).ConfigureAwait(false);
        }
        finally
        {
            if (_connections.TryRemove(connection.Device.Id, out _))
            {
                connection.Device.State = ConnectionState.Offline;
                DeviceDisconnected?.Invoke(this, connection.Device);
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            _receiveTasks.TryRemove(connection.Device.Id, out _);
        }
    }

    private HelloMessage CreateHello() => new(ProtocolSerializer.ProtocolVersion, _settings.DeviceId, _settings.DeviceName);

    private static void EnsureType(ProtocolMessage message, MessageType expected)
    {
        if (message.Type != expected)
        {
            throw new InvalidDataException($"Expected {expected}, received {message.Type}.");
        }
    }

    private static void ValidateHello(HelloMessage hello, Guid? expectedDeviceId = null)
    {
        if (hello.ProtocolVersion != ProtocolSerializer.ProtocolVersion || hello.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(hello.DeviceName) || (expectedDeviceId.HasValue && hello.DeviceId != expectedDeviceId.Value))
        {
            throw new InvalidDataException("Invalid or incompatible HELLO message.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        await _serverService.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_receiveTasks.Values).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _connections.Clear();
        _lifetimeCts.Dispose();
    }
}
