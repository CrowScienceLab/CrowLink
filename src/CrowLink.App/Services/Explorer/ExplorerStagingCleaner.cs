namespace CrowLink.Services.Explorer;

public static class ExplorerStagingCleaner
{
    public static void DeleteRoots(string receiveFolder, IEnumerable<string> roots)
    {
        var receiveRoot = Path.GetFullPath(receiveFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var receivePrefix = receiveRoot + Path.DirectorySeparatorChar;
        var validated = roots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var fullPath in validated)
        {
            if (!fullPath.StartsWith(receivePrefix, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(receiveRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Explorer staging 경로가 수신 폴더 밖에 있습니다.");
            }
        }

        foreach (var root in validated.OrderByDescending(path => path.Length))
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
            else if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
