namespace CrowLink.Models;

public sealed record ClipboardHistoryItem(
    DateTimeOffset Timestamp,
    string DisplayName,
    string DetailText,
    string StatusText);
