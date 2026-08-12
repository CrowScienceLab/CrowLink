namespace CrowLink.Protocol;

public sealed record FileMetadataMessage(
    Guid BatchId,
    Guid TransferId,
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTime,
    bool IsDirectory,
    bool IsRoot,
    Guid ExplorerPackageId = default);

public sealed record FileCompleteMessage(Guid BatchId, Guid TransferId, bool IsBatchComplete = false);

public sealed record TransferCancelMessage(Guid BatchId, string? Reason);

public sealed record ErrorMessage(string Code, string Message);
