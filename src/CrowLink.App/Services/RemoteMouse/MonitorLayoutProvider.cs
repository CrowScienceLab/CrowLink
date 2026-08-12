using System.Runtime.InteropServices;
using CrowLink.Protocol;

namespace CrowLink.Services.RemoteMouse;

internal static class MonitorLayoutProvider
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int EffectiveDpi = 0;

    public static MonitorInfoMessage GetMonitorInfo()
    {
        var monitors = new List<MonitorDescriptorMessage>();
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var dpiX = 96u;
            var dpiY = 96u;
            try
            {
                _ = NativeMethods.GetDpiForMonitor(monitor, EffectiveDpi, out dpiX, out dpiY);
            }
            catch (DllNotFoundException)
            {
                dpiX = dpiY = 96;
            }
            catch (EntryPointNotFoundException)
            {
                dpiX = dpiY = 96;
            }

            monitors.Add(new MonitorDescriptorMessage(
                info.DeviceName ?? string.Empty,
                info.Monitor.Left,
                info.Monitor.Top,
                Math.Max(1, info.Monitor.Right - info.Monitor.Left),
                Math.Max(1, info.Monitor.Bottom - info.Monitor.Top),
                info.Work.Left,
                info.Work.Top,
                Math.Max(1, info.Work.Right - info.Work.Left),
                Math.Max(1, info.Work.Bottom - info.Work.Top),
                Math.Max(96u, dpiX),
                Math.Max(96u, dpiY),
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0) || monitors.Count == 0)
        {
            var width = Math.Max(1, NativeMethods.GetSystemMetrics(78));
            var height = Math.Max(1, NativeMethods.GetSystemMetrics(79));
            monitors.Add(new MonitorDescriptorMessage(
                "DISPLAY",
                NativeMethods.GetSystemMetrics(76),
                NativeMethods.GetSystemMetrics(77),
                width,
                height,
                NativeMethods.GetSystemMetrics(76),
                NativeMethods.GetSystemMetrics(77),
                width,
                height,
                96,
                96,
                true));
        }

        var left = monitors.Min(item => item.X);
        var top = monitors.Min(item => item.Y);
        var right = monitors.Max(item => item.X + item.Width);
        var bottom = monitors.Max(item => item.Y + item.Height);
        return new MonitorInfoMessage(
            Math.Max(1, right - left),
            Math.Max(1, bottom - top),
            monitors.Count,
            monitors);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    private static class NativeMethods
    {
        public delegate bool MonitorEnumProc(nint monitor, nint deviceContext, nint monitorRect, nint data);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(nint deviceContext, nint clipRect, MonitorEnumProc callback, nint data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);
    }
}
