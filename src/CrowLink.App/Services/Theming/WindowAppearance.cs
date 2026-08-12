using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CrowLink.Services.Theming;

public static class WindowAppearance
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void ApplyFrame(Window window, bool bright)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return;
        }

        var enabled = bright ? 0 : 1;
        if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
        }

        var border = bright ? ToColorRef(0xA7, 0xD7, 0xEA) : ToColorRef(0x1E, 0x24, 0x2D);
        var caption = bright ? ToColorRef(0xEA, 0xF8, 0xFF) : ToColorRef(0x05, 0x06, 0x08);
        var text = bright ? ToColorRef(0x15, 0x38, 0x4A) : ToColorRef(0xF2, 0xF5, 0xF8);
        _ = DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
    }

    private static int ToColorRef(byte red, byte green, byte blue) => red | green << 8 | blue << 16;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
