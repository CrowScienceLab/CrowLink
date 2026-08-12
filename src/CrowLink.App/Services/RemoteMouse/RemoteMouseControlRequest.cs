using CrowLink.Services.Network;

namespace CrowLink.Services.RemoteMouse;

public sealed record RemoteMouseControlRequest(
    PeerConnection Connection,
    Guid SessionId,
    MouseTransitionEdge EntryEdge);
