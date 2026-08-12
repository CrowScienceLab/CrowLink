using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CrowLink.Services.RemoteMouse;

internal sealed class GlobalMouseTracker : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WhKeyboardLl = 13;
    private const int WmQuit = 0x0012;
    private const int WmHotkey = 0x0312;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlMouseInjected = 0x00000001;
    private const uint LlKeyboardExtended = 0x00000001;
    private const uint LlKeyboardInjected = 0x00000010;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkEscape = 0x1B;
    private const int EmergencyHotkeyId = 0xC011;

    private readonly MouseTransitionEdge _edge;
    private readonly int _remoteHeight;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly NativeMethods.LowLevelMouseProc _hookCallback;
    private readonly NativeMethods.LowLevelMouseProc _keyboardCallback;
    private readonly HashSet<ushort> _pressedKeys = [];
    private readonly HashSet<ushort> _blockedKeys = [];
    private Thread? _thread;
    private MouseBoundaryTracker? _boundary;
    private nint _hook;
    private nint _keyboardHook;
    private uint _threadId;
    private Exception? _startError;
    private bool _wasAtEdge;
    private bool _ignoreCenteredMove;
    private int _centerX;
    private int _centerY;

    public GlobalMouseTracker(MouseTransitionEdge edge, int remoteHeight)
    {
        _edge = edge;
        _remoteHeight = Math.Max(1, remoteHeight);
        _hookCallback = HookCallback;
        _keyboardCallback = KeyboardHookCallback;
    }

    public event Action<double, double>? Move;
    public event Action<string, bool>? Button;
    public event Action<int>? Wheel;
    public event Action<bool>? RemoteModeChanged;
    public event Action? EmergencyReleased;
    public event Action<ushort, ushort, bool, bool>? Keyboard;
    public event Action? SecureShortcutBlocked;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "CrowLink global mouse tracker",
        };
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new TimeoutException("전역 마우스 추적기를 시작하지 못했습니다.");
        }

        if (_startError is not null)
        {
            throw new InvalidOperationException("전역 마우스 추적기를 시작하지 못했습니다.", _startError);
        }
    }

    public void ExitRemoteMode()
    {
        if (_boundary?.IsRemote != true)
        {
            return;
        }

        _boundary.Exit();
        _pressedKeys.Clear();
        _blockedKeys.Clear();
        RestoreCursorToEntryEdge();
        RemoteModeChanged?.Invoke(false);
    }

    private void RunMessageLoop()
    {
        try
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            _boundary = new MouseBoundaryTracker(_edge);
            _hook = NativeMethods.SetWindowsHookEx(WhMouseLl, _hookCallback, NativeMethods.GetModuleHandle(null), 0);
            if (_hook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _keyboardHook = NativeMethods.SetWindowsHookEx(WhKeyboardLl, _keyboardCallback, NativeMethods.GetModuleHandle(null), 0);
            if (_keyboardHook == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!NativeMethods.RegisterHotKey(0, EmergencyHotkeyId, ModControl | ModAlt, VkEscape))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            _started.Set();
            while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Message == WmHotkey && message.WParam == EmergencyHotkeyId)
                {
                    ExitRemoteMode();
                    EmergencyReleased?.Invoke();
                }
            }
        }
        catch (Exception exception)
        {
            _startError = exception;
            _started.Set();
        }
        finally
        {
            NativeMethods.UnregisterHotKey(0, EmergencyHotkeyId);
            if (_hook != 0)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = 0;
            }


            if (_keyboardHook != 0)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = 0;
            }
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0 || _boundary is null)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
        if ((data.Flags & LlMouseInjected) != 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        if (!_boundary.IsRemote)
        {
            if (message != WmMouseMove)
            {
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            }

            var left = NativeMethods.GetSystemMetrics(76);
            var top = NativeMethods.GetSystemMetrics(77);
            var width = Math.Max(1, NativeMethods.GetSystemMetrics(78));
            var height = Math.Max(1, NativeMethods.GetSystemMetrics(79));
            var atEdge = _edge == MouseTransitionEdge.Right
                ? data.Point.X >= left + width - 1
                : data.Point.X <= left;
            if (_boundary.TryEnter(data.Point.X, data.Point.Y, left, top, width, height, _wasAtEdge))
            {
                _centerX = left + (width / 2);
                _centerY = top + (height / 2);
                _ignoreCenteredMove = true;
                NativeMethods.SetCursorPos(_centerX, _centerY);
                RemoteModeChanged?.Invoke(true);
                Move?.Invoke(_boundary.X, _boundary.Y);
                _wasAtEdge = true;
                return 1;
            }

            _wasAtEdge = atEdge;
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        if (message == WmMouseMove)
        {
            if (_ignoreCenteredMove && data.Point.X == _centerX && data.Point.Y == _centerY)
            {
                _ignoreCenteredMove = false;
                return 1;
            }

            var exited = _boundary.ApplyDelta(data.Point.X - _centerX, data.Point.Y - _centerY, _remoteHeight);
            if (exited)
            {
                RestoreCursorToEntryEdge();
                _pressedKeys.Clear();
                _blockedKeys.Clear();
                RemoteModeChanged?.Invoke(false);
                return 1;
            }

            Move?.Invoke(_boundary.X, _boundary.Y);
            _ignoreCenteredMove = true;
            NativeMethods.SetCursorPos(_centerX, _centerY);
            return 1;
        }

        if (TryGetButton(message, out var button, out var isDown))
        {
            Button?.Invoke(button, isDown);
            return 1;
        }

        if (message == WmMouseWheel)
        {
            Wheel?.Invoke(unchecked((short)(data.MouseData >> 16)));
            return 1;
        }

        return 1;
    }

    private nint KeyboardHookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0 || _boundary?.IsRemote != true)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if ((data.Flags & LlKeyboardInjected) != 0)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        var isDown = message is WmKeyDown or WmSysKeyDown;
        var isUp = message is WmKeyUp or WmSysKeyUp;
        if (!isDown && !isUp)
        {
            return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }

        var virtualKey = unchecked((ushort)data.VirtualKey);
        var scanCode = unchecked((ushort)data.ScanCode);
        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
        }

        if (ShortcutPolicy.IsEmergencyRelease(_pressedKeys, virtualKey, isDown))
        {
            ExitRemoteMode();
            EmergencyReleased?.Invoke();
            return 1;
        }

        if (ShortcutPolicy.IsSecureAttentionSequence(_pressedKeys, virtualKey, isDown))
        {
            _blockedKeys.Add(virtualKey);
            SecureShortcutBlocked?.Invoke();
            return 1;
        }

        if (isUp && _blockedKeys.Remove(virtualKey))
        {
            _pressedKeys.Remove(virtualKey);
            return 1;
        }

        Keyboard?.Invoke(
            virtualKey,
            scanCode,
            isDown,
            (data.Flags & LlKeyboardExtended) != 0);
        if (isUp)
        {
            _pressedKeys.Remove(virtualKey);
        }

        return 1;
    }

    private static bool TryGetButton(int message, out string button, out bool isDown)
    {
        (button, isDown) = message switch
        {
            WmLButtonDown => ("Left", true),
            WmLButtonUp => ("Left", false),
            WmRButtonDown => ("Right", true),
            WmRButtonUp => ("Right", false),
            WmMButtonDown => ("Middle", true),
            WmMButtonUp => ("Middle", false),
            _ => (string.Empty, false),
        };
        return button.Length > 0;
    }

    private void RestoreCursorToEntryEdge()
    {
        var left = NativeMethods.GetSystemMetrics(76);
        var top = NativeMethods.GetSystemMetrics(77);
        var width = Math.Max(1, NativeMethods.GetSystemMetrics(78));
        var height = Math.Max(1, NativeMethods.GetSystemMetrics(79));
        var x = _edge == MouseTransitionEdge.Right ? left + width - 2 : left + 1;
        var y = top + (int)Math.Round((_boundary?.Y ?? 0.5d) * Math.Max(1, height - 1));
        _wasAtEdge = true;
        NativeMethods.SetCursorPos(x, y);
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, WmQuit, 0, 0);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _started.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint HWnd;
        public int Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    private static class NativeMethods
    {
        public delegate nint LowLevelMouseProc(int code, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookEx(int hookId, LowLevelMouseProc callback, nint module, uint threadId);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(nint hook);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint window, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(string? moduleName);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetCursorPos(int x, int y);
    }
}
