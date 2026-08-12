using System.Collections.Concurrent;
using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;
using CrowLink.Services.Transfer;
using CrowLink.Services.Settings;

namespace CrowLink.Services.Explorer;

public sealed class ExplorerBridgeService : IAsyncDisposable
{
    private const int MaxRootsPerPackage = 32;
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(30);
    private readonly ConnectionService _connections;
    private readonly FileTransferService _transfers;
    private readonly LogService _log;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<Guid, OutgoingPackage> _outgoing = new();
    private readonly ConcurrentDictionary<Guid, IncomingPackage> _incoming = new();

    public ExplorerBridgeService(
        ConnectionService connections,
        FileTransferService transfers,
        LogService log,
        AppSettings settings)
    {
        _connections = connections;
        _transfers = transfers;
        _log = log;
        _settings = settings;
        _connections.MessageReceived += OnMessageReceivedAsync;
        _connections.DeviceDisconnected += OnDeviceDisconnected;
        _transfers.IncomingRootCompleted += OnIncomingRootCompleted;
    }

    public event Func<ExplorerDragOfferRequest, Task<bool>>? OfferApprovalRequested;
    public event EventHandler<ExplorerPackageChangedEventArgs>? PackageChanged;

    public async Task<bool> ConsumeIncomingPackageAsync(Guid packageId)
    {
        if (!_incoming.TryGetValue(packageId, out var package))
        {
            return false;
        }

        string[] roots;
        lock (package.RootPaths)
        {
            if (package.RootPaths.Count != package.ExpectedRootCount)
            {
                return false;
            }

            roots = package.RootPaths.ToArray();
        }

        ExplorerStagingCleaner.DeleteRoots(_settings.ReceiveFolder, roots);

        _incoming.TryRemove(packageId, out _);
        Publish(package, "원하는 폴더로 이동 완료 · staging 삭제됨", [], false);
        await _log.InfoAsync($"Explorer staging consumed: {packageId}").ConfigureAwait(false);
        return true;
    }

