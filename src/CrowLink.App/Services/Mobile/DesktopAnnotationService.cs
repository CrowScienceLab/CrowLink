using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CrowLink.Protocol;

namespace CrowLink.Services.Mobile;

/// <summary>Non-activating desktop ink; mobile strokes never click the underlying app.</summary>
internal sealed class DesktopAnnotationService
{
    private Window? _window;
    private Canvas? _canvas;
    private Polyline? _stroke;
    private Point? _lastLaserPoint;
    private long _lastLaserAt;
    private TextBlock? _pointer;
    private bool _whiteboard;
    private DispatcherTimer? _escapeTimer;
    public event Action<bool>? WhiteboardChanged;
    private MonitorDescriptorMessage? _monitor;
    private string _tool = "cursor";
    private Color _color = Colors.Red;
    private double _width = 4;
    public bool IsEnabled => _tool != "cursor";

    public void SetWhiteboard(bool enabled) => OnUi(() =>
    {
        _whiteboard = enabled && _window?.IsVisible == true && _tool == "draw";
        if (_canvas is not null) _canvas.Background = _whiteboard ? Brushes.White : Brushes.Transparent;
        if (_whiteboard)
        {
            if (_escapeTimer is null)
            {
                _escapeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _escapeTimer.Tick += (_, _) => { if ((GetAsyncKeyState(0x1B) & 0x8000) != 0) SetWhiteboard(false); };
            }
            _escapeTimer.Start();
        }
        else _escapeTimer?.Stop();
        WhiteboardChanged?.Invoke(_whiteboard);
    });

    public void Configure(string tool, string color, double width, MonitorDescriptorMessage monitor)
    {
        _tool = tool;
        OnUi(() =>
        {
            _stroke = null;
            _lastLaserPoint = null;
            if (_pointer is not null) _canvas?.Children.Remove(_pointer);
            _pointer = null;
            if (tool != "draw") SetWhiteboard(false);
            _width = Math.Clamp(width, 1, 30);
            try { _color = (Color)ColorConverter.ConvertFromString(color); } catch (FormatException) { _color = Colors.Red; }
            if (tool == "cursor") { _window?.Hide(); return; }
            if (_window is null || _monitor?.DeviceName != monitor.DeviceName || _monitor.Width != monitor.Width ||
                _monitor.Height != monitor.Height || _monitor.X != monitor.X || _monitor.Y != monitor.Y)
            {
                _window?.Close();
                SetWhiteboard(false);
                _monitor = monitor;
                _canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true, IsHitTestVisible = false };
                _window = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                    ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                    Topmost = true, Content = _canvas, Width = monitor.Width, Height = monitor.Height,
                    Title = "CrowLink 화면 필기",
                };
                _window.SourceInitialized += (_, _) =>
                {
                    var handle = new WindowInteropHelper(_window).Handle;
                    SetWindowLongPtr(handle, -20, GetWindowLongPtr(handle, -20) | 0x080000A0);
                    SetWindowPos(handle, -1, monitor.X, monitor.Y, monitor.Width, monitor.Height, 0x0010);
                };
            }
            _window.Show();
            SetWindowPos(new WindowInteropHelper(_window).Handle, -1, monitor.X, monitor.Y, monitor.Width, monitor.Height, 0x0010);
        });
    }

    public void Point(double x, double y, string phase) => OnUi(() =>
    {
        if (_canvas is null || _window?.IsVisible != true) return;
        var point = new Point(Math.Clamp(x, 0, 1) * _canvas.ActualWidth, Math.Clamp(y, 0, 1) * _canvas.ActualHeight);
        if (_tool == "laser")
        {
            if (Environment.TickCount64 - _lastLaserAt > 200) _lastLaserPoint = null;
            _lastLaserAt = Environment.TickCount64;
            var segment = new Line { X1 = _lastLaserPoint?.X ?? point.X, Y1 = _lastLaserPoint?.Y ?? point.Y,
                X2 = point.X + .01, Y2 = point.Y, Stroke = new SolidColorBrush(_color), StrokeThickness = 9,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var canvas = _canvas;
            if (canvas.Children.Count > 2000) canvas.Children.RemoveAt(0);
            canvas.Children.Add(segment);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.1));
            fade.Completed += (_, _) => canvas.Children.Remove(segment);
            segment.BeginAnimation(UIElement.OpacityProperty, fade);
            _lastLaserPoint = point;
            return;
        }
        if (_tool is "wand" or "finger")
        {
            if (_pointer is null)
            {
                _pointer = new TextBlock { Text = _tool == "finger" ? "☝" : "╱", FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = _tool == "finger" ? 48 : 65, Foreground = new SolidColorBrush(_color),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Color = Colors.Black } };
                _canvas.Children.Add(_pointer);
            }
            Canvas.SetLeft(_pointer, point.X - (_tool == "finger" ? 20 : 30));
            Canvas.SetTop(_pointer, point.Y - 7);
            return;
        }
        if (phase == "down")
        {
            if (_canvas.Children.Count > 2000) _canvas.Children.RemoveAt(0);
            _stroke = new Polyline
            {
                Stroke = new SolidColorBrush(_color), StrokeThickness = _tool == "highlight" ? Math.Max(12, _width * 4) : _width,
                Opacity = _tool == "highlight" ? .35 : 1, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            };
            _stroke.Points.Add(point);
            _stroke.Points.Add(new Point(point.X + .01, point.Y));
            _canvas.Children.Add(_stroke);
        }
        else if (phase == "move" && _stroke is not null) _stroke.Points.Add(point);
        else if (phase is "up" or "cancel") _stroke = null;
    });

    public void Cursor(string phase)
    {
        var monitor = _monitor;
        if (monitor is null || !GetCursorPos(out var point)) return;
        Point((point.X - monitor.X) / (double)monitor.Width, (point.Y - monitor.Y) / (double)monitor.Height, phase);
    }

    public void Clear() => OnUi(() => { _canvas?.Children.Clear(); _stroke = null; _pointer = null; _lastLaserPoint = null; });
    public void Close()
    {
        _tool = "cursor";
        OnUi(() => { SetWhiteboard(false); _window?.Close(); _window = null; _canvas = null; _stroke = null; _pointer = null; _lastLaserPoint = null; });
    }
    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
