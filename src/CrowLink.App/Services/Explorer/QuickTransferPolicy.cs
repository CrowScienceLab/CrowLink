namespace CrowLink.Services.Explorer;

public static class QuickTransferPolicy
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".hwp", ".hwpx", ".pdf", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff",
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".mp3", ".wav", ".m4a", ".flac", ".zip",
    };
    public static bool IsAllowed(string name) => !string.IsNullOrWhiteSpace(name) &&
        name == Path.GetFileName(name) && !name.Contains(':') && Extensions.Contains(Path.GetExtension(name));
}
