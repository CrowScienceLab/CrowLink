using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrowLink.Models;
using CrowLink.Services.Logging;
using CrowLink.Services.RemoteMouse;
using CrowLink.Services.Settings;

namespace CrowLink.Services.Mobile;

public sealed class MobileTouchpadService : IAsyncDisposable
{
    private const int MaximumHeaderBytes = 16 * 1024;
    private const int MaximumEventsPerSecond = 180;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppSettings _settings;
    private readonly LogService _log;
    private readonly object _stateGate = new();
    private readonly ConcurrentDictionary<Guid, Task> _clientTasks = new();
    private CancellationTokenSource? _serverCancellation;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private TcpClient? _activeClient;
    private MobileSessionSnapshot? _session;
    private string _pairingCode = CreatePairingCode();
    private string _status = "Mobile Touchpad가 꺼져 있습니다.";
    private long _eventWindowStarted;
    private int _eventWindowCount;
    private long _lastStatisticsUpdate;
    private int _eventsThisInterval;
    private int _eventsPerSecond;
    private int _droppedEvents;
    private double _averageLatencyMs;
    private bool _leftDown;
    private bool _rightDown;
    private int _failedPairingAttempts;
    private double _sessionSensitivity;
    private bool _sessionAcceleration;
    private string _sessionMode = "touchpad";
    private int _sessionMonitorIndex;
    private int _autoStopQueued;

