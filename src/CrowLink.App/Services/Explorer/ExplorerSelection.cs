using System.Runtime.InteropServices;

namespace CrowLink.Services.Explorer;

internal static class ExplorerSelection
{
    public static string? GetSingleSelectedFile()
    {
        object? shellObject = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return null;
            shellObject = Activator.CreateInstance(type);
            dynamic shell = shellObject!;
            var foreground = GetForegroundWindow();
            foreach (dynamic window in shell.Windows())
            {
                if ((nint)(long)window.HWND != foreground) continue;
                dynamic items = window.Document.SelectedItems();
                if ((int)items.Count != 1) return null;
                string path = items.Item(0).Path;
                return File.Exists(path) && QuickTransferPolicy.IsAllowed(Path.GetFileName(path)) ? path : null;
            }
            return null;
        }
        catch (Exception exception) when (exception is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        { return null; }
        finally { if (shellObject is not null && Marshal.IsComObject(shellObject)) Marshal.ReleaseComObject(shellObject); }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