    public async Task SendPackageAsync(
        PeerConnection connection,
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        var normalized = paths
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRootsPerPackage + 1)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("Explorer에서 가져온 파일이나 폴더가 없습니다.");
        }

        if (normalized.Length > MaxRootsPerPackage)
        {
            throw new InvalidOperationException($"한 번에 최대 {MaxRootsPerPackage}개 루트 항목을 보낼 수 있습니다.");
        }

        var descriptors = normalized.Select(CreateDescriptor).ToArray();
        var packageId = Guid.NewGuid();
        var package = new OutgoingPackage(
            packageId,
            connection.Device.Id,
            connection.Device.Name,
            CreateSummary(descriptors),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!_outgoing.TryAdd(packageId, package))
        {
            throw new InvalidOperationException("Explorer package identifier collision.");
        }

        Publish(package, "상대 PC의 수락을 기다리는 중");
        try
        {
            await connection.SendJsonAsync(
                MessageType.ExplorerDragOffer,
                new ExplorerDragOfferMessage(packageId, descriptors),
                cancellationToken).ConfigureAwait(false);

            var approved = await package.Approval.Task.WaitAsync(ApprovalTimeout, cancellationToken).ConfigureAwait(false);
            if (!approved)
            {
                Publish(package, "상대 PC에서 Explorer 전송을 거부했습니다.");
                return;
            }

            Publish(package, "원격 Explorer 패키지 전송 중");
            var transferred = await _transfers.SendPathsAsync(
                connection,
                normalized,
                packageId,
                cancellationToken).ConfigureAwait(false);
            if (!transferred)
            {
                await TrySendAbortAsync(connection, packageId, "파일 전송이 완료되지 않았습니다.").ConfigureAwait(false);
                Publish(package, "파일 전송 실패 또는 취소");
                return;
            }

            Publish(package, package.IsReady
                ? "상대 PC에서 Explorer 드래그 준비 완료"
                : "상대 PC에서 Explorer 드래그 준비 중");
        }
        catch (TimeoutException)
        {
            await TrySendAbortAsync(connection, packageId, "승인 시간이 초과되었습니다.").ConfigureAwait(false);
            Publish(package, "승인 시간 초과");
        }
        catch (Exception exception)
        {
            await TrySendAbortAsync(connection, packageId, exception.Message).ConfigureAwait(false);
            Publish(package, $"실패: {exception.Message}");
            throw;
        }
    }

    private async Task OnMessageReceivedAsync(PeerMessageEventArgs args)
    {
        switch (args.Message.Type)
        {
            case MessageType.ExplorerDragOffer:
                await HandleOfferAsync(args).ConfigureAwait(false);
                break;
            case MessageType.ExplorerDragAccept:
                CompleteApproval(args, true);
                break;
            case MessageType.ExplorerDragReject:
                CompleteApproval(args, false);
                break;
            case MessageType.ExplorerDragReady:
                HandleReady(args);
                break;
            case MessageType.ExplorerDragAbort:
                HandleAbort(args);
                break;
        }
    }

    private async Task HandleOfferAsync(PeerMessageEventArgs args)
    {
        var offer = ProtocolSerializer.Deserialize<ExplorerDragOfferMessage>(args.Message);
        ValidateOffer(offer);
        var handlers = OfferApprovalRequested?.GetInvocationList()
            .Cast<Func<ExplorerDragOfferRequest, Task<bool>>>()
            .ToArray();
        var approved = handlers is { Length: > 0 };
        if (approved)
        {
            foreach (var handler in handlers!)
            {
                approved &= await handler(new ExplorerDragOfferRequest(
                    args.Connection,
                    offer.PackageId,
                    offer.Items)).ConfigureAwait(false);
            }
        }

        if (!approved)
        {
            await args.Connection.SendJsonAsync(
                MessageType.ExplorerDragReject,
                new ExplorerDragResponseMessage(offer.PackageId),
                CancellationToken.None).ConfigureAwait(false);
            return;
        }

        var package = new IncomingPackage(
            offer.PackageId,
            args.Connection.Device.Id,
            args.Connection.Device.Name,
            CreateSummary(offer.Items),
            offer.Items.Count);
        if (!_incoming.TryAdd(offer.PackageId, package))
        {
            throw new InvalidDataException("Duplicate Explorer drag package.");
        }

        Publish(package, "원격 파일 수신 대기 중", [], false);
        await args.Connection.SendJsonAsync(
            MessageType.ExplorerDragAccept,
            new ExplorerDragResponseMessage(offer.PackageId),
            CancellationToken.None).ConfigureAwait(false);
    }

    private void CompleteApproval(PeerMessageEventArgs args, bool approved)
    {
        var response = ProtocolSerializer.Deserialize<ExplorerDragResponseMessage>(args.Message);
        if (_outgoing.TryGetValue(response.PackageId, out var package) && package.DeviceId == args.Connection.Device.Id)
        {
            package.Approval.TrySetResult(approved);
        }
    }

    private void HandleReady(PeerMessageEventArgs args)
    {
        var ready = ProtocolSerializer.Deserialize<ExplorerDragReadyMessage>(args.Message);
        if (_outgoing.TryGetValue(ready.PackageId, out var package) && package.DeviceId == args.Connection.Device.Id)
        {
            package.IsReady = true;
            Publish(package, "상대 PC에서 Explorer 드래그 준비 완료");
        }
    }

    private void HandleAbort(PeerMessageEventArgs args)
    {
        var abort = ProtocolSerializer.Deserialize<ExplorerDragAbortMessage>(args.Message);
        if (_incoming.TryGetValue(abort.PackageId, out var existingIncoming) &&
            existingIncoming.DeviceId == args.Connection.Device.Id &&
            _incoming.TryRemove(abort.PackageId, out var incoming))
        {
            Publish(incoming, $"중단됨: {abort.Reason}", incoming.RootPaths.ToArray(), false);
        }
        else if (_outgoing.TryGetValue(abort.PackageId, out var outgoing) && outgoing.DeviceId == args.Connection.Device.Id)
        {
            outgoing.Approval.TrySetResult(false);
            Publish(outgoing, $"상대 PC에서 중단: {abort.Reason}");
        }
    }

    private void OnIncomingRootCompleted(object? sender, IncomingRootCompletedEventArgs e)
    {
        if (!_incoming.TryGetValue(e.PackageId, out var package) || package.DeviceId != e.DeviceId)
        {
            return;
        }

        var ready = false;
        lock (package.RootPaths)
        {
            if (!package.RootPaths.Contains(e.RootPath, StringComparer.OrdinalIgnoreCase))
            {
                package.RootPaths.Add(e.RootPath);
            }

            ready = package.RootPaths.Count == package.ExpectedRootCount;
        }

        if (!ready)
        {
            Publish(package, $"수신 중 · {package.RootPaths.Count}/{package.ExpectedRootCount}", package.RootPaths.ToArray(), false);
            return;
        }

        var paths = package.RootPaths.ToArray();
        Publish(package, "Explorer로 드래그할 준비 완료", paths, true);
        _ = NotifyReadyAsync(package);
    }

    private async Task NotifyReadyAsync(IncomingPackage package)
    {
        if (!_connections.TryGetConnection(package.DeviceId, out var connection) || connection is null)
        {
            return;
        }

        try
        {
            await connection.SendJsonAsync(
                MessageType.ExplorerDragReady,
                new ExplorerDragReadyMessage(package.PackageId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _log.WarningAsync($"Explorer ready notice failed: {exception.Message}").ConfigureAwait(false);
        }
    }

    private void OnDeviceDisconnected(object? sender, Models.DeviceInfo device)
    {
        foreach (var package in _outgoing.Values.Where(item => item.DeviceId == device.Id))
        {
            package.Approval.TrySetResult(false);
            Publish(package, "연결 끊김");
            _outgoing.TryRemove(package.PackageId, out _);
        }

        foreach (var entry in _incoming.Where(item => item.Value.DeviceId == device.Id).ToArray())
        {
            if (_incoming.TryRemove(entry.Key, out var package))
            {
                Publish(package, "연결 끊김", package.RootPaths.ToArray(), false);
            }
        }
    }

    private void Publish(OutgoingPackage package, string status) =>
        Publish(new ExplorerPackageSnapshot(
            package.PackageId,
            package.DeviceId,
            package.DeviceName,
            false,
            package.Summary,
            status,
            [],
            false));

    private void Publish(IncomingPackage package, string status, IReadOnlyList<string> paths, bool canDrag) =>
        Publish(new ExplorerPackageSnapshot(
            package.PackageId,
            package.DeviceId,
            package.DeviceName,
            true,
            package.Summary,
            status,
            paths,
            canDrag));

    private void Publish(ExplorerPackageSnapshot snapshot) =>
        PackageChanged?.Invoke(this, new ExplorerPackageChangedEventArgs(snapshot));

    private static ExplorerDragItemDescriptor CreateDescriptor(string path)
    {
        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            return new ExplorerDragItemDescriptor(file.Name, false, file.Length);
        }

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return new ExplorerDragItemDescriptor(name, true, 0);
    }

    private static void ValidateOffer(ExplorerDragOfferMessage offer)
    {
        if (offer.PackageId == Guid.Empty || offer.Items is null || offer.Items.Count is < 1 or > MaxRootsPerPackage ||
            offer.Items.Any(item => item.Size < 0 || string.IsNullOrWhiteSpace(item.Name) ||
                !string.Equals(Path.GetFileName(item.Name), item.Name, StringComparison.Ordinal) || item.Name is "." or ".."))
        {
            throw new InvalidDataException("Invalid Explorer drag offer.");
        }
    }

    private static string CreateSummary(IReadOnlyList<ExplorerDragItemDescriptor> items)
    {
        var names = string.Join(", ", items.Take(3).Select(item => item.Name));
        return items.Count <= 3 ? names : $"{names} 외 {items.Count - 3}개";
    }

    private static async Task TrySendAbortAsync(PeerConnection connection, Guid packageId, string reason)
    {
        try
        {
            await connection.SendJsonAsync(
                MessageType.ExplorerDragAbort,
                new ExplorerDragAbortMessage(packageId, reason.Length <= 300 ? reason : reason[..300]),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The original failure remains the useful result.
        }
    }

    public ValueTask DisposeAsync()
    {
        _connections.MessageReceived -= OnMessageReceivedAsync;
        _connections.DeviceDisconnected -= OnDeviceDisconnected;
        _transfers.IncomingRootCompleted -= OnIncomingRootCompleted;
        foreach (var package in _outgoing.Values)
        {
            package.Approval.TrySetCanceled();
        }

        _outgoing.Clear();
        _incoming.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class OutgoingPackage(
        Guid packageId,
        Guid deviceId,
        string deviceName,
        string summary,
        TaskCompletionSource<bool> approval)
    {
        public Guid PackageId { get; } = packageId;
        public Guid DeviceId { get; } = deviceId;
        public string DeviceName { get; } = deviceName;
        public string Summary { get; } = summary;
        public TaskCompletionSource<bool> Approval { get; } = approval;
        public bool IsReady { get; set; }
    }

    private sealed record IncomingPackage(
        Guid PackageId,
        Guid DeviceId,
        string DeviceName,
        string Summary,
        int ExpectedRootCount)
    {
        public List<string> RootPaths { get; } = [];
    }
}
