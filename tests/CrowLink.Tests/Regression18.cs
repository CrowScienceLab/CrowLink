using System.IO;
using System.Windows;
using System.Windows.Controls;
using CrowLink.Services;
using CrowLink.Services.Explorer;
using CrowLink.Services.Settings;
using CrowLink.Services.Theming;

internal static class Regression18
{
    public static Task PolicyAsync()
    {
        foreach (var name in new[] { "a.pdf", "자료.hwp", "photo.JPG", "movie.mp4", "archive.zip" })
            if (!QuickTransferPolicy.IsAllowed(name)) throw new Exception("Allowed type rejected: " + name);
        foreach (var name in new[] { "a.exe", "a.pdf.exe", "a.lnk", "a.ps1", "a.js", "a.docm", "../a.pdf", "a.pdf:evil.exe" })
            if (QuickTransferPolicy.IsAllowed(name)) throw new Exception("Unsafe quick-transfer name accepted: " + name);
        if (new AppSettings().Theme != ThemeService.CrowTheme || ThemeService.Normalize("pink") != ThemeService.PinkTheme)
            throw new Exception("1.8 theme defaults are wrong.");
        var boundary = new CrowLink.Services.RemoteMouse.MouseBoundaryTracker(CrowLink.Services.RemoteMouse.MouseTransitionEdge.Right);
        boundary.TryEnter(99,50,0,0,100,100,false);
        if (boundary.ApplyDelta(9000,0,100) || boundary.X != 1) throw new Exception("Far edge must clamp, not return to the local PC.");
        return Task.CompletedTask;
    }

    public static void CheckInk(AppHost host)
    {
        var type = typeof(AppHost).Assembly.GetType("CrowLink.Services.Mobile.DesktopAnnotationService")!;
        var ink = Task.Run(() => Activator.CreateInstance(type)!).GetAwaiter().GetResult();
        try
        {
            type.GetMethod("Configure")!.Invoke(ink, ["draw", "#ff3030", 4d, host.RemoteMouse.LocalMonitor.Monitors[0]]);
            var window = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "CrowLink 화면 필기");
            window.UpdateLayout();
            type.GetMethod("Point")!.Invoke(ink, [.2d, .2d, "down"]);
            type.GetMethod("Point")!.Invoke(ink, [.8d, .8d, "move"]);
            type.GetMethod("Point")!.Invoke(ink, [.8d, .8d, "up"]);
            var canvas = (Canvas)window.Content;
            if (canvas.Children.Count != 1 || canvas.IsHitTestVisible) throw new Exception("Desktop ink must draw without intercepting underlying input.");
            type.GetMethod("SetWhiteboard")!.Invoke(ink, [true]);
            var escapeTimer = (System.Windows.Threading.DispatcherTimer)type.GetField("_escapeTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ink)!;
            if (escapeTimer.Dispatcher != Application.Current.Dispatcher || !escapeTimer.IsEnabled)
                throw new Exception("Whiteboard escape timer must run on the UI dispatcher even when the service is created in the background.");
            if (!Equals(canvas.Background, System.Windows.Media.Brushes.White) || canvas.Children.Count != 1)
                throw new Exception("Whiteboard must keep ink above a white background.");
            type.GetMethod("SetWhiteboard")!.Invoke(ink, [false]);
            if (!Equals(canvas.Background, System.Windows.Media.Brushes.Transparent) || canvas.Children.Count != 1)
                throw new Exception("Leaving whiteboard must preserve ink and restore the desktop background.");
            type.GetMethod("Clear")!.Invoke(ink, null);
            if (canvas.Children.Count != 0) throw new Exception("Clear ink failed.");
            type.GetMethod("Configure")!.Invoke(ink, ["laser", "#ff3030", 4d, host.RemoteMouse.LocalMonitor.Monitors[0]]);
            type.GetMethod("Point")!.Invoke(ink, [.2d, .2d, "move"]);
            type.GetMethod("Point")!.Invoke(ink, [.4d, .4d, "move"]);
            if (canvas.Children.Count != 2 || canvas.Children.Cast<System.Windows.Shapes.Line>().Any(line => !line.HasAnimatedProperties))
                throw new Exception("Laser must create a fading trail, not just one point.");
        }
        finally { type.GetMethod("Close")!.Invoke(ink, null); }
    }
}
