using System.ComponentModel;
using System.Runtime.InteropServices;
using CrowLink.Protocol;

namespace CrowLink.Services.RemoteMouse;

internal static class MouseInputInjector
{
    private const uint InputMouse = 0;
    private const uint Move = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;
    private const uint Wheel = 0x0800;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;
    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyScanCode = 0x0008;

    public static MonitorInfoMessage GetMonitorInfo() => MonitorLayoutProvider.GetMonitorInfo();

    public static void MoveTo(double x, double y)
    {
        Send(new MouseInput
        {
            Dx = (int)Math.Round(Math.Clamp(x, 0d, 1d) * 65535d),
            Dy = (int)Math.Round(Math.Clamp(y, 0d, 1d) * 65535d),
            Flags = Move | Absolute | VirtualDesk,
        });
    }

    public static void Button(string button, bool isDown)
    {
        var flags = (button, isDown) switch
        {
            ("Left", true) => LeftDown,
            ("Left", false) => LeftUp,
            ("Right", true) => RightDown,
            ("Right", false) => RightUp,
            ("Middle", true) => MiddleDown,
            ("Middle", false) => MiddleUp,
            _ => throw new InvalidDataException("Unsupported mouse button."),
        };
        Send(new MouseInput { Flags = flags });
    }

    public static void WheelBy(int delta) => Send(new MouseInput
    {
        MouseData = unchecked((uint)delta),
        Flags = Wheel,
    });

    public static void Keyboard(ushort virtualKey, ushort scanCode, bool isDown, bool isExtended)
    {
        var flags = isDown ? 0u : KeyUp;
        if (isExtended)
        {
            flags |= KeyExtended;
        }

        var keyboardInput = new KeyboardInput
        {
            VirtualKey = virtualKey,
            ScanCode = scanCode,
            Flags = flags,
        };
        // IME keys such as VK_HANGUL carry semantic virtual-key meaning. Converting
        // them to KEYEVENTF_SCANCODE drops the Hangul/English mode transition.
        if (scanCode != 0 && !KeyboardInputPolicy.RequiresVirtualKey(virtualKey))
        {
            keyboardInput.VirtualKey = 0;
            keyboardInput.Flags |= KeyScanCode;
        }

        Send(keyboardInput);
    }

    private static void Send(MouseInput mouseInput)
    {
        var input = new Input { Type = InputMouse, Data = new InputUnion { Mouse = mouseInput } };
        Send(input);
    }

    private static void Send(KeyboardInput keyboardInput)
    {
        var input = new Input { Type = 1, Data = new InputUnion { Keyboard = keyboardInput } };
        Send(input);
    }

    private static void Send(Input input)
    {
        if (NativeMethods.SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint inputCount, [In] Input[] inputs, int inputSize);
    }
}
