using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace CrowLink.Protocol;

public sealed class ProtocolSerializer : IAsyncDisposable
{
    public const int ProtocolVersion = 5;
    public const int HeaderSize = 5;
    public const int MaxPayloadSize = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ProtocolSerializer(Stream stream) => _stream = stream;

    public async Task WriteJsonAsync<T>(MessageType type, T payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await WriteAsync(type, bytes, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > MaxPayloadSize)
        {
            throw new InvalidDataException($"Payload exceeds {MaxPayloadSize} bytes.");
        }

        var header = new byte[HeaderSize];
        header[0] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), payload.Length);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            }

            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<ProtocolMessage> ReadAsync(CancellationToken cancellationToken)
    {
        var header = new byte[HeaderSize];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
        if (length < 0 || length > MaxPayloadSize)
        {
            throw new InvalidDataException($"Invalid payload length: {length}.");
        }

        var payload = new byte[length];
        if (length > 0)
        {
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new ProtocolMessage((MessageType)header[0], payload);
    }

    public static T Deserialize<T>(ProtocolMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, JsonOptions)
        ?? throw new InvalidDataException($"The {message.Type} payload is empty or invalid.");

    public async ValueTask DisposeAsync()
    {
        _writeGate.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
