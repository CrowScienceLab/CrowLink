using System.Windows;
using System.Windows.Media;

namespace CrowLink.Services.Theming;

public sealed class ThemeService
{
    public const string CrowTheme = "crow";
    public const string SkyTheme = "sky";

    private static readonly IReadOnlyDictionary<string, string> CrowPalette = new Dictionary<string, string>
    {
        ["PageBrush"] = "#050608",
        ["CardBrush"] = "#0B0E13",
        ["InkBrush"] = "#F2F5F8",
        ["MutedBrush"] = "#8793A1",
        ["AccentBrush"] = "#55C7F3",
        ["AccentDarkBrush"] = "#126A8B",
        ["AccentSoftBrush"] = "#123443",
        ["SuccessBrush"] = "#46D7A0",
        ["BorderBrush"] = "#232A34",
        ["SecondaryBrush"] = "#11151C",
        ["SecondaryTextBrush"] = "#F2F5F8",
        ["DropZoneBrush"] = "#090F15",
        ["DropZoneActiveBrush"] = "#123443",
        ["DangerBrush"] = "#E17A82",
        ["WhiteBrush"] = "#FFFFFF",
        ["ShellBrush"] = "#080A0E",
        ["SurfaceBrush"] = "#0C0F14",
        ["SurfaceRaisedBrush"] = "#11161E",
        ["LineBrush"] = "#242B35",
        ["SoftTextBrush"] = "#8793A1",
        ["VioletBrush"] = "#9689FF",
        ["NavHoverBrush"] = "#111821",
        ["NavSelectedBrush"] = "#10222B",
        ["NavSelectedBorderBrush"] = "#24576D",
        ["HeaderIconBrush"] = "#0E151C",
        ["StatusPanelBrush"] = "#0D1218",
        ["AutomationPanelBrush"] = "#11161D",
        ["InfoPanelBrush"] = "#0D1D25",
        ["InfoBorderBrush"] = "#1F4657",
        ["QueueItemBrush"] = "#101820",
        ["QueueItemBorderBrush"] = "#243A48",
        ["ExplorerPanelBrush"] = "#100F19",
        ["ExplorerAccentBrush"] = "#A99EFF",
        ["MonitorGridBrush"] = "#080B10",
        ["MonitorGridLineBrush"] = "#111820",
        ["MonitorLocalBrush"] = "#10212A",
        ["MonitorRemoteBrush"] = "#18172A",
        ["ToolTipBrush"] = "#11161E",
        ["ToolTipBorderBrush"] = "#34404D",
        ["AccentTextBrush"] = "#75D8FA",
    };

    private static readonly IReadOnlyDictionary<string, string> SkyPalette = new Dictionary<string, string>
    {
        ["PageBrush"] = "#DFF6FF",
        ["CardBrush"] = "#FFFFFF",
        ["InkBrush"] = "#15384A",
        ["MutedBrush"] = "#5D7E8E",
        ["AccentBrush"] = "#37B7E6",
        ["AccentDarkBrush"] = "#1595C7",
        ["AccentSoftBrush"] = "#CBEFFF",
        ["SuccessBrush"] = "#2CB982",
        ["BorderBrush"] = "#B8DDED",
        ["SecondaryBrush"] = "#EDF8FC",
        ["SecondaryTextBrush"] = "#15384A",
        ["DropZoneBrush"] = "#F2FBFF",
        ["DropZoneActiveBrush"] = "#CBEFFF",
        ["DangerBrush"] = "#D96878",
        ["WhiteBrush"] = "#FFFFFF",
        ["ShellBrush"] = "#F7FCFF",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceRaisedBrush"] = "#EFF9FF",
        ["LineBrush"] = "#B8DDED",
        ["SoftTextBrush"] = "#5D7E8E",
        ["VioletBrush"] = "#8B80D8",
        ["NavHoverBrush"] = "#E6F6FD",
        ["NavSelectedBrush"] = "#CBEFFF",
        ["NavSelectedBorderBrush"] = "#8DD8F4",
        ["HeaderIconBrush"] = "#E1F6FF",
        ["StatusPanelBrush"] = "#F0FAFE",
        ["AutomationPanelBrush"] = "#E7F4FA",
        ["InfoPanelBrush"] = "#DDF5FF",
        ["InfoBorderBrush"] = "#9CDCF2",
        ["QueueItemBrush"] = "#EEF9FE",
        ["QueueItemBorderBrush"] = "#C3E7F4",
        ["ExplorerPanelBrush"] = "#F6F0FF",
        ["ExplorerAccentBrush"] = "#8B80D8",
        ["MonitorGridBrush"] = "#EAF7FC",
        ["MonitorGridLineBrush"] = "#D4ECF5",
        ["MonitorLocalBrush"] = "#D8F3FF",
        ["MonitorRemoteBrush"] = "#EEE9FF",
        ["ToolTipBrush"] = "#FFFFFF",
        ["ToolTipBorderBrush"] = "#B8DDED",
        ["AccentTextBrush"] = "#167DA5",
    };

    public string CurrentTheme { get; private set; } = CrowTheme;

    public void Apply(string? themeName)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => Apply(themeName));
            return;
        }

        CurrentTheme = Normalize(themeName);
        var palette = CurrentTheme == SkyTheme ? SkyPalette : CrowPalette;
        foreach (var entry in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(entry.Value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[entry.Key] = brush;
        }
    }

    public static string Normalize(string? themeName) =>
        string.Equals(themeName, SkyTheme, StringComparison.OrdinalIgnoreCase) ? SkyTheme : CrowTheme;
}
