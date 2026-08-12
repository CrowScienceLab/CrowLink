using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using CrowLink.Models;
using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;
using CrowLink.Services.Settings;
using CrowLink.Utilities;

namespace CrowLink.Services.Transfer;

public sealed class FileTransferService : IAsyncDisposable
{
    private readonly AppSettings _settings;
    private readonly ConnectionService _connections;
    private readonly LogService _log;
    private readonly ConcurrentDictionary<Guid, IncomingBatch> _incomingBatches = new();
    private readonly ConcurrentDictionary<Guid, IncomingFile> _incomingFiles = new();
    private readonly ConcurrentDictionary<Guid, TransferSession> _outgoingSessions = new();
    private readonly CancellationTokenSource _lifetimeCts = new();

    public FileTransferService(AppSettings settings, ConnectionService connections, LogService log)
    {
        _settings = settings;
        _connections = connections;
        _log = log;
        _connections.MessageReceived += OnMessageReceivedAsync;
        _connections.DeviceDisconnected += OnDeviceDisconnected;
    }

    public event EventHandler<TransferItem>? TransferAdded;
    public event EventHandler<TransferItem>? TransferChanged;
    public event EventHandler<IncomingRootCompletedEventArgs>? IncomingRootCompleted;

    public async Task<bool> CancelTransferAsync(Guid batchId)
    {
        if (!_outgoingSessions.TryGetValue(batchId, out var session) || !session.Item.CanCancel)
        {
            return false;
        }

        await session.Cancellation.CancelAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SendPathsAsync(
        PeerConnection connection,
        IEnumerable<string> paths,
        Guid explorerPackageId = default,
        CancellationToken cancellationToken = default)
    {
        var sentAny = false;
        var allSucceeded = true;
        foreach (var sourcePath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                continue;
            }

            sentAny = true;
            allSucceeded &= await SendRootAsync(connection, fullPath, explorerPackageId, cancellationToken).ConfigureAwait(false);
        }

        return sentAny && allSucceeded;
    }

    private async Task<bool> SendRootAsync(
        PeerConnection connection,
        string rootPath,
        Guid explorerPackageId,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var entries = EnumerateEntries(rootPath).ToArray();
        var totalBytes = entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size);
        var item = new TransferItem(batchId, Path.GetFileName(rootPath), false)
        {
            TotalBytes = totalBytes,
            Status = TransferStatus.Preparing,
        };
        TransferAdded?.Invoke(this, item);

