namespace SelectTranslate.Core.Logging;

/// <summary>
/// 轻量文件日志：按天滚动、保留 7 天、线程安全。
/// 只记录时间、级别、事件与错误类型；绝不记录原文、译文或 Key。
/// </summary>
public sealed class AppLog : IDisposable
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly int _minLevel;

    private StreamWriter? _writer;
    private DateTime _writerDate;
    private bool _disposed;

    public AppLog(string directory, string level)
    {
        _directory = directory;
        _minLevel = ParseLevel(level);
        Directory.CreateDirectory(_directory);
        Cleanup();
    }

    public string DirectoryPath => _directory;

    public void Debug(string message) => Write(0, "DEBUG", message);

    public void Info(string message) => Write(1, "INFO", message);

    public void Warn(string message) => Write(2, "WARN", message);

    public void Error(string message, Exception? exception = null)
        => Write(3, "ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    private void Write(int level, string tag, string message)
    {
        if (level < _minLevel || _disposed)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {tag} {message}");
                _writer.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 日志失败不能影响主流程。
            }
        }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Today;
        if (_writer is not null && _writerDate == today)
        {
            return;
        }

        _writer?.Dispose();
        _writerDate = today;

        var path = Path.Combine(_directory, $"app-{today:yyyyMMdd}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    private void Cleanup()
    {
        try
        {
            var cutoff = DateTime.Now - Retention;
            foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 清理失败忽略。
        }
    }

    private static int ParseLevel(string level) => level?.ToLowerInvariant() switch
    {
        "debug" => 0,
        "warn" => 2,
        "error" => 3,
        _ => 1,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}