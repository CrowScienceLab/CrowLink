using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace CrowLink.Services.Updates;

public static class UpdateService
{
    public const string ReleasesUrl = "https://github.com/CrowScienceLab/CrowLink/releases/latest";
    private const string LatestReleaseApi = "https://api.github.com/repos/CrowScienceLab/CrowLink/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CrowLink", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new UpdateCheckResult(UpdateCheckState.SignInRequired, CurrentVersion, null, ReleasesUrl, null);
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        var pageUrl = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() : ReleasesUrl;
        var latest = ParseVersion(tag);
        string? installerUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                if (name?.Contains("Setup", StringComparison.OrdinalIgnoreCase) == true &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var urlElement))
                {
                    installerUrl = urlElement.GetString();
                    break;
                }
            }
        }

        if (latest is null)
        {
            return new UpdateCheckResult(UpdateCheckState.Unknown, CurrentVersion, null, pageUrl ?? ReleasesUrl, installerUrl);
        }

        var state = latest > CurrentVersion ? UpdateCheckState.UpdateAvailable : UpdateCheckState.Current;
        return new UpdateCheckResult(state, CurrentVersion, latest, pageUrl ?? ReleasesUrl, installerUrl);
    }

    public static void OpenDownload(UpdateCheckResult result) => Open(result.InstallerUrl ?? result.ReleasePageUrl);

    public static void OpenReleasePage() => Open(ReleasesUrl);

    private static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : new Version(1, 5, 0);

    private static Version? ParseVersion(string? value)
    {
        var normalized = value?.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : null;
    }

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    Version CurrentVersion,
    Version? LatestVersion,
    string ReleasePageUrl,
    string? InstallerUrl);

public enum UpdateCheckState
{
    Current,
    UpdateAvailable,
    SignInRequired,
    Unknown,
}
