namespace CrowLink.Services.RemoteMouse;

public static class KeyboardInputPolicy
{
    public const ushort Hangul = 0x15;
    public const ushort ImeOn = 0x16;
    public const ushort Hanja = 0x19;
    public const ushort ImeOff = 0x1A;
    public const ushort ProcessKey = 0xE5;

    public static bool RequiresVirtualKey(ushort virtualKey) =>
        virtualKey is >= Hangul and <= ImeOff or ProcessKey;
}