        using var userCancellation = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCts.Token,
            userCancellation.Token);
        var session = new TransferSession(item, userCancellation);
        if (!_outgoingSessions.TryAdd(batchId, session))
        {
            throw new InvalidOperationException("Duplicate outgoing transfer identifier.");
        }

        var succeeded = false;
        try
        {
            item.Status = TransferStatus.Transferring;
            TransferChanged?.Invoke(this, item);
            await _log.InfoAsync($"Transfer started: {batchId}").ConfigureAwait(false);

            foreach (var entry in entries)
            {
                linkedCts.Token.ThrowIfCancellationRequested();
                var transferId = Guid.NewGuid();
                var metadata = new FileMetadataMessage(
                    batchId,
                    transferId,
                    entry.RelativePath,
                    entry.Size,
                    entry.LastWriteTime,
                    entry.IsDirectory,
                    entry.IsRoot,
                    explorerPackageId);
                await connection.SendJsonAsync(MessageType.FileMetadata, metadata, _lifetimeCts.Token).ConfigureAwait(false);

                if (!entry.IsDirectory)
                {
                    await SendFileContentAsync(connection, transferId, entry.FullPath, item, linkedCts.Token).ConfigureAwait(false);
                }

                linkedCts.Token.ThrowIfCancellationRequested();
                await connection.SendJsonAsync(
                    MessageType.FileComplete,
                    new FileCompleteMessage(batchId, transferId),
                    _lifetimeCts.Token).ConfigureAwait(false);
            }

            linkedCts.Token.ThrowIfCancellationRequested();
            await connection.SendJsonAsync(
                MessageType.FileComplete,
                new FileCompleteMessage(batchId, Guid.Empty, true),
                _lifetimeCts.Token).ConfigureAwait(false);
            item.TransferredBytes = totalBytes;
            item.Status = TransferStatus.Completed;
            TransferChanged?.Invoke(this, item);
            await _log.InfoAsync($"Transfer completed: {batchId}").ConfigureAwait(false);
            succeeded = true;
        }
        catch (OperationCanceledException)
        {
            item.Status = TransferStatus.Cancelled;
            TransferChanged?.Invoke(this, item);
            await TrySendCancelAsync(connection, batchId, "Cancelled", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            item.ErrorMessage = exception.Message;
            item.Status = TransferStatus.Failed;
            TransferChanged?.Invoke(this, item);
            await _log.ErrorAsync($"Transfer failed: {batchId}", exception).ConfigureAwait(false);
            await TrySendCancelAsync(connection, batchId, "Sender error", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _outgoingSessions.TryRemove(batchId, out _);
        }

        return succeeded;
    }

    private async Task SendFileContentAsync(
        PeerConnection connection,
        Guid transferId,
        string path,
        TransferItem item,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_settings.ChunkSizeBytes);
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                _settings.ChunkSizeBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, _settings.ChunkSizeBytes), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                var payload = FileChunkMessage.CreatePayload(transferId, buffer.AsSpan(0, read));
                // Finish the current frame before observing a user cancellation. Cancelling a
                // NetworkStream write mid-frame would corrupt the length-prefixed protocol.
                await connection.SendAsync(MessageType.FileChunk, payload, _lifetimeCts.Token).ConfigureAwait(false);
                item.TransferredBytes += read;
                TransferChanged?.Invoke(this, item);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task OnMessageReceivedAsync(PeerMessageEventArgs args)
    {
        switch (args.Message.Type)
        {
            case MessageType.FileMetadata:
                await HandleMetadataAsync(
                    args.Connection.Device.Id,
                    ProtocolSerializer.Deserialize<FileMetadataMessage>(args.Message),
                    _lifetimeCts.Token).ConfigureAwait(false);
                break;
            case MessageType.FileChunk:
                await HandleChunkAsync(args.Message.Payload, _lifetimeCts.Token).ConfigureAwait(false);
                break;
            case MessageType.FileComplete:
                await HandleCompleteAsync(ProtocolSerializer.Deserialize<FileCompleteMessage>(args.Message), _lifetimeCts.Token).ConfigureAwait(false);
                break;
            case MessageType.TransferCancel:
                await HandleCancelAsync(ProtocolSerializer.Deserialize<TransferCancelMessage>(args.Message)).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleMetadataAsync(Guid deviceId, FileMetadataMessage metadata, CancellationToken cancellationToken)
    {
        if (metadata.BatchId == Guid.Empty || metadata.TransferId == Guid.Empty || metadata.Size < 0)
        {
            throw new InvalidDataException("Invalid file metadata.");
        }

        var receiveRoot = Path.GetFullPath(_settings.ReceiveFolder);
        Directory.CreateDirectory(receiveRoot);

        IncomingBatch batch;
        if (metadata.IsRoot)
        {
            if (!string.Equals(Path.GetFileName(metadata.RelativePath), metadata.RelativePath, StringComparison.Ordinal) ||
                metadata.RelativePath is "." or "..")
            {
                throw new InvalidDataException("Invalid root item name.");
            }

            var desiredPath = PathSecurity.GetSafeDestination(receiveRoot, metadata.RelativePath);
            var destination = PathSecurity.GetAvailablePath(desiredPath);
            var item = new TransferItem(metadata.BatchId, Path.GetFileName(destination), true)
            {
                Status = TransferStatus.Transferring,
            };
            batch = new IncomingBatch(
                metadata.BatchId,
                deviceId,
                metadata.RelativePath,
                destination,
                metadata.IsDirectory,
                metadata.ExplorerPackageId,
                item);
            if (!_incomingBatches.TryAdd(metadata.BatchId, batch))
            {
                throw new InvalidDataException("Duplicate transfer batch.");
            }

            TransferAdded?.Invoke(this, item);
            await _log.InfoAsync($"Incoming transfer started: {metadata.BatchId}").ConfigureAwait(false);
        }
        else if (!_incomingBatches.TryGetValue(metadata.BatchId, out batch!))
        {
            throw new InvalidDataException("Metadata was received before the batch root.");
        }

        batch.Item.TotalBytes += metadata.IsDirectory ? 0 : metadata.Size;
        var destinationPath = ResolveEntryPath(receiveRoot, batch, metadata);
        if (metadata.IsDirectory)
        {
            Directory.CreateDirectory(destinationPath);
            TransferChanged?.Invoke(this, batch.Item);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + $".{metadata.TransferId:N}.crowpart";
        var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            _settings.ChunkSizeBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var incomingFile = new IncomingFile(metadata, destinationPath, temporaryPath, stream, batch);
        if (!_incomingFiles.TryAdd(metadata.TransferId, incomingFile))
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            File.Delete(temporaryPath);
            throw new InvalidDataException("Duplicate file transfer identifier.");
        }

        TransferChanged?.Invoke(this, batch.Item);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task HandleChunkAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var (transferId, data) = FileChunkMessage.Parse(payload);
        if (!_incomingFiles.TryGetValue(transferId, out var file))
        {
            throw new InvalidDataException("A file chunk arrived without metadata.");
        }

        if (file.BytesWritten + data.Length > file.Metadata.Size)
        {
            throw new InvalidDataException("Received file data exceeds the declared size.");
        }

        await file.Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        file.BytesWritten += data.Length;
        file.Batch.Item.TransferredBytes += data.Length;
        TransferChanged?.Invoke(this, file.Batch.Item);
    }

    private async Task HandleCompleteAsync(FileCompleteMessage message, CancellationToken cancellationToken)
    {
        if (message.IsBatchComplete)
        {
            if (_incomingBatches.TryRemove(message.BatchId, out var batch))
            {
                batch.Item.Status = TransferStatus.Completed;
                TransferChanged?.Invoke(this, batch.Item);
                await _log.InfoAsync($"Incoming transfer completed: {message.BatchId}").ConfigureAwait(false);
                if (batch.ExplorerPackageId != Guid.Empty)
                {
                    IncomingRootCompleted?.Invoke(
                        this,
                        new IncomingRootCompletedEventArgs(batch.DeviceId, batch.ExplorerPackageId, batch.RootDestination));
                }
            }

            return;
        }

        if (!_incomingFiles.TryRemove(message.TransferId, out var file))
        {
            // Directory entries do not own a stream and require no finalization.
            return;
        }

        try
        {
            await file.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await file.Stream.DisposeAsync().ConfigureAwait(false);
            if (file.BytesWritten != file.Metadata.Size)
            {
                throw new InvalidDataException($"File size mismatch. Expected {file.Metadata.Size}, received {file.BytesWritten}.");
            }

            File.Move(file.TemporaryPath, file.DestinationPath, false);
            File.SetLastWriteTimeUtc(file.DestinationPath, file.Metadata.LastWriteTime.UtcDateTime);
        }
        catch
        {
            await file.Stream.DisposeAsync().ConfigureAwait(false);
            TryDelete(file.TemporaryPath);
            throw;
        }
    }

    private async Task HandleCancelAsync(TransferCancelMessage message)
    {
        if (_incomingBatches.TryRemove(message.BatchId, out var batch))
        {
            batch.Item.Status = TransferStatus.Cancelled;
            batch.Item.ErrorMessage = message.Reason;
            TransferChanged?.Invoke(this, batch.Item);
        }

        foreach (var entry in _incomingFiles.Where(entry => entry.Value.Metadata.BatchId == message.BatchId).ToArray())
        {
            if (_incomingFiles.TryRemove(entry.Key, out var file))
            {
                await file.Stream.DisposeAsync().ConfigureAwait(false);
                TryDelete(file.TemporaryPath);
            }
        }
    }

    private void OnDeviceDisconnected(object? sender, DeviceInfo device) => _ = CleanupDisconnectedDeviceAsync(device.Id);

    private async Task CleanupDisconnectedDeviceAsync(Guid deviceId)
    {
        foreach (var batchEntry in _incomingBatches.Where(entry => entry.Value.DeviceId == deviceId).ToArray())
        {
            if (!_incomingBatches.TryRemove(batchEntry.Key, out var batch))
            {
                continue;
            }

            batch.Item.ErrorMessage = "연결이 끊어졌습니다.";
            batch.Item.Status = TransferStatus.Failed;
            TransferChanged?.Invoke(this, batch.Item);

            foreach (var fileEntry in _incomingFiles.Where(entry => entry.Value.Metadata.BatchId == batch.BatchId).ToArray())
            {
                if (_incomingFiles.TryRemove(fileEntry.Key, out var file))
                {
                    await file.Stream.DisposeAsync().ConfigureAwait(false);
                    TryDelete(file.TemporaryPath);
                }
            }
        }
    }

    private static string ResolveEntryPath(string receiveRoot, IncomingBatch batch, FileMetadataMessage metadata)
    {
        if (metadata.IsRoot)
        {
            return batch.RootDestination;
        }

        var relative = metadata.RelativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var rootPrefix = batch.OriginalRootName + Path.DirectorySeparatorChar;
        if (!relative.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Entry is outside its declared transfer root.");
        }

        var suffix = relative[rootPrefix.Length..];
        var candidate = Path.Combine(batch.RootDestination, suffix);
        var safeCandidate = PathSecurity.GetSafeDestination(receiveRoot, Path.GetRelativePath(receiveRoot, candidate));
        var rootWithSeparator = Path.GetFullPath(batch.RootDestination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!safeCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Entry escapes its transfer root.");
        }

        return safeCandidate;
    }

    private static IEnumerable<TransferEntry> EnumerateEntries(string rootPath)
    {
        var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (File.Exists(rootPath))
        {
            var file = new FileInfo(rootPath);
            yield return new TransferEntry(file.FullName, rootName, file.Length, file.LastWriteTimeUtc, false, true);
            yield break;
        }

        var root = new DirectoryInfo(rootPath);
        yield return new TransferEntry(root.FullName, rootName, 0, root.LastWriteTimeUtc, true, true);
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var childDirectory in directory.EnumerateDirectories().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((childDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relative = Path.Combine(rootName, Path.GetRelativePath(root.FullName, childDirectory.FullName));
                yield return new TransferEntry(childDirectory.FullName, relative, 0, childDirectory.LastWriteTimeUtc, true, false);
                pending.Push(childDirectory);
            }

            foreach (var childFile in directory.EnumerateFiles().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((childFile.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var relative = Path.Combine(rootName, Path.GetRelativePath(root.FullName, childFile.FullName));
                yield return new TransferEntry(childFile.FullName, relative, childFile.Length, childFile.LastWriteTimeUtc, false, false);
            }
        }
    }

    private static async Task TrySendCancelAsync(PeerConnection connection, Guid batchId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendJsonAsync(MessageType.TransferCancel, new TransferCancelMessage(batchId, reason), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The original transfer error is more useful than a secondary cancel-send failure.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _connections.MessageReceived -= OnMessageReceivedAsync;
        _connections.DeviceDisconnected -= OnDeviceDisconnected;
        await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        foreach (var file in _incomingFiles.Values)
        {
            await file.Stream.DisposeAsync().ConfigureAwait(false);
            TryDelete(file.TemporaryPath);
        }

        _incomingFiles.Clear();
        _incomingBatches.Clear();
        foreach (var session in _outgoingSessions.Values)
        {
            await session.Cancellation.CancelAsync().ConfigureAwait(false);
        }

        _outgoingSessions.Clear();
        _lifetimeCts.Dispose();
    }

    private sealed record TransferEntry(
        string FullPath,
        string RelativePath,
        long Size,
        DateTimeOffset LastWriteTime,
        bool IsDirectory,
        bool IsRoot);

    private sealed record IncomingBatch(
        Guid BatchId,
        Guid DeviceId,
        string OriginalRootName,
        string RootDestination,
        bool IsDirectory,
        Guid ExplorerPackageId,
        TransferItem Item);

    private sealed class IncomingFile
    {
        public IncomingFile(
            FileMetadataMessage metadata,
            string destinationPath,
            string temporaryPath,
            FileStream stream,
            IncomingBatch batch)
        {
            Metadata = metadata;
            DestinationPath = destinationPath;
            TemporaryPath = temporaryPath;
            Stream = stream;
            Batch = batch;
        }

        public FileMetadataMessage Metadata { get; }
        public string DestinationPath { get; }
        public string TemporaryPath { get; }
        public FileStream Stream { get; }
        public IncomingBatch Batch { get; }
        public long BytesWritten { get; set; }
    }
}
