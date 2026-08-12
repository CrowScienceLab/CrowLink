using CrowLink.Utilities;

namespace CrowLink.Models;

public sealed class PendingTransferItem : ObservableObject
{
    public PendingTransferItem(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        DisplayName = System.IO.Path.GetFileName(Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));
        IsDirectory = Directory.Exists(Path);
    }

    public string Path { get; }
    public string DisplayName { get; }
    public bool IsDirectory { get; }
    public string KindText => IsDirectory ? "폴더" : "파일";
}
