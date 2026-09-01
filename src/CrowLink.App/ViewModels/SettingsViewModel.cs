using CrowLink.Services.Settings;
using CrowLink.Services.Theming;
using CrowLink.Utilities;

namespace CrowLink.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private string _deviceName;
    private string _tcpPort;
    private string _discoveryPort;
    private string _receiveFolder;
    private ThemeChoice _selectedTheme;
    private bool _autoApproveConnect;
    private bool _autoApproveShare;
    private bool _autoApproveControl;
    private bool _autoApproveExplorer;
    private bool _enableMobileTouchpad;
    private string _mobileTouchpadPort;
    private double _mobileSensitivity;
    private double _mobileScrollSpeed;
    private bool _mobilePointerAcceleration;
    private bool _mobileLocalNetworkOnly;

    public SettingsViewModel(AppSettings settings)
    {
        _settings = settings;
        _deviceName = settings.DeviceName;
        _tcpPort = settings.TcpPort.ToString();
        _discoveryPort = settings.DiscoveryPort.ToString();
        _receiveFolder = settings.ReceiveFolder;
        ThemeChoices =
        [
            new ThemeChoice(ThemeService.CrowTheme, "Black · Crow"),
            new ThemeChoice(ThemeService.SkyTheme, "Bright Sky Blue"),
        ];
        _selectedTheme = ThemeChoices.First(choice => choice.Key == ThemeService.Normalize(settings.Theme));
        _autoApproveConnect = settings.AutoApproveConnect;
        _autoApproveShare = settings.AutoApproveShare;
        _autoApproveControl = settings.AutoApproveControl;
        _autoApproveExplorer = settings.AutoApproveExplorer;
        _enableMobileTouchpad = settings.EnableMobileTouchpad;
        _mobileTouchpadPort = settings.MobileTouchpadPort.ToString();
        _mobileSensitivity = settings.MobileSensitivity;
        _mobileScrollSpeed = settings.MobileScrollSpeed;
        _mobilePointerAcceleration = settings.MobilePointerAcceleration;
        _mobileLocalNetworkOnly = settings.MobileLocalNetworkOnly;
    }

    public string DeviceName { get => _deviceName; set => SetProperty(ref _deviceName, value); }
    public string TcpPort { get => _tcpPort; set => SetProperty(ref _tcpPort, value); }
    public string DiscoveryPort { get => _discoveryPort; set => SetProperty(ref _discoveryPort, value); }
    public string ReceiveFolder { get => _receiveFolder; set => SetProperty(ref _receiveFolder, value); }
    public IReadOnlyList<ThemeChoice> ThemeChoices { get; }
    public ThemeChoice SelectedTheme { get => _selectedTheme; set => SetProperty(ref _selectedTheme, value); }
    public int TrustedDeviceCount => _settings.TrustedDevices.Count;
    public bool AutoApproveConnect { get => _autoApproveConnect; set => SetAutoApproval(ref _autoApproveConnect, value); }
    public bool AutoApproveShare { get => _autoApproveShare; set => SetAutoApproval(ref _autoApproveShare, value); }
    public bool AutoApproveControl { get => _autoApproveControl; set => SetAutoApproval(ref _autoApproveControl, value); }
    public bool AutoApproveExplorer { get => _autoApproveExplorer; set => SetAutoApproval(ref _autoApproveExplorer, value); }
    public bool EnableMobileTouchpad { get => _enableMobileTouchpad; set => SetProperty(ref _enableMobileTouchpad, value); }
    public string MobileTouchpadPort { get => _mobileTouchpadPort; set => SetProperty(ref _mobileTouchpadPort, value); }
    public double MobileSensitivity { get => _mobileSensitivity; set => SetProperty(ref _mobileSensitivity, value); }
    public double MobileScrollSpeed { get => _mobileScrollSpeed; set => SetProperty(ref _mobileScrollSpeed, value); }
    public bool MobilePointerAcceleration { get => _mobilePointerAcceleration; set => SetProperty(ref _mobilePointerAcceleration, value); }
    public bool MobileLocalNetworkOnly { get => _mobileLocalNetworkOnly; set => SetProperty(ref _mobileLocalNetworkOnly, value); }
    public int EnabledAutomationCount => new[] { AutoApproveConnect, AutoApproveShare, AutoApproveControl, AutoApproveExplorer }.Count(value => value);
    public string AutomationStatusText => EnabledAutomationCount == 0 ? "모든 요청을 직접 확인" : $"자동 승인 {EnabledAutomationCount}/4 활성화";

    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(DeviceName))
        {
            throw new InvalidOperationException("장치 이름을 입력하세요.");
        }

        if (!int.TryParse(TcpPort, out var tcpPort) || tcpPort is <= 0 or > 65535 ||
            !int.TryParse(DiscoveryPort, out var discoveryPort) || discoveryPort is <= 0 or > 65535 ||
            !int.TryParse(MobileTouchpadPort, out var mobileTouchpadPort) || mobileTouchpadPort is <= 0 or > 65535)
        {
            throw new InvalidOperationException("포트는 1~65535 사이의 숫자여야 합니다.");
        }

        if (mobileTouchpadPort == tcpPort || mobileTouchpadPort == discoveryPort)
        {
            throw new InvalidOperationException("Mobile 포트는 TCP 및 검색 UDP 포트와 달라야 합니다.");
        }

        if (string.IsNullOrWhiteSpace(ReceiveFolder))
        {
            throw new InvalidOperationException("수신 폴더를 입력하세요.");
        }

        _settings.DeviceName = DeviceName.Trim();
        _settings.TcpPort = tcpPort;
        _settings.DiscoveryPort = discoveryPort;
        _settings.ReceiveFolder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ReceiveFolder.Trim()));
        _settings.Theme = SelectedTheme.Key;
        _settings.AutoApproveConnect = AutoApproveConnect;
        _settings.AutoApproveShare = AutoApproveShare;
        _settings.AutoApproveControl = AutoApproveControl;
        _settings.AutoApproveExplorer = AutoApproveExplorer;
        _settings.EnableMobileTouchpad = EnableMobileTouchpad;
        _settings.MobileTouchpadPort = mobileTouchpadPort;
        _settings.MobileSensitivity = Math.Clamp(MobileSensitivity, 0.5d, 5d);
        _settings.MobileScrollSpeed = Math.Clamp(MobileScrollSpeed, 0.5d, 3d);
        _settings.MobilePointerAcceleration = MobilePointerAcceleration;
        _settings.MobileLocalNetworkOnly = MobileLocalNetworkOnly;
    }

    private void SetAutoApproval(ref bool field, bool value)
    {
        if (SetProperty(ref field, value))
        {
            OnPropertyChanged(nameof(EnabledAutomationCount));
            OnPropertyChanged(nameof(AutomationStatusText));
        }
    }

    public sealed record ThemeChoice(string Key, string DisplayName);
}
