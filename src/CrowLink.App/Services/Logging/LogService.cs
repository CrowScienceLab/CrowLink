using System.Text;

namespace CrowLink.Services.Logging;

public sealed class LogService : IAsyncDisposable
{
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LogService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrowLink", "logs");
        _logPath = Path.Combine(directory, "crowlink.log");
    }

    public Task InfoAsync(string message) => WriteAsync("INFO", message);
    public Task WarningAsync(string message) => WriteAsync("WARN", message);
    public Task ErrorAsync(string message, Exception? exception = null) =>
        WriteAsync("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private async Task WriteAsync(string level, string message)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(directory);
            RotateIfNeeded();
            var sanitized = message.Replace('\r', ' ').Replace('\n', ' ');
            var line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logPath, line, Encoding.UTF8).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Logging must never terminate the application.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must never terminate the application.
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_logPath);
        if (!file.Exists || file.Length < MaxLogBytes)
        {
            return;
        }

        var archive = Path.Combine(file.DirectoryName!, $"crowlink-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
        File.Move(_logPath, archive);
        foreach (var oldLog in new DirectoryInfo(file.DirectoryName!).GetFiles("crowlink-*.log").OrderByDescending(item => item.LastWriteTimeUtc).Skip(4))
        {
            oldLog.Delete();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
