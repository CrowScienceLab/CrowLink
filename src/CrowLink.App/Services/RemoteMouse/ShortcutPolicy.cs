namespace CrowLink.Services.RemoteMouse;

public static class ShortcutPolicy
{
    public const ushort Escape = 0x1B;
    public const ushort Delete = 0x2E;
    public const ushort Control = 0x11;
    public const ushort LeftControl = 0xA2;
    public const ushort RightControl = 0xA3;
    public const ushort Alt = 0x12;
    public const ushort LeftAlt = 0xA4;
    public const ushort RightAlt = 0xA5;

    public static bool IsEmergencyRelease(IReadOnlySet<ushort> pressedKeys, ushort virtualKey, bool isDown) =>
        isDown && virtualKey == Escape && HasControl(pressedKeys) && HasAlt(pressedKeys);

    public static bool IsSecureAttentionSequence(IReadOnlySet<ushort> pressedKeys, ushort virtualKey, bool isDown) =>
        isDown && virtualKey == Delete && HasControl(pressedKeys) && HasAlt(pressedKeys);

    private static bool HasControl(IReadOnlySet<ushort> keys) =>
        keys.Contains(Control) || keys.Contains(LeftControl) || keys.Contains(RightControl);

    private static bool HasAlt(IReadOnlySet<ushort> keys) =>
        keys.Contains(Alt) || keys.Contains(LeftAlt) || keys.Contains(RightAlt);
}
