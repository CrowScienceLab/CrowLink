using System.Collections.Concurrent;
using System.Threading.Channels;
using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;

namespace CrowLink.Services.RemoteMouse;

public sealed class RemoteMouseService : IAsyncDisposable
{
    private readonly ConnectionService _connections;
    private readonly LogService _log;
    private readonly MonitorInfoMessage _localMonitor;
    private readonly ConcurrentDictionary<Guid, MonitorInfoMessage> _monitors = new();
    private GlobalMouseTracker? _tracker;
    private PeerConnection? _controlConnection;
    private Guid _sessionId;
    private Guid _receivingPeerId;
    private Channel<RemoteInputEvent>? _inputChannel;
    private Task? _inputPump;
    private readonly Dictionary<ushort, InjectedKey> _injectedKeys = [];
    private string _status = "입력 공유 사용 안 함";

    public RemoteMouseService(ConnectionService connections, LogService log)
    {
        _connections = connections;
        _log = log;
        _localMonitor = MouseInputInjector.GetMonitorInfo();
        _connections.MessageReceived += OnMessageReceivedAsync;
        _connections.DeviceConnected += OnDeviceConnected;
        _connections.DeviceDisconnected += OnDeviceDisconnected;
    }

    public event Func<RemoteMouseControlRequest, Task<bool>>? ControlRequested;
    public event EventHandler? StateChanged;
    public event EventHandler<RemoteMonitorChangedEventArgs>? MonitorChanged;

    public bool IsActive => _sessionId != Guid.Empty || _receivingPeerId != Guid.Empty;
    public bool IsControlling => _tracker is not null;
    public string Status => _status;
    public MonitorInfoMessage LocalMonitor => _localMonitor;

    public bool TryGetRemoteMonitor(Guid deviceId, out MonitorInfoMessage? monitor) =>
        _monitors.TryGetValue(deviceId, out monitor);

