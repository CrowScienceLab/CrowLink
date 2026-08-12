using CrowLink.Services.Discovery;
using CrowLink.Services.Clipboard;
using CrowLink.Services.Logging;
using CrowLink.Services.Network;
using CrowLink.Services.Security;
using CrowLink.Services.Settings;
using CrowLink.Services.Transfer;
using CrowLink.Services.Theming;
using CrowLink.Services.RemoteMouse;
using CrowLink.Services.Explorer;

namespace CrowLink.Services;

public sealed class AppHost : IAsyncDisposable
{
    private AppHost(
        SettingsService settings,
        LogService log,
        IDeviceDiscoveryService discovery,
        PairingService pairing,
        ConnectionService connections,
        FileTransferService transfers,
        ClipboardSharingService clipboard,
        RemoteMouseService remoteMouse,
        ExplorerBridgeService explorer,
        ThemeService theme)
    {
        Settings = settings;
        Log = log;
        Discovery = discovery;
        Pairing = pairing;
        Connections = connections;
        Transfers = transfers;
        Clipboard = clipboard;
        RemoteMouse = remoteMouse;
        Explorer = explorer;
        Theme = theme;
    }

    public SettingsService Settings { get; }
    public LogService Log { get; }
    public IDeviceDiscoveryService Discovery { get; }
    public PairingService Pairing { get; }
    public ConnectionService Connections { get; }
    public FileTransferService Transfers { get; }
    public ClipboardSharingService Clipboard { get; }
    public RemoteMouseService RemoteMouse { get; }
    public ExplorerBridgeService Explorer { get; }
    public ThemeService Theme { get; }

    public static async Task<AppHost> CreateAsync(CancellationToken cancellationToken = default, string? settingsPath = null)
    {
        var settings = new SettingsService(settingsPath);
        var appSettings = await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var log = new LogService();
        await log.InfoAsync("Application started").ConfigureAwait(false);
        var discovery = new DeviceDiscoveryService(appSettings, log);
        var pairing = new PairingService(settings, log);
        var connections = new ConnectionService(appSettings, pairing, log);
        var transfers = new FileTransferService(appSettings, connections, log);
        var clipboard = new ClipboardSharingService(connections, log);
        var remoteMouse = new RemoteMouseService(connections, log);
        var explorer = new ExplorerBridgeService(connections, transfers, log, appSettings);
        var theme = new ThemeService();
        return new AppHost(settings, log, discovery, pairing, connections, transfers, clipboard, remoteMouse, explorer, theme);
    }

    public async ValueTask DisposeAsync()
    {
        await Explorer.DisposeAsync().ConfigureAwait(false);
        await Clipboard.DisposeAsync().ConfigureAwait(false);
        await RemoteMouse.DisposeAsync().ConfigureAwait(false);
        await Transfers.DisposeAsync().ConfigureAwait(false);
        await Discovery.DisposeAsync().ConfigureAwait(false);
        await Connections.DisposeAsync().ConfigureAwait(false);
        await Settings.SaveAsync().ConfigureAwait(false);
        await Log.InfoAsync("Application stopped").ConfigureAwait(false);
        await Log.DisposeAsync().ConfigureAwait(false);
    }
}
