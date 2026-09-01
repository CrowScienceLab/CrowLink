using System.Text.Json;
using CrowLink.Services.Theming;

namespace CrowLink.Services.Settings;

public sealed class SettingsService
{
    private const int CurrentColorSchemeVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(string? settingsPath = null)
    {
        var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrowLink");
        _settingsPath = settingsPath ?? Path.Combine(appFolder, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Current = new AppSettings();
                Validate(Current);
                await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
                return Current;
            }

            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new AppSettings();
            Validate(Current);
            return Current;
        }
        catch (JsonException)
        {
            var backup = _settingsPath + ".invalid-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
            File.Move(_settingsPath, backup, true);
            Current = new AppSettings();
            Validate(Current);
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Validate(Current);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.DeviceId == Guid.Empty)
        {
            settings.DeviceId = Guid.NewGuid();
        }

        settings.DeviceName = string.IsNullOrWhiteSpace(settings.DeviceName) ? Environment.MachineName : settings.DeviceName.Trim();
        settings.TcpPort = ValidatePort(settings.TcpPort, 45100);
        settings.DiscoveryPort = ValidatePort(settings.DiscoveryPort, 45101);
        settings.MobileTouchpadPort = ValidatePort(settings.MobileTouchpadPort, 45102);
        if (settings.MobileTouchpadPort == settings.TcpPort || settings.MobileTouchpadPort == settings.DiscoveryPort)
        {
            settings.MobileTouchpadPort = 45102;
        }

        settings.MobileSensitivity = double.IsFinite(settings.MobileSensitivity)
            ? Math.Clamp(settings.MobileSensitivity, 0.5d, 5d)
            : 1.6d;
        settings.MobileScrollSpeed = double.IsFinite(settings.MobileScrollSpeed)
            ? Math.Clamp(settings.MobileScrollSpeed, 0.5d, 3d)
            : 1d;
        settings.ChunkSizeBytes = Math.Clamp(settings.ChunkSizeBytes, 64 * 1024, 4 * 1024 * 1024);
        if (settings.ColorSchemeVersion < CurrentColorSchemeVersion)
        {
            settings.Theme = ThemeService.SkyTheme;
            settings.ColorSchemeVersion = CurrentColorSchemeVersion;
        }
        else
        {
            settings.Theme = ThemeService.Normalize(settings.Theme);
        }
        if (string.IsNullOrWhiteSpace(settings.ReceiveFolder))
        {
            settings.ReceiveFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "CrowLink");
        }

        settings.ReceiveFolder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.ReceiveFolder));
        settings.TrustedDevices ??= [];
        settings.MonitorPlacements ??= [];
        foreach (var placement in settings.MonitorPlacements.Values)
        {
            placement.X = double.IsFinite(placement.X) ? Math.Clamp(placement.X, 0d, 1d) : 0d;
            placement.Y = double.IsFinite(placement.Y) ? Math.Clamp(placement.Y, 0d, 1d) : 0d;
        }
    }

    private static int ValidatePort(int value, int defaultValue) => value is > 0 and <= 65535 ? value : defaultValue;
}
