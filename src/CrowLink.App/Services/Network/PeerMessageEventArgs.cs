using CrowLink.Protocol;

namespace CrowLink.Services.Network;

public sealed record PeerMessageEventArgs(PeerConnection Connection, ProtocolMessage Message);
