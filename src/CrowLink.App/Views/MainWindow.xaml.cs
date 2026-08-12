using System.Windows;
using CrowLink.ViewModels;
using CrowLink.Models;
using CrowLink.Services.Explorer;
using CrowLink.Services.Theming;

namespace CrowLink.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private Point _explorerDragStart;
    private ExplorerPackageItem? _explorerDragPackage;
    private Point _monitorDragPoint;
    private string? _monitorDragGroup;
    private FrameworkElement? _monitorDragElement;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowAppearance.ApplyFrame(this, _viewModel.IsSkyTheme);
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
}