    public MobileTouchpadService(AppSettings settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    public event Func<MobilePairingRequest, Task<bool>>? PairingRequested;
    public event EventHandler? StateChanged;
    public event EventHandler? AutoStopped;

    public bool IsRunning => _listener is not null;
    public bool HasActiveSession => _session is not null;
    public string Status => _status;
    public string PairingCode => _pairingCode;
    public MobileSessionSnapshot? Session => _session;
    public string LocalAddress => FindLocalAddress()?.ToString() ?? "로컬 IP를 찾지 못함";
    public string MobileUrl => FindLocalAddress() is { } address
        ? $"http://{address}:{_settings.MobileTouchpadPort}/mobile"
        : $"http://<PC-IP>:{_settings.MobileTouchpadPort}/mobile";
    public string Statistics => $"{_eventsPerSecond} events/s · 평균 {_averageLatencyMs:0} ms · 누락 {_droppedEvents}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        if (_settings.MobileLocalNetworkOnly && FindLocalAddress() is null)
        {
            throw new InvalidOperationException("휴대폰에서 접근 가능한 사설 IPv4 네트워크를 찾지 못했습니다.");
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var listener = new TcpListener(IPAddress.Any, _settings.MobileTouchpadPort);
        try
        {
            listener.Start(16);
        }
        catch
        {
            cancellation.Dispose();
            throw;
        }

        _serverCancellation = cancellation;
        _listener = listener;
        _eventWindowStarted = Stopwatch.GetTimestamp();
        _lastStatisticsUpdate = _eventWindowStarted;
        _sessionSensitivity = Math.Clamp(_settings.MobileSensitivity, 0.5d, 5d);
        _sessionAcceleration = _settings.MobilePointerAcceleration;
        SetStatus("휴대폰 연결 대기 중");
        _acceptLoop = AcceptLoopAsync(listener, cancellation.Token);
        await _log.InfoAsync($"Mobile server started on port {_settings.MobileTouchpadPort}").ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var listener = Interlocked.Exchange(ref _listener, null);
        var cancellation = Interlocked.Exchange(ref _serverCancellation, null);
        if (listener is null && cancellation is null)
        {
            return;
        }

        cancellation?.Cancel();
        listener?.Stop();
        await DisconnectAsync("Mobile Touchpad 서버를 중지했습니다.", cancellationToken).ConfigureAwait(false);
        if (_acceptLoop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _acceptLoop = null;
        var clientTasks = _clientTasks.Values.ToArray();
        if (clientTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(clientTasks).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await _log.WarningAsync("Mobile client shutdown timed out").ConfigureAwait(false);
            }
        }

        cancellation?.Dispose();
        SetStatus("Mobile Touchpad가 꺼져 있습니다.");
        await _log.InfoAsync("Mobile server stopped").ConfigureAwait(false);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        DisconnectAsync("휴대폰 연결을 종료했습니다.", cancellationToken);

    public void RefreshPairingCode()
    {
        lock (_stateGate)
        {
            _pairingCode = CreatePairingCode();
        }

        NotifyStateChanged();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                await _log.WarningAsync($"Mobile client accept failed: {exception.Message}").ConfigureAwait(false);
                continue;
            }

            var clientId = Guid.NewGuid();
            var task = HandleClientAsync(client, cancellationToken);
            _clientTasks[clientId] = task;
            _ = task.ContinueWith(
                completedTask => _clientTasks.TryRemove(clientId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCancellation)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var remote = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address ?? IPAddress.None;
                if (_settings.MobileLocalNetworkOnly && !IsPrivateOrLocal(remote))
                {
                    await WriteHttpAsync(client.GetStream(), "403 Forbidden", "text/plain; charset=utf-8", "Local network only", serverCancellation)
                        .ConfigureAwait(false);
                    await _log.WarningAsync($"Mobile public-network request rejected: {remote}").ConfigureAwait(false);
                    return;
                }

                var stream = client.GetStream();
                var request = await ReadHttpRequestAsync(stream, serverCancellation).ConfigureAwait(false);
                if (request is null)
                {
                    return;
                }

                if (request.Path.Equals("/mobile", StringComparison.OrdinalIgnoreCase) || request.Path == "/")
                {
                    await WriteHttpAsync(stream, "200 OK", "text/html; charset=utf-8", MobileWebAssets.Html(_settings.DeviceName), serverCancellation)
                        .ConfigureAwait(false);
                    return;
                }

                if (request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHttpAsync(stream, "200 OK", "application/json; charset=utf-8", "{\"status\":\"ok\"}", serverCancellation)
                        .ConfigureAwait(false);
                    return;
                }

                if (!request.Path.Equals("/ws", StringComparison.OrdinalIgnoreCase) ||
                    !request.Headers.TryGetValue("Upgrade", out var upgrade) ||
                    !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteHttpAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found", serverCancellation)
                        .ConfigureAwait(false);
                    return;
                }

                await using var socket = await MobileWebSocket.AcceptAsync(stream, request.Headers, serverCancellation)
                    .ConfigureAwait(false);
                if (socket is null)
                {
                    return;
                }

                await HandleWebSocketAsync(client, socket, remote, serverCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException exception)
            {
                await _log.WarningAsync($"Mobile connection closed: {exception.Message}").ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await _log.ErrorAsync("Mobile client failed", exception).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleWebSocketAsync(
        TcpClient client,
        MobileWebSocket socket,
        IPAddress remote,
        CancellationToken cancellationToken)
    {
        SetStatus($"휴대폰 감지 · {remote}");
        await _log.InfoAsync($"Mobile client detected: {remote}").ConfigureAwait(false);
        var firstMessage = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
        if (!TryReadHello(firstMessage, out var suppliedCode, out var deviceName) || !MatchesPairingCode(suppliedCode))
        {
            if (Interlocked.Increment(ref _failedPairingAttempts) >= 5)
            {
                Interlocked.Exchange(ref _failedPairingAttempts, 0);
                RefreshPairingCode();
            }

            await SendAsync(socket, new { type = "error", message = "페어링 코드가 올바르지 않습니다." }, cancellationToken)
                .ConfigureAwait(false);
            await _log.WarningAsync($"Mobile authentication failed: {remote}").ConfigureAwait(false);
            SetStatus("잘못된 모바일 페어링 코드를 거부했습니다.");
            return;
        }

        if (HasActiveSession)
        {
            await SendAsync(socket, new { type = "error", message = "다른 휴대폰이 이미 연결되어 있습니다." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        SetStatus($"{deviceName} 승인 대기 중");
        await _log.InfoAsync($"Mobile pairing requested: {deviceName} ({remote})").ConfigureAwait(false);
        var approved = await RequestApprovalAsync(new MobilePairingRequest(deviceName, remote)).ConfigureAwait(false);
        if (!approved)
        {
            await SendAsync(socket, new { type = "error", message = "PC에서 연결 요청을 거부했습니다." }, cancellationToken)
                .ConfigureAwait(false);
            SetStatus("모바일 연결 요청을 거부했습니다.");
            return;
        }

        var session = new MobileSessionSnapshot(Guid.NewGuid(), deviceName, remote, DeviceType.MobileBrowser, DateTimeOffset.Now);
        Interlocked.Exchange(ref _failedPairingAttempts, 0);
        lock (_stateGate)
        {
            if (_session is not null)
            {
                approved = false;
            }
            else
            {
                _session = session;
                _activeClient = client;
                _pairingCode = CreatePairingCode();
                _sessionSensitivity = Math.Clamp(_settings.MobileSensitivity, 0.5d, 5d);
                _sessionAcceleration = _settings.MobilePointerAcceleration;
                _sessionMode = "touchpad";
                _sessionMonitorIndex = 0;
            }
        }

        if (!approved)
        {
            await SendAsync(socket, new { type = "error", message = "다른 휴대폰이 먼저 연결되었습니다." }, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var monitorInfo = MouseInputInjector.GetMonitorInfo();
        var monitors = monitorInfo.Monitors.Select((monitor, index) => new
        {
            index,
            name = monitor.IsPrimary ? $"주 화면 · {monitor.Width}×{monitor.Height}" : $"화면 {index + 1} · {monitor.Width}×{monitor.Height}",
            width = monitor.Width,
            height = monitor.Height,
            primary = monitor.IsPrimary,
        }).ToArray();
        _sessionMonitorIndex = Array.FindIndex(monitors, monitor => monitor.primary);
        if (_sessionMonitorIndex < 0)
        {
            _sessionMonitorIndex = 0;
        }

        await SendAsync(socket, new
        {
            type = "paired",
            session = session.SessionId,
            sensitivity = _sessionSensitivity,
            acceleration = _sessionAcceleration,
            monitorIndex = _sessionMonitorIndex,
            monitors,
        }, cancellationToken).ConfigureAwait(false);
        SetStatus($"{deviceName} 연결됨 · Ctrl+Alt+Esc 또는 연결 종료로 해제");
        await _log.InfoAsync($"Mobile connected: {deviceName} ({remote})").ConfigureAwait(false);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var text = await socket.ReceiveTextAsync(cancellationToken).ConfigureAwait(false);
                if (text is null || !await ProcessInputAsync(text, session, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        finally
        {
            var ownsSession = false;
            lock (_stateGate)
            {
                if (_session?.SessionId == session.SessionId)
                {
                    _session = null;
                    _activeClient = null;
                    ownsSession = true;
                }
            }

            if (ownsSession)
            {
                ReleaseButtons();
                SetStatus("휴대폰 연결 종료 · 서버를 자동으로 중지합니다.");
                await _log.InfoAsync($"Mobile disconnected: {deviceName}").ConfigureAwait(false);
                QueueAutoStop();
            }
        }
    }

    private async Task<bool> ProcessInputAsync(
        string text,
        MobileSessionSnapshot session,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (!TryGetString(root, "type", out var type) ||
            !TryGetGuid(root, "session", out var sessionId) ||
            sessionId != session.SessionId)
        {
            return true;
        }

        if (type == "disconnect")
        {
            return false;
        }

        if (type == "settings")
        {
            ApplySessionSettings(root);
            UpdateStatistics(root);
            return true;
        }

        if (!AllowInputEvent())
        {
            Interlocked.Increment(ref _droppedEvents);
            UpdateStatistics(root);
            return true;
        }

        try
        {
            switch (type)
            {
                case "move" when TryGetNumber(root, "dx", out var dx) && TryGetNumber(root, "dy", out var dy):
                    var movement = MobilePointerMath.ScaleMovement(
                        dx,
                        dy,
                        _sessionSensitivity,
                        _sessionAcceleration);
                    MouseInputInjector.MoveBy(movement.X, movement.Y);
                    break;
                case "pen" when TryGetNumber(root, "x", out var x) &&
                                     TryGetNumber(root, "y", out var y) &&
                                     TryGetString(root, "phase", out var phase):
                    ApplyPenInput(x, y, phase);
                    break;
                case "click" when TryGetString(root, "button", out var clickButton):
                    Click(clickButton);
                    break;
                case "button" when TryGetString(root, "button", out var button) &&
                                   root.TryGetProperty("down", out var downElement) &&
                                   downElement.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    SetButton(button, downElement.GetBoolean());
                    break;
                case "scroll" when TryGetNumber(root, "delta", out var delta):
                    var wheel = MobilePointerMath.ScaleWheel(delta, _settings.MobileScrollSpeed);
                    if (wheel != 0)
                    {
                        MouseInputInjector.WheelBy(wheel);
                    }

                    if (TryGetNumber(root, "horizontal", out var horizontal))
                    {
                        var horizontalWheel = MobilePointerMath.ScaleWheel(horizontal, _settings.MobileScrollSpeed);
                        if (horizontalWheel != 0)
                        {
                            MouseInputInjector.HorizontalWheelBy(horizontalWheel);
                        }
                    }

                    break;
                default:
                    Interlocked.Increment(ref _droppedEvents);
                    break;
            }
        }
        catch (Exception exception)
        {
            await _log.WarningAsync($"Mobile input ignored: {exception.Message}").ConfigureAwait(false);
        }

        UpdateStatistics(root);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private void Click(string button)
    {
        var normalized = NormalizeButton(button);
        MouseInputInjector.Button(normalized, true);
        MouseInputInjector.Button(normalized, false);
    }

    private void ApplySessionSettings(JsonElement root)
    {
        if (TryGetNumber(root, "sensitivity", out var sensitivity))
        {
            _sessionSensitivity = Math.Clamp(sensitivity, 0.5d, 5d);
        }

        if (TryGetBoolean(root, "acceleration", out var acceleration))
        {
            _sessionAcceleration = acceleration;
        }

        if (TryGetString(root, "mode", out var mode) && mode is "touchpad" or "pen")
        {
            if (_sessionMode == "pen" && mode != "pen")
            {
                ReleaseButtons();
            }

            _sessionMode = mode;
        }

        if (TryGetInteger(root, "monitorIndex", out var monitorIndex))
        {
            var count = MouseInputInjector.GetMonitorInfo().MonitorCount;
            _sessionMonitorIndex = Math.Clamp(monitorIndex, 0, Math.Max(0, count - 1));
        }
    }

    private void ApplyPenInput(double x, double y, string phase)
    {
        if (_sessionMode != "pen")
        {
            return;
        }

        if (phase == "cancel")
        {
            SetButton("left", false);
            return;
        }

        var monitorInfo = MouseInputInjector.GetMonitorInfo();
        if (monitorInfo.Monitors.Count == 0)
        {
            return;
        }

        var monitor = monitorInfo.Monitors[Math.Clamp(_sessionMonitorIndex, 0, monitorInfo.Monitors.Count - 1)];
        var virtualLeft = monitorInfo.Monitors.Min(item => item.X);
        var virtualTop = monitorInfo.Monitors.Min(item => item.Y);
        var absoluteX = (monitor.X - virtualLeft + (Math.Clamp(x, 0d, 1d) * monitor.Width)) / monitorInfo.VirtualWidth;
        var absoluteY = (monitor.Y - virtualTop + (Math.Clamp(y, 0d, 1d) * monitor.Height)) / monitorInfo.VirtualHeight;
        MouseInputInjector.MoveTo(absoluteX, absoluteY);

        switch (phase)
        {
            case "down":
                SetButton("left", true);
                break;
            case "up":
                SetButton("left", false);
                break;
            case "move":
                break;
            default:
                throw new InvalidDataException("Unsupported mobile pen phase.");
        }
    }

    private void QueueAutoStop()
    {
        if (Interlocked.Exchange(ref _autoStopQueued, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the current WebSocket task unwind before StopAsync waits for clients.
                await Task.Delay(150).ConfigureAwait(false);
                _settings.EnableMobileTouchpad = false;
                await StopAsync().ConfigureAwait(false);
                SetStatus("휴대폰 연결 종료 · 서버 자동 중지");
                AutoStopped?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                await _log.ErrorAsync("Mobile server auto-stop failed", exception).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _autoStopQueued, 0);
            }
        });
    }

    private void SetButton(string button, bool down)
    {
        var normalized = NormalizeButton(button);
        if (normalized == "Left")
        {
            if (_leftDown == down)
            {
                return;
            }

            _leftDown = down;
        }
        else
        {
            if (_rightDown == down)
            {
                return;
            }

            _rightDown = down;
        }

        MouseInputInjector.Button(normalized, down);
    }

    private static string NormalizeButton(string button) => button.ToLowerInvariant() switch
    {
        "left" => "Left",
        "right" => "Right",
        _ => throw new InvalidDataException("Unsupported mobile mouse button."),
    };

    private void ReleaseButtons()
    {
        if (_leftDown)
        {
            _leftDown = false;
            TryReleaseButton("Left");
        }

        if (_rightDown)
        {
            _rightDown = false;
            TryReleaseButton("Right");
        }
    }

    private void TryReleaseButton(string button)
    {
        try
        {
            MouseInputInjector.Button(button, false);
        }
        catch (Exception exception)
        {
            _ = _log.WarningAsync($"Mobile mouse button release failed: {exception.Message}");
        }
    }

    private async Task DisconnectAsync(string status, CancellationToken cancellationToken)
    {
        TcpClient? client;
        lock (_stateGate)
        {
            client = _activeClient;
            _activeClient = null;
            _session = null;
        }

        ReleaseButtons();
        if (client is not null)
        {
            try
            {
                client.Client.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException)
            {
            }

            client.Close();
        }

        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(status);
        if (client is not null)
        {
            await _log.InfoAsync("Mobile input session stopped").ConfigureAwait(false);
        }
    }

    private async Task<bool> RequestApprovalAsync(MobilePairingRequest request)
    {
        var handlers = PairingRequested?.GetInvocationList()
            .Cast<Func<MobilePairingRequest, Task<bool>>>()
            .ToArray();
        if (handlers is not { Length: > 0 })
        {
            return false;
        }

        foreach (var handler in handlers)
        {
            if (!await handler(request).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private bool MatchesPairingCode(string suppliedCode)
    {
        var expected = Encoding.UTF8.GetBytes(_pairingCode);
        var supplied = Encoding.UTF8.GetBytes(suppliedCode);
        return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private bool AllowInputEvent()
    {
        var now = Stopwatch.GetTimestamp();
        lock (_stateGate)
        {
            if (Stopwatch.GetElapsedTime(_eventWindowStarted, now) >= TimeSpan.FromSeconds(1))
            {
                _eventWindowStarted = now;
                _eventWindowCount = 0;
            }

            if (_eventWindowCount >= MaximumEventsPerSecond)
            {
                return false;
            }

            _eventWindowCount++;
            _eventsThisInterval++;
            return true;
        }
    }

    private void UpdateStatistics(JsonElement root)
    {
        if (TryGetNumber(root, "clientTime", out var clientTime))
        {
            var latency = Math.Clamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clientTime, 0d, 5000d);
            _averageLatencyMs = _averageLatencyMs <= 0d ? latency : (_averageLatencyMs * 0.9d) + (latency * 0.1d);
        }

        var now = Stopwatch.GetTimestamp();
        lock (_stateGate)
        {
            var elapsed = Stopwatch.GetElapsedTime(_lastStatisticsUpdate, now);
            if (elapsed < TimeSpan.FromSeconds(1))
            {
                return;
            }

            _eventsPerSecond = (int)Math.Round(_eventsThisInterval / elapsed.TotalSeconds);
            _eventsThisInterval = 0;
            _lastStatisticsUpdate = now;
        }

        NotifyStateChanged();
    }

    private void SetStatus(string status)
    {
        _status = status;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static async Task<HttpRequest?> ReadHttpRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var current = new byte[1];
        var matched = 0;
        byte[] delimiter = [13, 10, 13, 10];
        while (buffer.Length < MaximumHeaderBytes)
        {
            var read = await stream.ReadAsync(current, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            buffer.WriteByte(current[0]);
            matched = current[0] == delimiter[matched] ? matched + 1 : current[0] == delimiter[0] ? 1 : 0;
            if (matched == delimiter.Length)
            {
                break;
            }
        }

        if (matched != delimiter.Length)
        {
            throw new InvalidDataException("HTTP header is too large.");
        }

        var headerText = Encoding.ASCII.GetString(buffer.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var requestLine = lines.FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine is not { Length: >= 2 } || requestLine[0] != "GET")
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        var path = requestLine[1].Split('?', 2)[0];
        return new HttpRequest(path, headers);
    }

    private static async Task WriteHttpAsync(
        Stream stream,
        string status,
        string contentType,
        string content,
        CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(content);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\n" +
            "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
            "Content-Security-Policy: default-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private static Task SendAsync(
        MobileWebSocket socket,
        object payload,
        CancellationToken cancellationToken) =>
        socket.SendTextAsync(JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);

    private static bool TryReadHello(string? text, out string code, out string deviceName)
    {
        code = string.Empty;
        deviceName = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (!TryGetString(root, "type", out var type) || type != "hello" ||
            !TryGetString(root, "code", out code) || code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
        {
            return false;
        }

        if (!TryGetString(root, "name", out deviceName))
        {
            deviceName = "Mobile Browser";
        }

        deviceName = new string(deviceName.Where(character => !char.IsControl(character)).Take(60).ToArray()).Trim();
        if (deviceName.Length == 0)
        {
            deviceName = "Mobile Browser";
        }

        return true;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetGuid(JsonElement root, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryGetString(root, name, out var text) && Guid.TryParse(text, out value);
    }

    private static bool TryGetNumber(JsonElement root, string name, out double value)
    {
        value = 0d;
        return root.TryGetProperty(name, out var element) && element.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static bool TryGetBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = element.GetBoolean();
        return true;
    }

    private static bool TryGetInteger(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element) && element.TryGetInt32(out value);
    }

    private static string CreatePairingCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static IPAddress? FindLocalAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                              network.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .SelectMany(network =>
            {
                var properties = network.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(gateway =>
                    !gateway.Address.Equals(IPAddress.Any) && !gateway.Address.Equals(IPAddress.IPv6Any));
                var isPhysical = !network.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase) &&
                                 !network.Description.Contains("hyper-v", StringComparison.OrdinalIgnoreCase) &&
                                 !network.Description.Contains("wsl", StringComparison.OrdinalIgnoreCase);
                return properties.UnicastAddresses
                    .Select(address => new
                    {
                        address.Address,
                        HasGateway = hasGateway,
                        IsPhysical = isPhysical,
                        PreferredType = network.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet,
                        network.Speed,
                    });
            })
            .Where(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivateOrLocal(candidate.Address))
            .OrderByDescending(candidate => candidate.HasGateway)
            .ThenByDescending(candidate => candidate.IsPhysical)
            .ThenByDescending(candidate => candidate.PreferredType)
            .ThenByDescending(candidate => candidate.Speed)
            .Select(candidate => candidate.Address)
            .FirstOrDefault();
    }

    public static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (bytes[0] & 0xFE) == 0xFC;
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        PairingRequested = null;
        StateChanged = null;
        AutoStopped = null;
        await StopAsync().ConfigureAwait(false);
    }

    private sealed record HttpRequest(string Path, IReadOnlyDictionary<string, string> Headers);
}
