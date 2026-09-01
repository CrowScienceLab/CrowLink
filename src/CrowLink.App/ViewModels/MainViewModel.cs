using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CrowLink.Services.Clipboard;
using CrowLink.Models;
using CrowLink.Services;
using CrowLink.Services.Network;
using CrowLink.Services.Security;
using CrowLink.Services.RemoteMouse;
using CrowLink.Services.Settings;
using CrowLink.Services.Theming;
using CrowLink.Protocol;
using CrowLink.Services.Explorer;
using CrowLink.Services.Mobile;
using CrowLink.Utilities;
using CrowLink.Views;

namespace CrowLink.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppHost _host;
    private DeviceInfo? _selectedDevice;
    private bool _isDragOver;
    private string _serviceStatus = "시작 중…";
    private string _clipboardStatus = "텍스트 또는 이미지를 선택한 PC로 보낼 수 있습니다.";
    private string _selectedMouseEdge = "오른쪽";
    private string _selectedSection = "Connect";
    private string _explorerStatus = "Explorer에서 파일을 놓으면 상대 PC로 전송할 수 있습니다.";
    private string _connectionStatusText = "연결 안 됨";
    private Brush _connectionStatusBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x88, 0x9B));
    private double _monitorCanvasWidth = 680d;
    private double _monitorCanvasHeight = 220d;
    private ImageSource? _mobileQrImage;

    public MainViewModel(AppHost host)
    {
        _host = host;
        Devices = [];
        Transfers = [];
        PendingTransfers = [];
        Activities = [];
        ExplorerPackages = [];
        MonitorTopologyItems = [];
        ConnectCommand = new AsyncRelayCommand(ConnectSelectedAsync, () => SelectedDevice is not null && SelectedDevice.State != ConnectionState.Connected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectSelectedAsync, () => SelectedDevice is not null && HasSelectedConnection);
        CancelTransferCommand = new AsyncRelayCommand<TransferItem>(CancelTransferAsync, transfer => transfer?.CanCancel == true);
        RemovePendingCommand = new AsyncRelayCommand<PendingTransferItem>(RemovePendingAsync, item => item is not null);
        ClearPendingCommand = new RelayCommand(ClearPending, () => PendingTransfers.Count > 0);
        SendQueuedCommand = new AsyncRelayCommand(SendQueuedAsync, () => PendingTransfers.Count > 0 && HasSelectedConnection);
        SendTextClipboardCommand = new AsyncRelayCommand(SendTextClipboardAsync, () => HasSelectedConnection);
        SendImageClipboardCommand = new AsyncRelayCommand(SendImageClipboardAsync, () => HasSelectedConnection);
        ToggleRemoteMouseCommand = new AsyncRelayCommand(ToggleRemoteMouseAsync, () => HasSelectedConnection || _host.RemoteMouse.IsActive);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        ShowConnectCommand = new RelayCommand(() => SelectedSection = "Connect");
        ShowShareCommand = new RelayCommand(() => SelectedSection = "Share");
        ShowControlCommand = new RelayCommand(() => SelectedSection = "Control");
        ShowExplorerCommand = new RelayCommand(() => SelectedSection = "Explorer");
        ShowMobileCommand = new RelayCommand(() => SelectedSection = "Mobile");
        ToggleMobileServerCommand = new AsyncRelayCommand(ToggleMobileServerAsync);
        DisconnectMobileCommand = new AsyncRelayCommand(DisconnectMobileAsync, () => _host.MobileTouchpad.HasActiveSession);
        RefreshMobileCodeCommand = new RelayCommand(RefreshMobileCode, () => !_host.MobileTouchpad.HasActiveSession);
        CopyMobileUrlCommand = new RelayCommand(CopyMobileUrl, () => _host.MobileTouchpad.IsRunning);

        Devices.CollectionChanged += OnCollectionChanged;
        Transfers.CollectionChanged += OnCollectionChanged;
        PendingTransfers.CollectionChanged += OnCollectionChanged;
        Activities.CollectionChanged += OnCollectionChanged;
        ExplorerPackages.CollectionChanged += OnCollectionChanged;
        _host.Discovery.DeviceDiscovered += OnDeviceDiscovered;
        _host.Discovery.DeviceExpired += OnDeviceExpired;
        _host.Connections.DeviceConnected += OnDeviceConnected;
        _host.Connections.DeviceDisconnected += OnDeviceDisconnected;
        _host.Transfers.TransferAdded += OnTransferAdded;
        _host.Transfers.TransferChanged += OnTransferChanged;
        _host.Pairing.ApprovalRequested += RequestPairApprovalAsync;
        _host.Clipboard.ContentReceived += OnClipboardContentReceivedAsync;
        _host.RemoteMouse.ControlRequested += RequestRemoteMouseApprovalAsync;
        _host.RemoteMouse.StateChanged += OnRemoteMouseStateChanged;
        _host.RemoteMouse.MonitorChanged += OnRemoteMonitorChanged;
        _host.Explorer.OfferApprovalRequested += RequestExplorerOfferApprovalAsync;
        _host.Explorer.PackageChanged += OnExplorerPackageChanged;
        _host.MobileTouchpad.PairingRequested += RequestMobilePairingApprovalAsync;
        _host.MobileTouchpad.StateChanged += OnMobileTouchpadStateChanged;
        _host.MobileTouchpad.AutoStopped += OnMobileTouchpadAutoStopped;
        RefreshMobileQr();
        RefreshMonitorTopology();
    }

    public string DeviceName => _host.Settings.Current.DeviceName;
    public bool IsSkyTheme => _host.Theme.CurrentTheme == ThemeService.SkyTheme;
    public string AutomationSummary
    {
        get
        {
            var count = new[]
            {
                _host.Settings.Current.AutoApproveConnect,
                _host.Settings.Current.AutoApproveShare,
                _host.Settings.Current.AutoApproveControl,
                _host.Settings.Current.AutoApproveExplorer,
            }.Count(value => value);
            return count == 0 ? "MANUAL" : $"AUTO {count}/4";
        }
    }
    public string ShareDropHint => _host.Settings.Current.AutoApproveShare
        ? "AUTO · 드롭하면 즉시 전송"
        : "목록을 만든 뒤 전송합니다";
    public ObservableCollection<DeviceInfo> Devices { get; }
    public ObservableCollection<TransferItem> Transfers { get; }
    public ObservableCollection<PendingTransferItem> PendingTransfers { get; }
    public ObservableCollection<object> Activities { get; }
    public ObservableCollection<ExplorerPackageItem> ExplorerPackages { get; }
    public ObservableCollection<MonitorTopologyItem> MonitorTopologyItems { get; }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand<TransferItem> CancelTransferCommand { get; }
    public AsyncRelayCommand<PendingTransferItem> RemovePendingCommand { get; }
    public RelayCommand ClearPendingCommand { get; }
    public AsyncRelayCommand SendQueuedCommand { get; }
    public AsyncRelayCommand SendTextClipboardCommand { get; }
    public AsyncRelayCommand SendImageClipboardCommand { get; }
    public AsyncRelayCommand ToggleRemoteMouseCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public RelayCommand ShowConnectCommand { get; }
    public RelayCommand ShowShareCommand { get; }
    public RelayCommand ShowControlCommand { get; }
    public RelayCommand ShowExplorerCommand { get; }
    public RelayCommand ShowMobileCommand { get; }
    public AsyncRelayCommand ToggleMobileServerCommand { get; }
    public AsyncRelayCommand DisconnectMobileCommand { get; }
    public RelayCommand RefreshMobileCodeCommand { get; }
    public RelayCommand CopyMobileUrlCommand { get; }

    public DeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(HasSelectedConnection));
                OnPropertyChanged(nameof(RemoteMonitorText));
                OnPropertyChanged(nameof(RemoteMonitorDetail));
                OnPropertyChanged(nameof(RemoteMonitorItems));
                RefreshMonitorTopology();
                RaiseConnectionCommandStates();
            }
        }
    }

    public string SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (SetProperty(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(ConnectVisibility));
                OnPropertyChanged(nameof(ShareVisibility));
                OnPropertyChanged(nameof(ControlVisibility));
                OnPropertyChanged(nameof(ExplorerVisibility));
                OnPropertyChanged(nameof(MobileVisibility));
                OnPropertyChanged(nameof(IsConnectSelected));
                OnPropertyChanged(nameof(IsShareSelected));
                OnPropertyChanged(nameof(IsControlSelected));
                OnPropertyChanged(nameof(IsExplorerSelected));
                OnPropertyChanged(nameof(IsMobileSelected));
            }
        }
    }

    public Visibility ConnectVisibility => SelectedSection == "Connect" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ShareVisibility => SelectedSection == "Share" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlVisibility => SelectedSection == "Control" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExplorerVisibility => SelectedSection == "Explorer" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MobileVisibility => SelectedSection == "Mobile" ? Visibility.Visible : Visibility.Collapsed;
    public bool IsConnectSelected => SelectedSection == "Connect";
    public bool IsShareSelected => SelectedSection == "Share";
    public bool IsControlSelected => SelectedSection == "Control";
    public bool IsExplorerSelected => SelectedSection == "Explorer";
    public bool IsMobileSelected => SelectedSection == "Mobile";

    public string MobileStatus => _host.MobileTouchpad.Status;
    public string MobileUrl => _host.MobileTouchpad.MobileUrl;
    public string MobilePairingCode
    {
        get
        {
            var code = _host.MobileTouchpad.PairingCode;
            return code.Length == 6 ? $"{code[..3]} {code[3..]}" : code;
        }
    }
    public string MobileDeviceText => _host.MobileTouchpad.Session is { } session
        ? $"{session.DeviceName} · {session.Address}"
        : "연결된 휴대폰 없음";
    public string MobileStatistics => _host.MobileTouchpad.Statistics;
    public string MobileServerButtonText => _host.MobileTouchpad.IsRunning ? "서버 중지" : "서버 시작";
    public string MobileStopButtonText => _host.MobileTouchpad.HasActiveSession ? "중지" : "중지 대기";
    public bool IsMobileSessionActive => _host.MobileTouchpad.HasActiveSession;
    public string MobileHeaderText => _host.MobileTouchpad.HasActiveSession
        ? "MOBILE ON"
        : _host.MobileTouchpad.IsRunning ? "MOBILE WAIT" : "MOBILE OFF";
    public Brush MobileStateBrush => new SolidColorBrush(_host.MobileTouchpad.HasActiveSession
        ? Color.FromRgb(0x63, 0xED, 0xB0)
        : _host.MobileTouchpad.IsRunning
            ? Color.FromRgb(0xFF, 0xD0, 0x70)
            : Color.FromRgb(0x62, 0x72, 0x7D));
    public ImageSource? MobileQrImage => _mobileQrImage;

    public string ServiceStatus
    {
        get => _serviceStatus;
        private set => SetProperty(ref _serviceStatus, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    public Brush ConnectionStatusBrush
    {
        get => _connectionStatusBrush;
        private set => SetProperty(ref _connectionStatusBrush, value);
    }

    public string ClipboardStatus
    {
        get => _clipboardStatus;
        private set => SetProperty(ref _clipboardStatus, value);
    }

    public IReadOnlyList<string> MouseEdges { get; } = ["왼쪽", "오른쪽"];

    public string SelectedMouseEdge
    {
        get => _selectedMouseEdge;
        set => SetProperty(ref _selectedMouseEdge, value);
    }

    public string LocalMonitorText
    {
        get
        {
            var monitor = _host.RemoteMouse.LocalMonitor;
            return $"{monitor.VirtualWidth}×{monitor.VirtualHeight} · {monitor.MonitorCount}개 화면";
        }
    }

    public string RemoteMonitorText
    {
        get
        {
            if (SelectedDevice is null)
            {
                return "연결할 PC를 선택하세요";
            }

            return _host.RemoteMouse.TryGetRemoteMonitor(SelectedDevice.Id, out var monitor) && monitor is not null
                ? $"{monitor.VirtualWidth}×{monitor.VirtualHeight} · {monitor.MonitorCount}개 화면"
                : "화면 정보 대기 중";
        }
    }

    public string LocalMonitorDetail => BuildMonitorDetail(_host.RemoteMouse.LocalMonitor);
    public IReadOnlyList<MonitorDisplayItem> LocalMonitorItems => BuildMonitorItems(_host.RemoteMouse.LocalMonitor);

    public string RemoteMonitorDetail
    {
        get
        {
            if (SelectedDevice is null ||
                !_host.RemoteMouse.TryGetRemoteMonitor(SelectedDevice.Id, out var monitor) || monitor is null)
            {
                return "다중 모니터 및 DPI 정보 대기 중";
            }

            return BuildMonitorDetail(monitor);
        }
    }

    public IReadOnlyList<MonitorDisplayItem> RemoteMonitorItems
    {
        get
        {
            if (SelectedDevice is null ||
                !_host.RemoteMouse.TryGetRemoteMonitor(SelectedDevice.Id, out var monitor) || monitor is null)
            {
                return [];
            }

            return BuildMonitorItems(monitor);
        }
    }

    public void ResizeMonitorTopology(double width, double height)
    {
        if (width < 200d || height < 120d ||
            Math.Abs(_monitorCanvasWidth - width) < 1d && Math.Abs(_monitorCanvasHeight - height) < 1d)
        {
            return;
        }

        _monitorCanvasWidth = width;
        _monitorCanvasHeight = height;
        RefreshMonitorTopology();
    }

    public void MoveMonitorGroup(string groupKey, double deltaX, double deltaY)
    {
        var items = MonitorTopologyItems.Where(item => item.GroupKey == groupKey).ToArray();
        if (items.Length == 0)
        {
            return;
        }

        var left = items.Min(item => item.Left);
        var top = items.Min(item => item.Top);
        var right = items.Max(item => item.Left + item.Width);
        var bottom = items.Max(item => item.Top + item.Height);
        deltaX = Math.Clamp(deltaX, 8d - left, _monitorCanvasWidth - 8d - right);
        deltaY = Math.Clamp(deltaY, 26d - top, _monitorCanvasHeight - 8d - bottom);

        foreach (var item in items)
        {
            item.Left += deltaX;
            item.Top += deltaY;
        }

        var placement = GetOrCreatePlacement(items[0].DeviceId, 0d, 0d);
        placement.X = Math.Clamp(items.Min(item => item.Left) / Math.Max(1d, _monitorCanvasWidth), 0d, 1d);
        placement.Y = Math.Clamp(items.Min(item => item.Top) / Math.Max(1d, _monitorCanvasHeight), 0d, 1d);
        UpdateEdgeFromTopology();
    }

    public Task SaveMonitorTopologyAsync() => _host.Settings.SaveAsync();

    public string RemoteMouseStatus => _host.RemoteMouse.Status;
    public string RemoteMouseButtonText => _host.RemoteMouse.IsActive ? "입력 공유 중지" : "입력 공유 시작";

    public string ExplorerStatus
    {
        get => _explorerStatus;
        private set => SetProperty(ref _explorerStatus, value);
    }

    public bool IsDragOver
    {
        get => _isDragOver;
        set
        {
            if (SetProperty(ref _isDragOver, value))
            {
                OnPropertyChanged(nameof(DropZoneBackground));
            }
        }
    }

    public Brush DropZoneBackground =>
        (Brush)Application.Current.FindResource(IsDragOver ? "DropZoneActiveBrush" : "DropZoneBrush");
    public bool HasSelectedConnection => SelectedDevice is not null && _host.Connections.TryGetConnection(SelectedDevice.Id, out _);
    public bool CanQueueFiles => true;
    public Visibility EmptyDevicesVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyTransfersVisibility => Transfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyPendingVisibility => PendingTransfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyActivitiesVisibility => Activities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyExplorerPackagesVisibility => ExplorerPackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SendQueueButtonText => PendingTransfers.Count == 0 ? "전송 시작" : $"전송 시작 ({PendingTransfers.Count})";

    public async Task StartAsync()
    {
        try
        {
            await _host.Connections.StartAsync().ConfigureAwait(true);
            await _host.Discovery.StartAsync().ConfigureAwait(true);
            ServiceStatus = "검색 및 수신 대기 중";
            if (_host.Settings.Current.EnableMobileTouchpad)
            {
                try
                {
                    await _host.MobileTouchpad.StartAsync().ConfigureAwait(true);
                    RefreshMobileQr();
                }
                catch (Exception mobileException)
                {
                    await _host.Log.ErrorAsync("Mobile Touchpad service failed to start", mobileException).ConfigureAwait(true);
                    MessageBox.Show(
                        $"PC 연결 기능은 정상적으로 시작했지만 Mobile Touchpad 서버를 열지 못했습니다.\n\n{mobileException.Message}",
                        "CrowLink Mobile Touchpad",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception exception)
        {
            ServiceStatus = "서비스 시작 실패";
            await _host.Log.ErrorAsync("Services failed to start", exception).ConfigureAwait(true);
            MessageBox.Show(
                $"네트워크 서비스를 시작하지 못했습니다. 포트 또는 방화벽 설정을 확인하세요.\n\n{exception.Message}",
                "CrowLink",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public async Task QueueDroppedPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if ((!File.Exists(fullPath) && !Directory.Exists(fullPath)) ||
                PendingTransfers.Any(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            PendingTransfers.Add(new PendingTransferItem(fullPath));
        }

        if (_host.Settings.Current.AutoApproveShare && PendingTransfers.Count > 0 && HasSelectedConnection)
        {
            await SendQueuedAsync().ConfigureAwait(true);
        }
    }

    public async Task SendExplorerPathsAsync(IEnumerable<string> paths)
    {
        if (!TryGetSelectedConnection(out var connection) || connection is null)
        {
            MessageBox.Show("Explorer 패키지를 보낼 연결 장치를 선택하세요.", "CrowLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ExplorerStatus = $"{connection.Device.Name}에 Explorer 패키지를 제안하는 중";
            await _host.Explorer.SendPackageAsync(connection, paths).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ExplorerStatus = $"Explorer 전송 실패: {exception.Message}";
            await _host.Log.ErrorAsync("Explorer bridge send failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink Explorer Bridge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task StartExplorerDragAsync(ExplorerPackageItem package)
    {
        if (!package.CanDragToExplorer)
        {
            return;
        }

        try
        {
            ExplorerStatus = "Explorer 대상 폴더에 놓으세요. Esc를 누르면 취소됩니다.";
            var copied = OleExplorerDragService.StartFileDrop(package.LocalPaths);
            if (!copied)
            {
                ExplorerStatus = "Explorer 드래그가 취소되었습니다. staging 파일은 유지됩니다.";
                return;
            }

            var removed = await _host.Explorer.ConsumeIncomingPackageAsync(package.PackageId).ConfigureAwait(true);
            ExplorerStatus = removed
                ? "원하는 폴더로 이동했습니다. 임시 수신 파일을 삭제했습니다."
                : "Explorer 복사는 완료됐지만 staging 정리에 실패했습니다.";
        }
        catch (Exception exception)
        {
            ExplorerStatus = $"OLE 드래그 실패: {exception.Message}";
            MessageBox.Show(exception.Message, "CrowLink OLE Drag", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SendQueuedAsync()
    {
        if (!TryGetSelectedConnection(out var connection) || connection is null)
        {
            MessageBox.Show("파일을 보낼 연결 장치를 선택하세요.", "CrowLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paths = PendingTransfers.Select(item => item.Path).ToArray();
        PendingTransfers.Clear();
        try
        {
            await _host.Transfers.SendPathsAsync(connection, paths).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Dropped paths could not be prepared", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink 전송 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task RemovePendingAsync(PendingTransferItem? item)
    {
        if (item is not null)
        {
            PendingTransfers.Remove(item);
        }

        return Task.CompletedTask;
    }

    private void ClearPending() => PendingTransfers.Clear();

    private async Task ConnectSelectedAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            ConnectionStatusText = $"연결 중 · {SelectedDevice.Name}";
            ConnectionStatusBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xC4, 0x51));
            await _host.Connections.ConnectAsync(SelectedDevice).ConfigureAwait(true);
            OnPropertyChanged(nameof(HasSelectedConnection));
            RaiseConnectionCommandStates();
        }
        catch (Exception exception)
        {
            ConnectionStatusText = $"연결 실패 · {SelectedDevice.Name}";
            ConnectionStatusBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x72, 0x7A));
            await _host.Log.ErrorAsync("Connection attempt failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink 연결 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task DisconnectSelectedAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        try
        {
            await _host.Connections.DisconnectAsync(SelectedDevice.Id).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Disconnect failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink 연결 해제 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CancelTransferAsync(TransferItem? transfer)
    {
        if (transfer is null || !transfer.CanCancel)
        {
            return;
        }

        try
        {
            await _host.Transfers.CancelTransferAsync(transfer.BatchId).ConfigureAwait(true);
            CancelTransferCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Transfer cancellation failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink 전송 취소 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SendTextClipboardAsync()
    {
        if (!TryGetSelectedConnection(out var connection) || connection is null)
        {
            return;
        }

        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                throw new InvalidOperationException("클립보드에 텍스트가 없습니다.");
            }

            var text = System.Windows.Clipboard.GetText();
            await _host.Clipboard.SendTextAsync(connection, text).ConfigureAwait(true);
            ClipboardStatus = $"{connection.Device.Name}에 텍스트 전송 요청을 보냈습니다.";
            AddClipboardHistory("텍스트 클립보드", connection.Device.Name, "전송 요청");
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Text clipboard send failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "텍스트 클립보드 전송 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            AddClipboardHistory("텍스트 클립보드", exception.Message, "실패");
        }
    }

    private async Task SendImageClipboardAsync()
    {
        if (!TryGetSelectedConnection(out var connection) || connection is null)
        {
            return;
        }

        try
        {
            var image = System.Windows.Clipboard.GetImage()
                ?? throw new InvalidOperationException("클립보드에 이미지가 없습니다.");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            await using var stream = new MemoryStream();
            encoder.Save(stream);
            await _host.Clipboard.SendImageAsync(connection, stream.ToArray()).ConfigureAwait(true);
            ClipboardStatus = $"{connection.Device.Name}에 이미지 전송 요청을 보냈습니다.";
            AddClipboardHistory("이미지 클립보드", connection.Device.Name, "전송 요청");
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Image clipboard send failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "이미지 클립보드 전송 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            AddClipboardHistory("이미지 클립보드", exception.Message, "실패");
        }
    }

    private async Task ToggleRemoteMouseAsync()
    {
        try
        {
            if (_host.RemoteMouse.IsActive)
            {
                await _host.RemoteMouse.StopAsync().ConfigureAwait(true);
                return;
            }

            if (!TryGetSelectedConnection(out var connection) || connection is null)
            {
                MessageBox.Show("키보드와 마우스를 공유할 연결 장치를 선택하세요.", "CrowLink", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var edge = string.Equals(SelectedMouseEdge, "왼쪽", StringComparison.Ordinal)
                ? MouseTransitionEdge.Left
                : MouseTransitionEdge.Right;
            await _host.RemoteMouse.RequestControlAsync(connection, edge).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Remote input operation failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink 입력 공유", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task OpenSettingsAsync()
    {
        var mobileWasRunning = _host.MobileTouchpad.IsRunning;
        var previousMobilePort = _host.Settings.Current.MobileTouchpadPort;
        var viewModel = new SettingsViewModel(_host.Settings.Current);
        var dialog = new SettingsWindow(viewModel) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            viewModel.Apply();
            await _host.Settings.SaveAsync().ConfigureAwait(true);
            _host.Theme.Apply(_host.Settings.Current.Theme);
            if (_host.Settings.Current.EnableMobileTouchpad)
            {
                if (mobileWasRunning && previousMobilePort != _host.Settings.Current.MobileTouchpadPort)
                {
                    await _host.MobileTouchpad.StopAsync().ConfigureAwait(true);
                    mobileWasRunning = false;
                }

                if (!mobileWasRunning)
                {
                    await _host.MobileTouchpad.StartAsync().ConfigureAwait(true);
                }
            }
            else if (mobileWasRunning)
            {
                await _host.MobileTouchpad.StopAsync().ConfigureAwait(true);
            }

            RefreshMobileQr();
            OnPropertyChanged(nameof(DeviceName));
            OnPropertyChanged(nameof(DropZoneBackground));
            OnPropertyChanged(nameof(AutomationSummary));
            OnPropertyChanged(nameof(ShareDropHint));
            MessageBox.Show(
                "설정을 저장했습니다. 장치 이름 또는 포트를 변경했다면 CrowLink를 다시 시작하세요.",
                "CrowLink",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Settings could not be saved", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "설정 저장 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task<bool> RequestPairApprovalAsync(PairingRequest request)
    {
        if (_host.Settings.Current.AutoApproveConnect)
        {
            return Task.FromResult(true);
        }

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                $"{request.DeviceName}에서 연결을 요청했습니다.\n\n주소: {request.Address}\n이번 연결을 허용하시겠습니까?",
                "CrowLink 연결 요청",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }).Task;
    }

    private Task<bool> RequestRemoteMouseApprovalAsync(RemoteMouseControlRequest request)
    {
        if (_host.Settings.Current.AutoApproveControl)
        {
            return Task.FromResult(true);
        }

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var edge = request.EntryEdge == MouseTransitionEdge.Right ? "오른쪽" : "왼쪽";
            var result = MessageBox.Show(
                $"{request.Connection.Device.Name}에서 이 PC의 키보드와 마우스 제어를 요청했습니다.\n\n요청 PC의 {edge} 화면 경계를 통해 들어옵니다.\n일반 단축키도 원격 PC로 전달되며 Ctrl+Alt+Esc로 즉시 종료할 수 있습니다.\n\n이번 입력 공유를 허용하시겠습니까?",
                "CrowLink 원격 입력 요청",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }).Task;
    }

    private Task<bool> RequestExplorerOfferApprovalAsync(ExplorerDragOfferRequest request)
    {
        if (_host.Settings.Current.AutoApproveExplorer)
        {
            return Task.FromResult(true);
        }

        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var summary = string.Join(", ", request.Items.Take(5).Select(item => item.Name));
            if (request.Items.Count > 5)
            {
                summary += $" 외 {request.Items.Count - 5}개";
            }

            var result = MessageBox.Show(
                $"{request.Connection.Device.Name}에서 Explorer 드래그 패키지를 보냅니다.\n\n{summary}\n\n수신 후 CrowLink에서 Explorer 폴더로 다시 드래그할 수 있습니다. 수락하시겠습니까?",
                "CrowLink Explorer 전송 요청",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }).Task;
    }

    private Task<bool> RequestMobilePairingApprovalAsync(MobilePairingRequest request) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                $"{request.DeviceName}에서 이 PC의 마우스 제어를 요청했습니다.\n\n주소: {request.Address}\n장치 유형: Mobile Browser\n\n휴대폰 화면을 무선 터치패드로 사용하도록 이번 세션을 허용하시겠습니까?",
                "CrowLink Mobile Touchpad 연결 요청",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }).Task;

    private async Task ToggleMobileServerAsync()
    {
        try
        {
            if (_host.MobileTouchpad.IsRunning)
            {
                await _host.MobileTouchpad.StopAsync().ConfigureAwait(true);
                _host.Settings.Current.EnableMobileTouchpad = false;
            }
            else
            {
                await _host.MobileTouchpad.StartAsync().ConfigureAwait(true);
                _host.Settings.Current.EnableMobileTouchpad = true;
            }

            await _host.Settings.SaveAsync().ConfigureAwait(true);
            RefreshMobileQr();
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Mobile Touchpad server operation failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink Mobile Touchpad", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task DisconnectMobileAsync()
    {
        try
        {
            await _host.MobileTouchpad.StopAsync().ConfigureAwait(true);
            _host.Settings.Current.EnableMobileTouchpad = false;
            await _host.Settings.SaveAsync().ConfigureAwait(true);
            RefreshMobileQr();
        }
        catch (Exception exception)
        {
            await _host.Log.ErrorAsync("Mobile Touchpad disconnect failed", exception).ConfigureAwait(true);
        }
    }

    private void RefreshMobileCode()
    {
        _host.MobileTouchpad.RefreshPairingCode();
        RefreshMobileQr();
    }

    private void CopyMobileUrl()
    {
        try
        {
            System.Windows.Clipboard.SetText(MobileUrl);
        }
        catch (Exception exception)
        {
            _ = _host.Log.WarningAsync($"Mobile URL clipboard copy failed: {exception.Message}");
        }
    }

    private void RefreshMobileQr()
    {
        try
        {
            _mobileQrImage = _host.MobileTouchpad.IsRunning
                ? MobileQrCode.CreateBitmap(_host.MobileTouchpad.MobileUrl, 4)
                : null;
        }
        catch (Exception exception)
        {
            _mobileQrImage = null;
            _ = _host.Log.WarningAsync($"Mobile QR generation failed: {exception.Message}");
        }

        RaiseMobileProperties();
    }

    private void OnMobileTouchpadStateChanged(object? sender, EventArgs e) => Dispatch(() =>
    {
        RaiseMobileProperties();
        DisconnectMobileCommand.RaiseCanExecuteChanged();
        RefreshMobileCodeCommand.RaiseCanExecuteChanged();
        CopyMobileUrlCommand.RaiseCanExecuteChanged();
    });

    private void OnMobileTouchpadAutoStopped(object? sender, EventArgs e)
    {
        _host.Settings.Current.EnableMobileTouchpad = false;
        _ = _host.Settings.SaveAsync();
        Dispatch(RefreshMobileQr);
    }

    private void RaiseMobileProperties()
    {
        OnPropertyChanged(nameof(MobileStatus));
        OnPropertyChanged(nameof(MobileUrl));
        OnPropertyChanged(nameof(MobilePairingCode));
        OnPropertyChanged(nameof(MobileDeviceText));
        OnPropertyChanged(nameof(MobileStatistics));
        OnPropertyChanged(nameof(MobileServerButtonText));
        OnPropertyChanged(nameof(MobileStopButtonText));
        OnPropertyChanged(nameof(IsMobileSessionActive));
        OnPropertyChanged(nameof(MobileHeaderText));
        OnPropertyChanged(nameof(MobileStateBrush));
        OnPropertyChanged(nameof(MobileQrImage));
    }

    private Task<bool> OnClipboardContentReceivedAsync(ClipboardContentReceivedEventArgs content) =>
        Application.Current.Dispatcher.InvokeAsync(() => ApplyReceivedClipboardAsync(content)).Task.Unwrap();

    private async Task<bool> ApplyReceivedClipboardAsync(ClipboardContentReceivedEventArgs content)
    {
        var description = content.Kind == ClipboardContentKind.Text
            ? $"텍스트: {CreateTextPreview(content.Text!)}"
            : $"PNG 이미지 ({FormatUtilities.FormatBytes(content.ImagePng!.Length)})";
        if (!_host.Settings.Current.AutoApproveShare)
        {
            var result = MessageBox.Show(
                $"{content.Connection.Device.Name}에서 클립보드를 보냈습니다.\n\n{description}\n\n이 PC의 클립보드에 적용하시겠습니까?",
                "CrowLink 클립보드 수신",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                ClipboardStatus = $"{content.Connection.Device.Name}의 클립보드를 거부했습니다.";
                AddClipboardHistory($"{content.Kind} 클립보드", content.Connection.Device.Name, "수신 거부");
                return false;
            }
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (content.Kind == ClipboardContentKind.Text)
                {
                    System.Windows.Clipboard.SetText(content.Text!);
                }
                else
                {
                    using var stream = new MemoryStream(content.ImagePng!, writable: false);
                    var decoder = new PngBitmapDecoder(
                        stream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    var bitmap = decoder.Frames[0];
                    bitmap.Freeze();
                    System.Windows.Clipboard.SetImage(bitmap);
                }

                ClipboardStatus = $"{content.Connection.Device.Name}의 {content.Kind} 클립보드를 적용했습니다.";
                AddClipboardHistory($"{content.Kind} 클립보드", content.Connection.Device.Name, "수신 적용");
                return true;
            }
            catch (System.Runtime.InteropServices.COMException exception)
            {
                lastError = exception;
                await Task.Delay(80).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                await _host.Log.ErrorAsync("Received clipboard could not be applied", exception).ConfigureAwait(true);
                ClipboardStatus = "받은 클립보드를 적용하지 못했습니다.";
                AddClipboardHistory($"{content.Kind} 클립보드", exception.Message, "적용 실패");
                MessageBox.Show(
                    exception.Message,
                    "CrowLink 클립보드 적용 실패",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        throw new InvalidOperationException("Windows 클립보드가 다른 프로그램에서 사용 중입니다.", lastError);
    }

    private void OnDeviceDiscovered(object? sender, DeviceInfo discovered) => Dispatch(() =>
    {
        var existing = Devices.FirstOrDefault(device => device.Id == discovered.Id);
        if (existing is null)
        {
            Devices.Add(new DeviceInfo(discovered.Id, discovered.Name, discovered.Address, discovered.TcpPort, discovered.LastSeen));
        }
        else
        {
            existing.UpdateFrom(discovered.Name, discovered.Address, discovered.TcpPort, discovered.LastSeen);
        }
    });

    private void OnDeviceExpired(object? sender, Guid deviceId) => Dispatch(() =>
    {
        var device = Devices.FirstOrDefault(item => item.Id == deviceId);
        if (device is not null && device.State != ConnectionState.Connected)
        {
            Devices.Remove(device);
        }
    });

    private void OnDeviceConnected(object? sender, PeerConnection connection) => Dispatch(() =>
    {
        var device = Devices.FirstOrDefault(item => item.Id == connection.Device.Id);
        if (device is null)
        {
            device = connection.Device;
            Devices.Add(device);
        }
        else
        {
            device.State = ConnectionState.Connected;
        }

        SelectedDevice = device;
        ConnectionStatusText = $"연결됨 · {device.Name}";
        ConnectionStatusBrush = new SolidColorBrush(Color.FromRgb(0x35, 0xD3, 0x99));
        OnPropertyChanged(nameof(HasSelectedConnection));
        RaiseConnectionCommandStates();
    });

    private void OnDeviceDisconnected(object? sender, DeviceInfo disconnected) => Dispatch(() =>
    {
        var device = Devices.FirstOrDefault(item => item.Id == disconnected.Id);
        if (device is not null)
        {
            device.State = disconnected.State;
        }

        ConnectionStatusText = $"연결 끊김 · {disconnected.Name}";
        ConnectionStatusBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0x72, 0x7A));

        OnPropertyChanged(nameof(HasSelectedConnection));
        RaiseConnectionCommandStates();
    });

    private void OnRemoteMouseStateChanged(object? sender, EventArgs e) => Dispatch(() =>
    {
        OnPropertyChanged(nameof(RemoteMouseStatus));
        OnPropertyChanged(nameof(RemoteMouseButtonText));
        ToggleRemoteMouseCommand.RaiseCanExecuteChanged();
    });

    private void OnRemoteMonitorChanged(object? sender, RemoteMonitorChangedEventArgs e) => Dispatch(() =>
    {
        if (SelectedDevice?.Id == e.DeviceId)
        {
            OnPropertyChanged(nameof(RemoteMonitorText));
            OnPropertyChanged(nameof(RemoteMonitorDetail));
            OnPropertyChanged(nameof(RemoteMonitorItems));
            RefreshMonitorTopology();
        }
    });

    private void OnExplorerPackageChanged(object? sender, ExplorerPackageChangedEventArgs e) => Dispatch(() =>
    {
        var snapshot = e.Package;
        var existing = ExplorerPackages.FirstOrDefault(item => item.PackageId == snapshot.PackageId);
        if (existing is null)
        {
            ExplorerPackages.Insert(0, new ExplorerPackageItem(snapshot));
        }
        else
        {
            existing.Update(snapshot);
        }

        ExplorerStatus = snapshot.StatusText;
    });

    private void OnTransferAdded(object? sender, TransferItem transfer) => Dispatch(() =>
    {
        Transfers.Insert(0, transfer);
        Activities.Insert(0, transfer);
    });

    private void OnTransferChanged(object? sender, TransferItem transfer) => Dispatch(() =>
    {
        if (!Transfers.Contains(transfer))
        {
            Transfers.Insert(0, transfer);
            Activities.Insert(0, transfer);
        }

        CancelTransferCommand.RaiseCanExecuteChanged();
    });

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(EmptyDevicesVisibility));
        OnPropertyChanged(nameof(EmptyTransfersVisibility));
        OnPropertyChanged(nameof(EmptyPendingVisibility));
        OnPropertyChanged(nameof(EmptyActivitiesVisibility));
        OnPropertyChanged(nameof(EmptyExplorerPackagesVisibility));
        OnPropertyChanged(nameof(SendQueueButtonText));
        SendQueuedCommand.RaiseCanExecuteChanged();
        ClearPendingCommand.RaiseCanExecuteChanged();
    }

    private bool TryGetSelectedConnection(out PeerConnection? connection)
    {
        connection = null;
        return SelectedDevice is not null && _host.Connections.TryGetConnection(SelectedDevice.Id, out connection);
    }

    private void RaiseConnectionCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        DisconnectCommand.RaiseCanExecuteChanged();
        SendQueuedCommand.RaiseCanExecuteChanged();
        SendTextClipboardCommand.RaiseCanExecuteChanged();
        SendImageClipboardCommand.RaiseCanExecuteChanged();
        ToggleRemoteMouseCommand.RaiseCanExecuteChanged();
    }

    private static string CreateTextPreview(string text)
    {
        var preview = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return preview.Length <= 120 ? preview : preview[..120] + "…";
    }

    private static string BuildMonitorDetail(MonitorInfoMessage monitor) =>
        string.Join(
            " · ",
            monitor.Monitors.Select((item, index) =>
            {
                var scale = (int)Math.Round(item.DpiX / 96d * 100d);
                var primary = item.IsPrimary ? " 주" : string.Empty;
                return $"{index + 1}{primary}: {item.Width}×{item.Height} {scale}%";
            }));

    private static IReadOnlyList<MonitorDisplayItem> BuildMonitorItems(MonitorInfoMessage monitor) =>
        monitor.Monitors
            .Select((item, index) => new MonitorDisplayItem(
                $"{(item.IsPrimary ? "● " : string.Empty)}{index + 1}",
                $"{item.Width}×{item.Height}",
                $"{(int)Math.Round(item.DpiX / 96d * 100d)}%"))
            .ToArray();

    private void RefreshMonitorTopology()
    {
        MonitorTopologyItems.Clear();
        var localId = _host.Settings.Current.DeviceId;
        AddMonitorGroup(
            _host.RemoteMouse.LocalMonitor,
            $"local:{localId:N}",
            localId,
            $"PC 1 · 이 PC · {DeviceName}",
            "PC 1",
            true,
            0.06d,
            0.34d);

        if (SelectedDevice is not null &&
            _host.RemoteMouse.TryGetRemoteMonitor(SelectedDevice.Id, out var remote) && remote is not null)
        {
            AddMonitorGroup(
                remote,
                $"remote:{SelectedDevice.Id:N}",
                SelectedDevice.Id,
                $"PC 2 · 상대 PC · {SelectedDevice.Name}",
                "PC 2",
                false,
                0.60d,
                0.34d);
            UpdateEdgeFromTopology();
        }
    }

    private void AddMonitorGroup(
        MonitorInfoMessage monitor,
        string groupKey,
        Guid deviceId,
        string computerLabel,
        string computerShortLabel,
        bool isLocal,
        double defaultX,
        double defaultY)
    {
        if (monitor.Monitors.Count == 0)
        {
            return;
        }

        var minX = monitor.Monitors.Min(item => item.X);
        var minY = monitor.Monitors.Min(item => item.Y);
        var maxX = monitor.Monitors.Max(item => item.X + item.Width);
        var maxY = monitor.Monitors.Max(item => item.Y + item.Height);
        var groupWidth = Math.Max(1, maxX - minX);
        var groupHeight = Math.Max(1, maxY - minY);
        var scale = Math.Min(238d / groupWidth, 98d / groupHeight);
        scale = Math.Clamp(scale, 0.025d, 0.15d);

        var placement = GetOrCreatePlacement(deviceId, defaultX, defaultY);
        var anchorX = placement.X * _monitorCanvasWidth;
        var anchorY = placement.Y * _monitorCanvasHeight;
        var visualGroupWidth = groupWidth * scale;
        var visualGroupHeight = groupHeight * scale;
        anchorX = Math.Clamp(anchorX, 8d, Math.Max(8d, _monitorCanvasWidth - visualGroupWidth - 8d));
        anchorY = Math.Clamp(anchorY, 26d, Math.Max(26d, _monitorCanvasHeight - visualGroupHeight - 8d));

        for (var index = 0; index < monitor.Monitors.Count; index++)
        {
            var item = monitor.Monitors[index];
            MonitorTopologyItems.Add(new MonitorTopologyItem(
                groupKey,
                deviceId,
                computerLabel,
                computerShortLabel,
                $"{index + 1}{(item.IsPrimary ? " · 주" : string.Empty)}",
                $"{item.Width}×{item.Height}",
                $"{(int)Math.Round(item.DpiX / 96d * 100d)}%",
                isLocal,
                item.IsPrimary,
                anchorX + (item.X - minX) * scale,
                anchorY + (item.Y - minY) * scale,
                Math.Max(54d, item.Width * scale),
                Math.Max(38d, item.Height * scale)));
        }
    }

    private MonitorPlacementSettings GetOrCreatePlacement(Guid deviceId, double defaultX, double defaultY)
    {
        if (!_host.Settings.Current.MonitorPlacements.TryGetValue(deviceId, out var placement))
        {
            placement = new MonitorPlacementSettings { X = defaultX, Y = defaultY };
            _host.Settings.Current.MonitorPlacements[deviceId] = placement;
        }

        return placement;
    }

    private void UpdateEdgeFromTopology()
    {
        var local = MonitorTopologyItems.Where(item => item.IsLocal).ToArray();
        var remote = MonitorTopologyItems.Where(item => !item.IsLocal).ToArray();
        if (local.Length == 0 || remote.Length == 0)
        {
            return;
        }

        var localCenter = (local.Min(item => item.Left) + local.Max(item => item.Left + item.Width)) / 2d;
        var remoteCenter = (remote.Min(item => item.Left) + remote.Max(item => item.Left + item.Width)) / 2d;
        SelectedMouseEdge = remoteCenter < localCenter ? "왼쪽" : "오른쪽";
    }

    private void AddClipboardHistory(string name, string detail, string status)
    {
        Activities.Insert(0, new ClipboardHistoryItem(DateTimeOffset.Now, name, detail, status));
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
