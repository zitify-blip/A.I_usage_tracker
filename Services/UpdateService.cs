using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClaudeUsageTracker.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
}

public class UpdateService
{
    // GitHub owner/repo — change this to your actual repo
    private const string GitHubOwner = "zitify-blip";
    private const string GitHubRepo = "zitify_claude_usage_tracker";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeUsageTracker/1.0");
    }

    public static string CurrentVersion
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
        }
    }

    /// <summary>
    /// Check GitHub Releases for a newer version.
    /// Returns UpdateInfo if update available, null otherwise.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(GitHubApiUrl);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);

            var tagName = doc.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = tagName.TrimStart('v', 'V');
            var body = doc.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";

            if (!IsNewer(latestVersion, CurrentVersion))
                return null;

            // Find installer asset (.exe or .msi)
            string downloadUrl = "";
            string fileName = "";

            if (doc.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var lower = name.ToLowerInvariant();
                    if (lower.EndsWith(".exe") || lower.EndsWith(".msi") || lower.EndsWith(".zip"))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        fileName = name;
                        // Prefer .exe > .msi > .zip
                        if (lower.EndsWith(".exe")) break;
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            return new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                ReleaseNotes = body.Length > 300 ? body[..300] + "..." : body
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Download the installer to temp folder and launch it.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int>? onProgress = null)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ClaudeUsageTrackerUpdate");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, info.FileName);

            using var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloadedBytes = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var pct = (int)(downloadedBytes * 100 / totalBytes);
                    onProgress?.Invoke(pct);
                }
            }

            onProgress?.Invoke(100);

            // Launch the installer
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(latest, out var vLatest) && Version.TryParse(current, out var vCurrent))
            return vLatest > vCurrent;
        return false;
    }
}
