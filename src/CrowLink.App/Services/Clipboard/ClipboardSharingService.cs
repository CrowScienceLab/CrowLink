using CrowLink.Protocol;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;

namespace CrowLink.Services.Clipboard;

public sealed class ClipboardSharingService : IAsyncDisposable
{
    public const int MaxTextCharacters = 1_000_000;
    public const int MaxImageBytes = 7 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly ConnectionService _connections;
    private readonly LogService _log;

    public ClipboardSharingService(ConnectionService connections, LogService log)
    {
        _connections = connections;
        _log = log;
        _connections.MessageReceived += OnMessageReceivedAsync;
    }

    public event Func<ClipboardContentReceivedEventArgs, Task<bool>>? ContentReceived;

    public async Task SendTextAsync(PeerConnection connection, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("클립보드에 보낼 텍스트가 없습니다.");
        }

        if (text.Length > MaxTextCharacters)
        {
            throw new InvalidDataException($"클립보드 텍스트는 {MaxTextCharacters:N0}자를 넘을 수 없습니다.");
        }

        await connection.SendJsonAsync(
            MessageType.ClipboardText,
            new ClipboardTextMessage(text),
            cancellationToken).ConfigureAwait(false);
        await _log.InfoAsync($"Text clipboard sent: {text.Length} characters").ConfigureAwait(false);
    }

    public async Task SendImageAsync(PeerConnection connection, byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        ValidatePng(pngBytes);
        await connection.SendAsync(MessageType.ClipboardImage, pngBytes, cancellationToken).ConfigureAwait(false);
        await _log.InfoAsync($"Image clipboard sent: {pngBytes.Length} bytes").ConfigureAwait(false);
    }

    private async Task OnMessageReceivedAsync(PeerMessageEventArgs args)
    {
        ClipboardContentReceivedEventArgs content;
        switch (args.Message.Type)
        {
            case MessageType.ClipboardText:
                var textMessage = ProtocolSerializer.Deserialize<ClipboardTextMessage>(args.Message);
                if (string.IsNullOrEmpty(textMessage.Text) || textMessage.Text.Length > MaxTextCharacters)
                {
                    throw new InvalidDataException("Invalid clipboard text payload.");
                }

                content = new ClipboardContentReceivedEventArgs(
                    args.Connection,
                    ClipboardContentKind.Text,
                    textMessage.Text,
                    null);
                break;
            case MessageType.ClipboardImage:
                ValidatePng(args.Message.Payload);
                content = new ClipboardContentReceivedEventArgs(
                    args.Connection,
                    ClipboardContentKind.Image,
                    null,
                    args.Message.Payload);
                break;
            default:
                return;
        }

        var handlers = ContentReceived?.GetInvocationList()
            .Cast<Func<ClipboardContentReceivedEventArgs, Task<bool>>>()
            .ToArray();
        if (handlers is null || handlers.Length == 0)
        {
            await _log.WarningAsync("Clipboard content rejected because no approval UI is available").ConfigureAwait(false);
            return;
        }

        var accepted = true;
        foreach (var handler in handlers)
        {
            accepted &= await handler(content).ConfigureAwait(false);
        }

        await _log.InfoAsync(accepted
            ? $"{content.Kind} clipboard accepted"
            : $"{content.Kind} clipboard rejected").ConfigureAwait(false);
    }

    private static void ValidatePng(byte[] pngBytes)
    {
        if (pngBytes.Length == 0 || pngBytes.Length > MaxImageBytes ||
            pngBytes.Length < PngSignature.Length ||
            !pngBytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException($"클립보드 이미지는 {MaxImageBytes / 1024 / 1024}MB 이하의 PNG여야 합니다.");
        }
    }

    public ValueTask DisposeAsync()
    {
        _connections.MessageReceived -= OnMessageReceivedAsync;
        return ValueTask.CompletedTask;
    }
}