    public async Task RequestControlAsync(
        PeerConnection connection,
        MouseTransitionEdge edge,
        CancellationToken cancellationToken = default)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("이미 원격 입력 세션이 진행 중입니다.");
        }

        _controlConnection = connection;
        _sessionId = Guid.NewGuid();
        _pendingEdge = edge;
        SetStatus($"{connection.Device.Name}의 수락을 기다리는 중");
        try
        {
            await connection.SendJsonAsync(
                MessageType.MouseControlRequest,
                new MouseControlRequestMessage(_sessionId, edge.ToString()),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ResetControllerState();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = _sessionId;
        var connection = _controlConnection;
        var receivingPeerId = _receivingPeerId;
        if (sessionId == Guid.Empty && receivingPeerId == Guid.Empty)
        {
            return;
        }

        if (connection is null && receivingPeerId != Guid.Empty)
        {
            _connections.TryGetConnection(receivingPeerId, out connection);
        }

        try
        {
            if (connection is not null)
            {
                await connection.SendJsonAsync(
                    MessageType.MouseControlStop,
                    new MouseControlStopMessage(sessionId),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            await _log.WarningAsync($"Remote mouse stop notice failed: {exception.Message}").ConfigureAwait(false);
        }

        await ResetAsync("입력 공유가 중지되었습니다.").ConfigureAwait(false);
    }

    private async void OnDeviceConnected(object? sender, PeerConnection connection)
    {
        try
        {
            await connection.SendJsonAsync(
                MessageType.MonitorInfo,
                LocalMonitor,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _log.WarningAsync($"Monitor layout send failed: {exception.Message}").ConfigureAwait(false);
        }
    }

    private void OnDeviceDisconnected(object? sender, Models.DeviceInfo device)
    {
        _monitors.TryRemove(device.Id, out _);
        MonitorChanged?.Invoke(this, new RemoteMonitorChangedEventArgs(device.Id, null));
        if (_controlConnection?.Device.Id == device.Id || _receivingPeerId == device.Id)
        {
            _ = ResetAsync("연결이 끊겨 입력 공유를 종료했습니다.");
        }
    }

    private async Task OnMessageReceivedAsync(PeerMessageEventArgs args)
    {
        switch (args.Message.Type)
        {
            case MessageType.MonitorInfo:
                HandleMonitorInfo(args);
                break;
            case MessageType.MouseControlRequest:
                await HandleControlRequestAsync(args).ConfigureAwait(false);
                break;
            case MessageType.MouseControlAccept:
                HandleControlAccepted(args);
                break;
            case MessageType.MouseControlReject:
                HandleControlRejected(args);
                break;
            case MessageType.MouseMove:
                HandleMouseMove(args);
                break;
            case MessageType.MouseButton:
                HandleMouseButton(args);
                break;
            case MessageType.MouseWheel:
                HandleMouseWheel(args);
                break;
            case MessageType.MouseControlStop:
                await HandleControlStoppedAsync(args).ConfigureAwait(false);
                break;
            case MessageType.KeyboardInput:
                HandleKeyboardInput(args);
                break;
            case MessageType.KeyboardReset:
                HandleKeyboardReset(args);
                break;
        }
    }

    private void HandleMonitorInfo(PeerMessageEventArgs args)
    {
        var monitor = ProtocolSerializer.Deserialize<MonitorInfoMessage>(args.Message);
        if (monitor.VirtualWidth <= 0 || monitor.VirtualHeight <= 0 || monitor.MonitorCount is < 1 or > 32 ||
            monitor.Monitors is null || monitor.Monitors.Count != monitor.MonitorCount ||
            monitor.Monitors.Any(item => item.Width <= 0 || item.Height <= 0 || item.DpiX is < 96 or > 960 || item.DpiY is < 96 or > 960))
        {
            throw new InvalidDataException("Invalid monitor layout.");
        }

        _monitors[args.Connection.Device.Id] = monitor;
        MonitorChanged?.Invoke(this, new RemoteMonitorChangedEventArgs(args.Connection.Device.Id, monitor));
    }

    private async Task HandleControlRequestAsync(PeerMessageEventArgs args)
    {
        var request = ProtocolSerializer.Deserialize<MouseControlRequestMessage>(args.Message);
        var edgeValid = Enum.TryParse<MouseTransitionEdge>(request.EntryEdge, true, out var edge);
        var approved = request.SessionId != Guid.Empty && edgeValid && !IsActive;
        var handlers = ControlRequested?.GetInvocationList()
            .Cast<Func<RemoteMouseControlRequest, Task<bool>>>()
            .ToArray();
        if (approved && handlers is { Length: > 0 })
        {
            foreach (var handler in handlers)
            {
                approved &= await handler(new RemoteMouseControlRequest(args.Connection, request.SessionId, edge))
                    .ConfigureAwait(false);
            }
        }
        else
        {
            approved = false;
        }

        if (!approved)
        {
            await args.Connection.SendJsonAsync(
                MessageType.MouseControlReject,
                new MouseControlResponseMessage(request.SessionId),
                CancellationToken.None).ConfigureAwait(false);
            return;
        }

        _receivingPeerId = args.Connection.Device.Id;
        _sessionId = request.SessionId;
        _controlConnection = args.Connection;
        SetStatus($"{args.Connection.Device.Name}에서 키보드·마우스를 공유하는 중");
        await args.Connection.SendJsonAsync(
            MessageType.MouseControlAccept,
            new MouseControlResponseMessage(request.SessionId),
            CancellationToken.None).ConfigureAwait(false);
    }

    private void HandleControlAccepted(PeerMessageEventArgs args)
    {
        var response = ProtocolSerializer.Deserialize<MouseControlResponseMessage>(args.Message);
        if (_controlConnection?.Device.Id != args.Connection.Device.Id || response.SessionId != _sessionId)
        {
            return;
        }

        var requestEdge = MouseTransitionEdge.Right;
        // The entry edge is kept in the request status until acceptance.
        if (_pendingEdge.HasValue)
        {
            requestEdge = _pendingEdge.Value;
        }

        var remoteHeight = _monitors.TryGetValue(args.Connection.Device.Id, out var monitor)
            ? monitor.VirtualHeight
            : 1080;
        try
        {
            StartTracker(requestEdge, remoteHeight);
            SetStatus($"준비됨 · {EntryEdgeText(requestEdge)} 끝으로 마우스를 이동하세요");
        }
        catch (Exception exception)
        {
            _ = _log.ErrorAsync("Global mouse tracker could not start", exception);
            _ = ResetAsync("전역 마우스 추적기를 시작하지 못했습니다.");
        }
    }

    private MouseTransitionEdge? _pendingEdge;

    private void HandleControlRejected(PeerMessageEventArgs args)
    {
        var response = ProtocolSerializer.Deserialize<MouseControlResponseMessage>(args.Message);
        if (_controlConnection?.Device.Id == args.Connection.Device.Id && response.SessionId == _sessionId)
        {
            ResetControllerState();
            SetStatus($"{args.Connection.Device.Name}에서 입력 공유를 거부했습니다.");
        }
    }

    private void StartTracker(MouseTransitionEdge edge, int remoteHeight)
    {
        _inputChannel = Channel.CreateUnbounded<RemoteInputEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _inputPump = PumpInputAsync(_inputChannel.Reader);
        _tracker = new GlobalMouseTracker(edge, remoteHeight);
        _tracker.Move += (x, y) => QueueInput(new RemoteInputEvent(MessageType.MouseMove, x, y, null, false, 0, 0, 0, false));
        _tracker.Button += (button, isDown) => QueueInput(new RemoteInputEvent(MessageType.MouseButton, 0, 0, button, isDown, 0, 0, 0, false));
        _tracker.Wheel += delta => QueueInput(new RemoteInputEvent(MessageType.MouseWheel, 0, 0, null, false, delta, 0, 0, false));
        _tracker.Keyboard += (virtualKey, scanCode, isDown, isExtended) => QueueInput(
            new RemoteInputEvent(MessageType.KeyboardInput, 0, 0, null, isDown, 0, virtualKey, scanCode, isExtended));
        _tracker.RemoteModeChanged += active =>
        {
            if (!active)
            {
                QueueInput(new RemoteInputEvent(MessageType.KeyboardReset, 0, 0, null, false, 0, 0, 0, false));
            }

            SetStatus(active
                ? $"{_controlConnection?.Device.Name}에서 키보드·마우스 사용 중 · Ctrl+Alt+Esc로 해제"
                : $"준비됨 · {EntryEdgeText(edge)} 끝으로 마우스를 이동하세요");
        };
        _tracker.SecureShortcutBlocked += () => SetStatus("Ctrl+Alt+Delete는 Windows 보안상 원격 전송할 수 없습니다.");
        _tracker.EmergencyReleased += () => _ = Task.Run(() => StopAsync());
        _tracker.Start();
    }

    private void QueueInput(RemoteInputEvent input) => _inputChannel?.Writer.TryWrite(input);

    private async Task PumpInputAsync(ChannelReader<RemoteInputEvent> reader)
    {
        try
        {
            await foreach (var input in reader.ReadAllAsync())
            {
                var connection = _controlConnection;
                var sessionId = _sessionId;
                if (connection is null || sessionId == Guid.Empty)
                {
                    continue;
                }

                switch (input.Type)
                {
                    case MessageType.MouseMove:
                        await connection.SendJsonAsync(input.Type, new MouseMoveMessage(sessionId, input.X, input.Y), CancellationToken.None).ConfigureAwait(false);
                        break;
                    case MessageType.MouseButton:
                        await connection.SendJsonAsync(input.Type, new MouseButtonMessage(sessionId, input.Button!, input.IsDown), CancellationToken.None).ConfigureAwait(false);
                        break;
                    case MessageType.MouseWheel:
                        await connection.SendJsonAsync(input.Type, new MouseWheelMessage(sessionId, input.Delta), CancellationToken.None).ConfigureAwait(false);
                        break;
                    case MessageType.KeyboardInput:
                        await connection.SendJsonAsync(
                            input.Type,
                            new KeyboardInputMessage(sessionId, input.VirtualKey, input.ScanCode, input.IsDown, input.IsExtended),
                            CancellationToken.None).ConfigureAwait(false);
                        break;
                    case MessageType.KeyboardReset:
                        await connection.SendJsonAsync(input.Type, new KeyboardResetMessage(sessionId), CancellationToken.None).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (Exception exception)
        {
            await _log.ErrorAsync("Remote input send failed", exception).ConfigureAwait(false);
            await ResetAsync("원격 입력 전송 오류로 공유를 종료했습니다.").ConfigureAwait(false);
        }
    }

    private void HandleMouseMove(PeerMessageEventArgs args)
    {
        var move = ProtocolSerializer.Deserialize<MouseMoveMessage>(args.Message);
        if (!IsReceiving(args.Connection, move.SessionId) || !double.IsFinite(move.X) || !double.IsFinite(move.Y))
        {
            return;
        }

        MouseInputInjector.MoveTo(move.X, move.Y);
    }

    private void HandleMouseButton(PeerMessageEventArgs args)
    {
        var button = ProtocolSerializer.Deserialize<MouseButtonMessage>(args.Message);
        if (IsReceiving(args.Connection, button.SessionId))
        {
            MouseInputInjector.Button(button.Button, button.IsDown);
        }
    }

    private void HandleMouseWheel(PeerMessageEventArgs args)
    {
        var wheel = ProtocolSerializer.Deserialize<MouseWheelMessage>(args.Message);
        if (IsReceiving(args.Connection, wheel.SessionId))
        {
            MouseInputInjector.WheelBy(wheel.Delta);
        }
    }

    private void HandleKeyboardInput(PeerMessageEventArgs args)
    {
        var key = ProtocolSerializer.Deserialize<KeyboardInputMessage>(args.Message);
        if (!IsReceiving(args.Connection, key.SessionId) || key.VirtualKey == 0)
        {
            return;
        }

        MouseInputInjector.Keyboard(key.VirtualKey, key.ScanCode, key.IsDown, key.IsExtended);
        if (key.IsDown)
        {
            _injectedKeys[key.VirtualKey] = new InjectedKey(key.VirtualKey, key.ScanCode, key.IsExtended);
        }
        else
        {
            _injectedKeys.Remove(key.VirtualKey);
        }
    }

    private void HandleKeyboardReset(PeerMessageEventArgs args)
    {
        var reset = ProtocolSerializer.Deserialize<KeyboardResetMessage>(args.Message);
        if (IsReceiving(args.Connection, reset.SessionId))
        {
            ReleaseInjectedKeys();
        }
    }

    private async Task HandleControlStoppedAsync(PeerMessageEventArgs args)
    {
        var stop = ProtocolSerializer.Deserialize<MouseControlStopMessage>(args.Message);
        if ((_controlConnection?.Device.Id == args.Connection.Device.Id || _receivingPeerId == args.Connection.Device.Id) &&
            stop.SessionId == _sessionId)
        {
            await ResetAsync("상대 PC에서 입력 공유를 중지했습니다.").ConfigureAwait(false);
        }
    }

    private bool IsReceiving(PeerConnection connection, Guid sessionId) =>
        _receivingPeerId == connection.Device.Id && _sessionId == sessionId;

    private Task ResetAsync(string status)
    {
        ReleaseInjectedKeys();
        var tracker = Interlocked.Exchange(ref _tracker, null);
        tracker?.Dispose();
        var channel = Interlocked.Exchange(ref _inputChannel, null);
        channel?.Writer.TryComplete();
        _ = Interlocked.Exchange(ref _inputPump, null);
        _controlConnection = null;
        _sessionId = Guid.Empty;
        _receivingPeerId = Guid.Empty;
        _pendingEdge = null;
        SetStatus(status);
        return Task.CompletedTask;
    }

    private void ResetControllerState()
    {
        _tracker?.Dispose();
        _tracker = null;
        _controlConnection = null;
        _sessionId = Guid.Empty;
        _pendingEdge = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string status)
    {
        _status = status;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string EntryEdgeText(MouseTransitionEdge edge) => edge == MouseTransitionEdge.Right ? "오른쪽" : "왼쪽";

    public async ValueTask DisposeAsync()
    {
        _connections.MessageReceived -= OnMessageReceivedAsync;
        _connections.DeviceConnected -= OnDeviceConnected;
        _connections.DeviceDisconnected -= OnDeviceDisconnected;
        await ResetAsync("사용 안 함").ConfigureAwait(false);
    }

    private sealed record RemoteInputEvent(
        MessageType Type,
        double X,
        double Y,
        string? Button,
        bool IsDown,
        int Delta,
        ushort VirtualKey,
        ushort ScanCode,
        bool IsExtended);

    private void ReleaseInjectedKeys()
    {
        foreach (var key in _injectedKeys.Values.Reverse())
        {
            try
            {
                MouseInputInjector.Keyboard(key.VirtualKey, key.ScanCode, false, key.IsExtended);
            }
            catch (Exception exception)
            {
                _ = _log.WarningAsync($"Remote key release failed: {exception.Message}");
            }
        }

        _injectedKeys.Clear();
    }

    private sealed record InjectedKey(ushort VirtualKey, ushort ScanCode, bool IsExtended);
}

public sealed class RemoteMonitorChangedEventArgs(Guid deviceId, MonitorInfoMessage? monitor) : EventArgs
{
    public Guid DeviceId { get; } = deviceId;
    public MonitorInfoMessage? Monitor { get; } = monitor;
}
