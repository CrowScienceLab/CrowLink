using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CrowLink.Services.Mobile;

internal sealed class MobileWebSocket(Stream stream) : IAsyncDisposable
{
    private const int MaximumPayloadBytes = 16 * 1024;
    private readonly Stream _stream = stream;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public static async Task<MobileWebSocket?> AcceptAsync(
        Stream stream,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        if (!headers.TryGetValue("Sec-WebSocket-Key", out var key) || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var acceptBytes = SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
        var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                       "Upgrade: websocket\r\n" +
                       "Connection: Upgrade\r\n" +
                       $"Sec-WebSocket-Accept: {Convert.ToBase64String(acceptBytes)}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken).ConfigureAwait(false);
        return new MobileWebSocket(stream);
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var header = new byte[2];
            if (!await ReadExactlyOrEndAsync(header, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var final = (header[0] & 0x80) != 0;
            var opcode = header[0] & 0x0F;
            var masked = (header[1] & 0x80) != 0;
            ulong length = (uint)(header[1] & 0x7F);
            if (!final || !masked)
            {
                throw new InvalidDataException("Fragmented or unmasked WebSocket frame.");
            }

            if (length == 126)
            {
                var extended = new byte[2];
                await ReadExactlyAsync(extended, cancellationToken).ConfigureAwait(false);
                length = BinaryPrimitives.ReadUInt16BigEndian(extended);
            }
            else if (length == 127)
            {
                var extended = new byte[8];
                await ReadExactlyAsync(extended, cancellationToken).ConfigureAwait(false);
                length = BinaryPrimitives.ReadUInt64BigEndian(extended);
            }

            if (length > MaximumPayloadBytes)
            {
                throw new InvalidDataException("WebSocket frame is too large.");
            }

            var mask = new byte[4];
            await ReadExactlyAsync(mask, cancellationToken).ConfigureAwait(false);
            var payload = new byte[(int)length];
            await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < payload.Length; index++)
            {
                payload[index] ^= mask[index & 3];
            }

            switch (opcode)
            {
                case 0x1:
                    return Encoding.UTF8.GetString(payload);
                case 0x8:
                    await SendFrameAsync(0x8, payload, cancellationToken).ConfigureAwait(false);
                    return null;
                case 0x9:
                    await SendFrameAsync(0xA, payload, cancellationToken).ConfigureAwait(false);
                    break;
                case 0xA:
                    break;
                default:
                    throw new InvalidDataException("Unsupported WebSocket frame.");
            }
        }
    }

    public Task SendTextAsync(string value, CancellationToken cancellationToken) =>
        SendFrameAsync(0x1, Encoding.UTF8.GetBytes(value), cancellationToken);

    private async Task SendFrameAsync(byte opcode, byte[] payload, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var buffer = new MemoryStream(payload.Length + 10);
            buffer.WriteByte((byte)(0x80 | opcode));
            if (payload.Length < 126)
            {
                buffer.WriteByte((byte)payload.Length);
            }
            else
            {
                buffer.WriteByte(126);
                Span<byte> lengthBytes = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, (ushort)payload.Length);
                buffer.Write(lengthBytes);
            }

            buffer.Write(payload);
            await _stream.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<bool> ReadExactlyOrEndAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (!await ReadExactlyOrEndAsync(buffer, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sendGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
