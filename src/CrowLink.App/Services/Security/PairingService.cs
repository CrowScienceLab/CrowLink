using CrowLink.Services.Logging;
using CrowLink.Services.Settings;

namespace CrowLink.Services.Security;

public sealed class PairingService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;

    public PairingService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    public event Func<PairingRequest, Task<bool>>? ApprovalRequested;

    public bool IsTrusted(Guid deviceId) => _settings.Current.TrustedDevices.Contains(deviceId);

    public async Task TrustAsync(Guid deviceId)
    {
        if (deviceId != Guid.Empty && _settings.Current.TrustedDevices.Add(deviceId))
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
    }

    public async Task<bool> RequestApprovalAsync(PairingRequest request)
    {
        if (_settings.Current.AutoApproveConnect)
        {
            await TrustAsync(request.DeviceId).ConfigureAwait(false);
            await _log.InfoAsync($"Pair request auto-accepted by setting: {request.DeviceName} ({request.DeviceId})").ConfigureAwait(false);
            return true;
        }

        // A trusted id is informational only. CrowLink deliberately asks the receiving
        // user on every new TCP connection so a previous approval never becomes consent
        // for a later session.
        var handlers = ApprovalRequested?.GetInvocationList().Cast<Func<PairingRequest, Task<bool>>>().ToArray();
        if (handlers is null || handlers.Length == 0)
        {
            await _log.WarningAsync($"Pair request rejected because no approval UI is available: {request.DeviceName}").ConfigureAwait(false);
            return false;
        }

        foreach (var handler in handlers)
        {
            if (!await handler(request).ConfigureAwait(false))
            {
                await _log.InfoAsync($"Pair request rejected: {request.DeviceName} ({request.DeviceId})").ConfigureAwait(false);
                return false;
            }
        }

        await TrustAsync(request.DeviceId).ConfigureAwait(false);
        await _log.InfoAsync($"Pair request accepted: {request.DeviceName} ({request.DeviceId})").ConfigureAwait(false);
        return true;
    }
}
