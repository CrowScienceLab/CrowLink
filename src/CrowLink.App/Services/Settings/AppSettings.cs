namespace CrowLink.Services.Settings;

public sealed class AppSettings
{
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public string DeviceName { get; set; } = Environment.MachineName;
    public int TcpPort { get; set; } = 45100;
    public int DiscoveryPort { get; set; } = 45101;
    public string ReceiveFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "CrowLink");
    public int ChunkSizeBytes { get; set; } = 1024 * 1024;
    public string Theme { get; set; } = "sky";
    public int ColorSchemeVersion { get; set; }
    public bool AutoApproveConnect { get; set; }
    public bool AutoApproveShare { get; set; }
    public bool AutoApproveControl { get; set; }
    public bool AutoApproveExplorer { get; set; }
    public bool EnableMobileTouchpad { get; set; }
    public int MobileTouchpadPort { get; set; } = 45102;
    public double MobileSensitivity { get; set; } = 1.6;
    public double MobileScrollSpeed { get; set; } = 1.0;
    public bool MobilePointerAcceleration { get; set; } = true;
    public bool MobileLocalNetworkOnly { get; set; } = true;
    public HashSet<Guid> TrustedDevices { get; set; } = [];
    public double WindowWidth { get; set; } = 760;
    public double WindowHeight { get; set; } = 560;
    public Dictionary<Guid, MonitorPlacementSettings> MonitorPlacements { get; set; } = [];
}

public sealed class MonitorPlacementSettings
{
    public double X { get; set; }
    public double Y { get; set; }
}
