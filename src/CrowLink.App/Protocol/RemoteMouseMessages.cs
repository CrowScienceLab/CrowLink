namespace CrowLink.Protocol;

public sealed record MonitorInfoMessage(
    int VirtualWidth,
    int VirtualHeight,
    int MonitorCount,
    IReadOnlyList<MonitorDescriptorMessage> Monitors);

public sealed record MonitorDescriptorMessage(
    string DeviceName,
    int X,
    int Y,
    int Width,
    int Height,
    int WorkX,
    int WorkY,
    int WorkWidth,
    int WorkHeight,
    uint DpiX,
    uint DpiY,
    bool IsPrimary);

public sealed record MouseControlRequestMessage(Guid SessionId, string EntryEdge);

public sealed record MouseControlResponseMessage(Guid SessionId);

public sealed record MouseMoveMessage(Guid SessionId, double X, double Y);

public sealed record MouseButtonMessage(Guid SessionId, string Button, bool IsDown);

public sealed record MouseWheelMessage(Guid SessionId, int Delta);

public sealed record MouseControlStopMessage(Guid SessionId);

public sealed record KeyboardInputMessage(
    Guid SessionId,
    ushort VirtualKey,
    ushort ScanCode,
    bool IsDown,
    bool IsExtended);

public sealed record KeyboardResetMessage(Guid SessionId);
