using System.Windows;
using CrowLink.ViewModels;
using CrowLink.Models;
using CrowLink.Services.Explorer;
using CrowLink.Services.Theming;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CrowLink.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Point _explorerDragStart;
    private ExplorerPackageItem? _explorerDragPackage;
    private Point _monitorDragPoint;
    private string? _monitorDragGroup;
    private FrameworkElement? _monitorDragElement;
    private HwndSource? _windowSource;
    private const int MobileEmergencyHotkeyId = 0x4352;
    private Point _receivedDragStart;
    private TransferItem? _receivedDragItem;

    private void ReceivedFile_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _receivedDragStart = e.GetPosition(this);
        _receivedDragItem = (sender as FrameworkElement)?.DataContext as TransferItem;
    }

    private void ReceivedFile_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            _receivedDragItem is not { IsIncoming: true, Status: TransferStatus.Completed, ReceivedPath: { } path }) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _receivedDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _receivedDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _receivedDragItem = null;
        if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
            DragDrop.DoDragDrop(this, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        _viewModel.TransferCompleted += OnTransferCompleted;
    }

    private Window? _transferToast;
    private long _mobilePointerTick;
    private async void MobileBrowserPointer_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (Environment.TickCount64 - _mobilePointerTick < 40 || sender is not FrameworkElement element) return;
        _mobilePointerTick = Environment.TickCount64;
        var point = e.GetPosition(element);
        await _viewModel.SendMobilePointerAsync(point.X / Math.Max(1, element.ActualWidth), point.Y / Math.Max(1, element.ActualHeight), false);
    }
    private async void MobileBrowserPointer_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        var point = e.GetPosition(element);
        await _viewModel.SendMobilePointerAsync(point.X / Math.Max(1, element.ActualWidth), point.Y / Math.Max(1, element.ActualHeight), true);
    }
    private void OnTransferCompleted(object? sender, TransferItem transfer)
    {
        _transferToast?.Close();
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"{(transfer.IsIncoming ? "수신" : "전송")} 완료 · {transfer.DisplayName}",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.White,
        });
        var close = new System.Windows.Controls.Button { Content = "확인", Margin = new Thickness(0,10,0,0) };
        panel.Children.Add(close);
        var toast = new Window
        {
            Title = "CrowLink 전송 완료", Width = 320, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10,30,40)),
            Content = panel, Left = SystemParameters.WorkArea.Right - 336, Top = SystemParameters.WorkArea.Bottom - 150,
        };
        close.Click += (_, _) => toast.Close();
        _transferToast = toast;
        toast.Show();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) => toast.Close();
        toast.Closed += (_, _) => { timer.Stop(); if (_transferToast == toast) _transferToast = null; };
        timer.Start();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximized();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenHelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow(_viewModel.IsSkyTheme) { Owner = this };
        helpWindow.ShowDialog();
    }

    private void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowAppearance.ApplyFrame(this, _viewModel.IsSkyTheme);
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        NativeMethods.RegisterHotKey(handle, MobileEmergencyHotkeyId, 0x0001 | 0x0002, 0x1B);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.TransferCompleted -= OnTransferCompleted;
        _transferToast?.Close();
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, MobileEmergencyHotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int hotkeyMessage = 0x0312;
        if (message == hotkeyMessage && wParam == MobileEmergencyHotkeyId)
        {
            _ = _viewModel.StopRemoteInputAsync();
            if (_viewModel.DisconnectMobileCommand.CanExecute(null))
            {
                _viewModel.DisconnectMobileCommand.Execute(null);
            }

            handled = true;
        }

        return 0;
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && _viewModel.CanQueueFiles
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        _viewModel.IsDragOver = e.Effects == DragDropEffects.Copy;
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => _viewModel.IsDragOver = false;

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await _viewModel.QueueDroppedPathsAsync(paths).ConfigureAwait(true);
        }
    }

    private void ExplorerDrop_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.HasSelectedConnection && OleExplorerDragService.TryExtractFileDrop(e.Data, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ExplorerDrop_DragLeave(object sender, DragEventArgs e) => e.Handled = true;

    private async void ExplorerDrop_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (OleExplorerDragService.TryExtractFileDrop(e.Data, out var paths))
        {
            await _viewModel.SendExplorerPathsAsync(paths).ConfigureAwait(true);
        }
    }

    private void ExplorerPackage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _explorerDragStart = e.GetPosition(this);
        _explorerDragPackage = (sender as FrameworkElement)?.DataContext as ExplorerPackageItem;
    }

    private async void ExplorerPackage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            _explorerDragPackage?.CanDragToExplorer != true)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _explorerDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _explorerDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var package = _explorerDragPackage;
        _explorerDragPackage = null;
        await _viewModel.StartExplorerDragAsync(package).ConfigureAwait(true);
    }

    private void MonitorCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        _viewModel.ResizeMonitorTopology(e.NewSize.Width, e.NewSize.Height);

    private void MonitorItem_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string groupKey)
        {
            return;
        }

        _monitorDragPoint = e.GetPosition(MonitorCanvas);
        _monitorDragGroup = groupKey;
        _monitorDragElement = element;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void MonitorItem_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_monitorDragGroup is null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(MonitorCanvas);
        _viewModel.MoveMonitorGroup(_monitorDragGroup, current.X - _monitorDragPoint.X, current.Y - _monitorDragPoint.Y);
        _monitorDragPoint = current;
        e.Handled = true;
    }

    private async void MonitorItem_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        await FinishMonitorDragAsync().ConfigureAwait(true);
    }

    private async void MonitorItem_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        await FinishMonitorDragAsync().ConfigureAwait(true);
    }

    private async Task FinishMonitorDragAsync()
    {
        if (_monitorDragGroup is null)
        {
            return;
        }

        var element = _monitorDragElement;
        _monitorDragGroup = null;
        _monitorDragElement = null;
        if (element?.IsMouseCaptured == true)
        {
            element.ReleaseMouseCapture();
        }

        await _viewModel.SaveMonitorTopologyAsync().ConfigureAwait(true);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint windowHandle, int id);
    }
}
