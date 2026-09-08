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
    private string _explorerStatus = "Quick 영역에 파일을 놓으면 상대 PC로 전송할 수 있습니다.";
    private string _connectionStatusText = "연결 안 됨";
    private Brush _connectionStatusBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x88, 0x9B));
    private double _monitorCanvasWidth = 680d;
    private double _monitorCanvasHeight = 220d;
    private ImageSource? _mobileQrImage;
    private readonly HashSet<Guid> _notifiedTransfers = [];
    public event EventHandler<TransferItem>? TransferCompleted;

    public MainViewModel(AppHost host)
    {
        _host = host;
        _mobileTextTimer.Tick += async (_, _) => { _mobileTextTimer.Stop(); await SendMobileTextAsync(); };
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
        SendMobileTextCommand = new AsyncRelayCommand(SendMobileTextAsync, () => IsMobileSessionActive);
        SendMobileClipboardCommand = new AsyncRelayCommand(SendMobileClipboardAsync, () => IsMobileSessionActive);
        CopyMobileReceivedCommand = new RelayCommand(CopyMobileReceived);
        _host.MobileTouchpad.ContentReceived += content => Dispatch(() => ReceiveMobileContent(content));

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
        _host.Clipboard.ResultReceived += (_, result) => Dispatch(() =>
        {
            ClipboardStatus = result.Accepted ? "상대 PC에 클립보드를 적용했습니다." : "상대 PC가 클립보드를 거부했거나 적용하지 못했습니다.";
            AddClipboardHistory($"{result.Kind} 클립보드", ClipboardStatus, result.Accepted ? "전달 확인" : "미적용");
        });
        _host.Connections.MessageReceived += OnPlacementReceivedAsync;
        _host.RemoteMouse.ControlRequested += RequestRemoteMouseApprovalAsync;
        _host.RemoteMouse.StateChanged += OnRemoteMouseStateChanged;
        _host.RemoteMouse.MonitorChanged += OnRemoteMonitorChanged;
        _host.RemoteMouse.QuickTransferEnabled = _host.Settings.Current.EnableQuickTransfer;
        _host.RemoteMouse.QuickDragStarted += (_, _) => Dispatch(() => _quickDragPath = ExplorerSelection.GetSingleSelectedFile());
        _host.RemoteMouse.QuickDropRequested += (_, _) => Dispatch(async () =>
        {
            var path = _quickDragPath; _quickDragPath = null;
            if (path is not null) await SendExplorerPathsAsync([path]);
            else ExplorerStatus = "탐색기에서 지원되는 파일 한 개를 선택해 드래그하세요.";
        });
        _host.Explorer.PackageChanged += OnExplorerPackageChanged;
        _host.MobileTouchpad.PairingRequested += RequestMobilePairingApprovalAsync;
        _host.MobileTouchpad.StateChanged += OnMobileTouchpadStateChanged;
        _host.MobileTouchpad.AutoStopped += OnMobileTouchpadAutoStopped;
        RefreshMobileQr();
        RefreshMonitorTopology();
    }

    public string DeviceName => _host.Settings.Current.DeviceName;
    private string _mobileBrowserText = string.Empty;
    private string _mobileExchangeStatus = "연결 후 텍스트와 이미지를 교환할 수 있습니다.";
    private string _mobileReceivedText = string.Empty;
    private ImageSource? _mobileReceivedImage;
    private byte[]? _mobileReceivedPng;
    private bool _mobileKeyboardLinked;
    private long _mobileTextRevision;
    private readonly System.Windows.Threading.DispatcherTimer _mobileTextTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _autoSelectingDevice, _userSelectedDevice;
    public string MobileBrowserText
    {
        get => _mobileBrowserText;
        set { if (SetProperty(ref _mobileBrowserText, value) && MobileKeyboardLinked && IsMobileSessionActive) { _mobileTextTimer.Stop(); _mobileTextTimer.Start(); } }
    }
    public bool MobileKeyboardLinked
    {
        get => _mobileKeyboardLinked;
        set { if (SetProperty(ref _mobileKeyboardLinked, value)) { _mobileTextTimer.Stop(); if (value && IsMobileSessionActive) _mobileTextTimer.Start(); } }
    }
    public string MobileExchangeStatus { get => _mobileExchangeStatus; private set => SetProperty(ref _mobileExchangeStatus, value); }
    public string MobileReceivedText { get => _mobileReceivedText; private set => SetProperty(ref _mobileReceivedText, value); }
    public ImageSource? MobileReceivedImage { get => _mobileReceivedImage; private set => SetProperty(ref _mobileReceivedImage, value); }
    public AsyncRelayCommand SendMobileClipboardCommand { get; }
    public RelayCommand CopyMobileReceivedCommand { get; }

    private async Task SendMobileTextAsync()
    {
        if (!IsMobileSessionActive) return;
        if (MobileBrowserText.Length > MobileContentLimits.TextCharacters) { MobileExchangeStatus = "텍스트는 100,000자까지 보낼 수 있습니다."; return; }
        var sent = await _host.MobileTouchpad.SendBrowserCommandAsync(new { type = "browserText", text = MobileBrowserText, revision = ++_mobileTextRevision });
        MobileExchangeStatus = sent ? "휴대폰 브라우저로 텍스트를 보냈습니다." : "휴대폰 연결이 끊겨 전송하지 못했습니다.";
    }

    private async Task SendMobileClipboardAsync()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is null) return;
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                var bytes = stream.ToArray();
                if (!MobileContentLimits.IsAllowedPng(bytes)) { MobileExchangeStatus = "이미지는 2MB·1,600만 화소 이하로 보내세요."; return; }
                var sent = await _host.MobileTouchpad.SendBrowserCommandAsync(new { type = "browserImage", png = Convert.ToBase64String(bytes) });
                MobileExchangeStatus = sent ? "휴대폰 브라우저로 이미지를 보냈습니다." : "휴대폰 연결이 끊겨 전송하지 못했습니다.";
            }
            else if (System.Windows.Clipboard.ContainsText())
            {
                MobileBrowserText = System.Windows.Clipboard.GetText();
                _mobileTextTimer.Stop();
                await SendMobileTextAsync();
            }
            else MobileExchangeStatus = "클립보드에 텍스트 또는 이미지가 없습니다.";
        }
        catch (Exception exception) { MobileExchangeStatus = $"클립보드를 보내지 못했습니다: {exception.Message}"; }
    }

    private void ReceiveMobileContent(MobileSharedContent content)
    {
        if (_host.MobileTouchpad.Session?.SessionId != content.SessionId) return;
        try
        {
            _mobileReceivedPng = content.Png;
            MobileReceivedText = content.Text ?? string.Empty;
            MobileReceivedImage = null;
            if (content.Png is { } png)
            {
                using var stream = new MemoryStream(png, false);
                var bitmap = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                bitmap.Freeze();
                MobileReceivedImage = bitmap;
            }
            MobileExchangeStatus = "휴대폰에서 수신했습니다. ‘받은 내용 복사’를 눌러 사용하세요.";
            AddClipboardHistory("모바일 클립보드", content.Png is null ? "텍스트 수신" : "이미지 수신", "수신 완료");
        }
        catch (Exception exception) { _mobileReceivedPng = null; MobileExchangeStatus = $"수신 이미지를 표시하지 못했습니다: {exception.Message}"; }
    }

    private void CopyMobileReceived()
    {
        try
        {
            if (_mobileReceivedPng is not null && MobileReceivedImage is BitmapSource image) System.Windows.Clipboard.SetImage(image);
            else System.Windows.Clipboard.SetText(MobileReceivedText);
            MobileExchangeStatus = "받은 내용을 PC 클립보드에 복사했습니다.";
        }
        catch (Exception exception) { MobileExchangeStatus = $"복사하지 못했습니다: {exception.Message}"; }
    }

    public AsyncRelayCommand SendMobileTextCommand { get; }
    public Task SendMobilePointerAsync(double x, double y, bool click) =>
        _host.MobileTouchpad.SendBrowserCommandAsync(new { type = "browserPointer", x, y, click });
    private string? _quickDragPath;
    public bool QuickTransferEnabled
    {
        get => _host.Settings.Current.EnableQuickTransfer;
        set
        {
            _host.Settings.Current.EnableQuickTransfer = value;
            _host.RemoteMouse.QuickTransferEnabled = value;
            OnPropertyChanged();
            _ = _host.Settings.SaveAsync();
        }
    }
    public bool IsSkyTheme => _host.Theme.CurrentTheme != ThemeService.CrowTheme;
    public string AutomationSummary
    {
        get
        {
            var count = new[]
            {
                _host.Settings.Current.AutoApproveConnect,
                _host.Settings.Current.AutoApproveShare,
                _host.Settings.Current.AutoApproveControl,
                _host.Settings.Current.EnableQuickTransfer,
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
                if (!_autoSelectingDevice && value is not null) _userSelectedDevice = true;
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

    public async Task SaveMonitorTopologyAsync()
    {
        if (_host.RemoteMouse.IsActive) await _host.RemoteMouse.StopAsync();
        await _host.Settings.SaveAsync();
        if (TryGetSelectedConnection(out var connection) && connection is not null)
        {
            var local = GetOrCreatePlacement(_host.Settings.Current.DeviceId, 0.06, 0.34);
            var remote = GetOrCreatePlacement(connection.Device.Id, 0.60, 0.34);
            await connection.SendJsonAsync(MessageType.MonitorPlacement,
                new MonitorPlacementMessage(local.X, local.Y, remote.X, remote.Y), CancellationToken.None);
        }
    }

    private Task OnPlacementReceivedAsync(PeerMessageEventArgs args)
    {
        if (args.Message.Type != MessageType.MonitorPlacement) return Task.CompletedTask;
        var layout = ProtocolSerializer.Deserialize<MonitorPlacementMessage>(args.Message);
        if (new[] { layout.SenderX, layout.SenderY, layout.ReceiverX, layout.ReceiverY }
            .Any(value => !double.IsFinite(value) || value < 0 || value > 1)) return Task.CompletedTask;
        return Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            _host.Settings.Current.MonitorPlacements[args.Connection.Device.Id] = new MonitorPlacementSettings { X = layout.SenderX, Y = layout.SenderY };
            _host.Settings.Current.MonitorPlacements[_host.Settings.Current.DeviceId] = new MonitorPlacementSettings { X = layout.ReceiverX, Y = layout.ReceiverY };
            RefreshMonitorTopology();
            await _host.Settings.SaveAsync();
        }).Task.Unwrap();
    }

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
            MessageBox.Show("Quick 파일을 보낼 연결 장치를 선택하세요.", "CrowLink", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            ExplorerStatus = $"{connection.Device.Name}에 Quick 파일을 전송하는 중";
            await _host.Explorer.SendPackageAsync(connection, paths).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ExplorerStatus = $"Quick 전송 실패: {exception.Message}";
            await _host.Log.ErrorAsync("Explorer bridge send failed", exception).ConfigureAwait(true);
            MessageBox.Show(exception.Message, "CrowLink Quick", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            ExplorerStatus = "탐색기 대상 폴더에 놓으세요. Esc를 누르면 취소됩니다.";
            var copied = OleExplorerDragService.StartFileDrop(package.LocalPaths);
            if (!copied)
            {
                ExplorerStatus = "드래그가 취소되었습니다. 받은 파일은 유지됩니다.";
                return;
            }

            ExplorerStatus = "원하는 폴더에 복사했습니다. 수신 원본은 유지됩니다.";
            await _host.Log.InfoAsync("Quick-transfer received file copied to Explorer");
        }
        catch (Exception exception)
        {
            ExplorerStatus = $"파일 드래그 실패: {exception.Message}";
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

    public Task StopRemoteInputAsync() => _host.RemoteMouse.StopAsync();

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
            await SaveMonitorTopologyAsync().ConfigureAwait(true);
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
            _host.RemoteMouse.QuickTransferEnabled = _host.Settings.Current.EnableQuickTransfer;
            OnPropertyChanged(nameof(QuickTransferEnabled));
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
                Application.Current.MainWindow,
                $"{request.Connection.Device.Name}에서 키보드·마우스 공유를 요청했습니다.\n\n수정 버전끼리는 한 번의 승인으로 양쪽 PC의 입력을 공유합니다. 요청 PC의 {edge} 경계와 이 PC의 반대 경계를 사용합니다.\nCtrl+Alt+Esc로 양쪽 공유를 즉시 종료합니다.\n\n이번 입력 공유를 허용하시겠습니까?",
                "CrowLink 원격 입력 요청",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }).Task;
    }

    private Task<bool> RequestMobilePairingApprovalAsync(MobilePairingRequest request) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                Application.Current.MainWindow,
                $"{request.DeviceName}에서 이 PC의 마우스 제어를 요청했습니다.\n\n주소: {request.Address}\n장치 유형: Mobile Browser\n펜 입력과 텍스트·이미지 교환을 사용할 수 있습니다.\n\n이번 모바일 연결을 허용하시겠습니까?",
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
        SendMobileTextCommand.RaiseCanExecuteChanged();
        SendMobileClipboardCommand.RaiseCanExecuteChanged();
        if (!IsMobileSessionActive) { MobileKeyboardLinked = false; _mobileTextTimer.Stop(); }
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
        AddClipboardHistory($"{content.Kind} 클립보드", content.Connection.Device.Name, "수신 · 승인 대기");
        var description = content.Kind == ClipboardContentKind.Text
            ? $"텍스트: {CreateTextPreview(content.Text!)}"
            : $"PNG 이미지 ({FormatUtilities.FormatBytes(content.ImagePng!.Length)})";
        if (!_host.Settings.Current.AutoApproveShare)
        {
            var result = MessageBox.Show(
                Application.Current.MainWindow,
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

        ClipboardStatus = "Windows 클립보드가 다른 프로그램에서 사용 중입니다. 다시 전송해 주세요.";
        AddClipboardHistory($"{content.Kind} 클립보드", lastError?.Message ?? ClipboardStatus, "적용 실패");
        return false;
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
        SelectDiscoveredDevice();
    });

    private void OnDeviceExpired(object? sender, Guid deviceId) => Dispatch(() =>
    {
        var device = Devices.FirstOrDefault(item => item.Id == deviceId);
        if (device is not null && device.State != ConnectionState.Connected)
        {
            var wasSelected = SelectedDevice?.Id == deviceId;
            Devices.Remove(device);
            if (wasSelected) _userSelectedDevice = false;
            SelectDiscoveredDevice();
        }
    });

    private void SelectDiscoveredDevice()
    {
        if (SelectedDevice is not null && Devices.Contains(SelectedDevice) && (_userSelectedDevice || HasSelectedConnection)) return;
        var candidate = Devices.FirstOrDefault(d => d.Id == _host.Settings.Current.LastConnectedDeviceId)
            ?? Devices.FirstOrDefault(d => _host.Settings.Current.TrustedDevices.Contains(d.Id))
            ?? Devices.FirstOrDefault();
        _autoSelectingDevice = true;
        try { SelectedDevice = candidate; } finally { _autoSelectingDevice = false; }
    }

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
        _host.Settings.Current.LastConnectedDeviceId = device.Id;
        _ = _host.Settings.SaveAsync();
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
        if (transfer.Status == TransferStatus.Completed && _notifiedTransfers.Add(transfer.BatchId))
            TransferCompleted?.Invoke(this, transfer);
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
                item.Width * scale,
                item.Height * scale));
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
