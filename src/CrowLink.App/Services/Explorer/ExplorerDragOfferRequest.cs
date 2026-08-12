using CrowLink.Protocol;
using CrowLink.Services.Network;

namespace CrowLink.Services.Explorer;

public sealed record ExplorerDragOfferRequest(
    PeerConnection Connection,
    Guid PackageId,
    IReadOnlyList<ExplorerDragItemDescriptor> Items);
