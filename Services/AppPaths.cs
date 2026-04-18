using System.IO;

namespace AIUsageTracker.Services;

/// <summary>
/// Resolves per-user storage root and performs a one-time migration from the
/// legacy ClaudeUsageTracker folder to AI_usage_tracker on first launch.
/// </summary>
public static class AppPaths
{
    private const string OldDirName = "ClaudeUsageTracker";
    private const string NewDirName = "AI_usage_tracker";

    public static string Root { get; } = Resolve();

    public static string LogsDir => Path.Combine(Root, "logs");

    private static string Resolve()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var newRoot = Path.Combine(appData, NewDirName);
        var oldRoot = Path.Combine(appData, OldDirName);

        try
        {
            if (!Directory.Exists(newRoot) && Directory.Exists(oldRoot))
                CopyDirectory(oldRoot, newRoot);
        }
        catch
        {
            // Migration must never block startup; fall through to new path.
        }

        Directory.CreateDirectory(newRoot);
        return newRoot;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var sub in Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(sub);
            CopyDirectory(sub, Path.Combine(dest, name));
        }
    }
}
