namespace CrowLink.Protocol;

public sealed record ExplorerDragItemDescriptor(string Name, bool IsDirectory, long Size);

public sealed record ExplorerDragOfferMessage(
    Guid PackageId,
    IReadOnlyList<ExplorerDragItemDescriptor> Items);

public sealed record ExplorerDragResponseMessage(Guid PackageId);

public sealed record ExplorerDragReadyMessage(Guid PackageId);

public sealed record ExplorerDragAbortMessage(Guid PackageId, string Reason);
