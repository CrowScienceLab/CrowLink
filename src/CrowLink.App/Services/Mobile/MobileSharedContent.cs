using System.Buffers.Binary;

namespace CrowLink.Services.Mobile;

public sealed record MobileSharedContent(Guid SessionId, string? Text, byte[]? Png);

public static class MobileContentLimits
{
    public const int TextCharacters = 100_000;
    public const int ImageBytes = 2 * 1024 * 1024;
    public const int MessageBytes = 3 * 1024 * 1024;

    public static bool IsAllowedPng(byte[] bytes)
    {
        if (bytes.Length is < 33 or > ImageBytes ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return false;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        return width is > 0 and <= 8192 && height is > 0 and <= 8192 && (ulong)width * height <= 16_000_000;
    }
}
