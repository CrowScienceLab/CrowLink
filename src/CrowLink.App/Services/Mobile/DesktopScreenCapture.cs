using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrowLink.Protocol;

namespace CrowLink.Services.Mobile;

internal static class DesktopScreenCapture
{
    public static string Capture(MonitorDescriptorMessage monitor)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var width = Math.Min(monitor.Width, 1280);
        var height = Math.Max(1, (int)Math.Round(monitor.Height * (double)width / monitor.Width));
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        if (screen == 0 || memory == 0 || bitmap == 0)
        {
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
            throw new Win32Exception("화면 캡처를 만들 수 없습니다.");
        }
        var previous = SelectObject(memory, bitmap);
        try
        {
            SetStretchBltMode(memory, 4);
            if (!StretchBlt(memory, 0, 0, width, height, screen, monitor.X, monitor.Y,
                monitor.Width, monitor.Height, 0x40CC0020)) throw new Win32Exception("화면 캡처 실패");
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(nint target, int x, int y, int width, int height,
        nint source, int sx, int sy, int swidth, int sheight, uint operation);
}
