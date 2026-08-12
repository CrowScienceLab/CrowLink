using System.Buffers.Binary;

namespace CrowLink.Protocol;

public static class FileChunkMessage
{
    private const int TransferIdSize = 16;

    public static byte[] CreatePayload(Guid transferId, ReadOnlySpan<byte> data)
    {
        var payload = new byte[TransferIdSize + data.Length];
        transferId.TryWriteBytes(payload);
        data.CopyTo(payload.AsSpan(TransferIdSize));
        return payload;
    }

    public static (Guid TransferId, ReadOnlyMemory<byte> Data) Parse(byte[] payload)
    {
        if (payload.Length < TransferIdSize)
        {
            throw new InvalidDataException("FILE_CHUNK payload is too short.");
        }

        return (new Guid(payload.AsSpan(0, TransferIdSize)), payload.AsMemory(TransferIdSize));
    }
}
