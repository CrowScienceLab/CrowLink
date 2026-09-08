namespace CrowLink;

public static class AppIdentity
{
    public static Version Version => typeof(AppIdentity).Assembly.GetName().Version is { } version
        ? new Version(version.Major, version.Minor, Math.Max(0, version.Build)) : new Version(1, 8, 1);
    public static string Title => $"CrowLink {Version.ToString(3)}";
    public static string SettingsTitle => $"{Title} 설정";
    public static string SettingsSubtitle => $"{Title} · 연결, 승인 및 Mobile";
    public static string HelpTitle => $"{Title} 도움말";
}
