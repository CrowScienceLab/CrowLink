namespace CrowLink.Protocol;

public sealed record ProtocolMessage(MessageType Type, byte[] Payload);
