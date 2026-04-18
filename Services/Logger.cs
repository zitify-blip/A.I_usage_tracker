using System.IO;

namespace ClaudeUsageTracker.Services;

/// <summary>
/// Lightweight file logger writing to %AppData%\ClaudeUsageTracker\logs\app.log.
/// Rotates the file when it exceeds 1 MB (keeps one previous as app.log.old).
/// </summary>
public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTracker", "logs");

    private static readonly string LogPath = Path.Combine(LogDir, "app.log");
    private static readonly string OldLogPath = Path.Combine(LogDir, "app.log.old");
    private const long MaxBytes = 1_000_000;

    private static readonly object _lock = new();

    public static void Info(string msg) => Write("INFO", msg, null);
    public static void Warn(string msg, Exception? ex = null) => Write("WARN", msg, ex);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    private static void Write(string level, string msg, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
                if (ex != null)
                    line += $" | {ex.GetType().Name}: {ex.Message}";

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logger must never throw
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length < MaxBytes) return;
            if (File.Exists(OldLogPath)) File.Delete(OldLogPath);
            File.Move(LogPath, OldLogPath);
        }
        catch { }
    }

    public static string LogFilePath => LogPath;
}
