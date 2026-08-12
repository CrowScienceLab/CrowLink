using CrowLink.Services.Network;

namespace CrowLink.Services.Clipboard;

public sealed record ClipboardContentReceivedEventArgs(
    PeerConnection Connection,
    ClipboardContentKind Kind,
    string? Text,
    byte[]? ImagePng);
