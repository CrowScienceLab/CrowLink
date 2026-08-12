namespace CrowLink.Utilities;

public static class PathSecurity
{
    public static string GetSafeDestination(string receiveRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("The target path is invalid.");
        }

        var normalizedRoot = Path.GetFullPath(receiveRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The target path escapes the receive folder.");
        }

        return destination;
    }

    public static string GetAvailablePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("The path has no parent directory.");
        var extension = Path.GetExtension(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var isDirectory = Directory.Exists(path);

        for (var index = 1; index < 10_000; index++)
        {
            var name = isDirectory ? $"{Path.GetFileName(path)} ({index})" : $"{baseName} ({index}){extension}";
            var candidate = Path.Combine(directory, name);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to create a non-conflicting file name.");
    }
}
