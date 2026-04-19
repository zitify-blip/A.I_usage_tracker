using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIUsageTracker.Services;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string? Sha256Url { get; set; }
}

public class UpdateService
{
    // GitHub owner/repo — change this to your actual repo
    private const string GitHubOwner = "zitify-blip";
    private const string GitHubRepo = "A.I_usage_tracker";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static UpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"AIUsageTracker/{CurrentVersion}");
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

            // Find installer asset (.exe or .msi) and matching .sha256 sidecar
            string downloadUrl = "";
            string fileName = "";
            string? sha256Url = null;
            var sha256Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (doc.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var lower = name.ToLowerInvariant();

                    if (lower.EndsWith(".sha256"))
                    {
                        sha256Map[name[..^".sha256".Length]] = url;
                    }
                    else if (lower.EndsWith(".exe") || lower.EndsWith(".msi") || lower.EndsWith(".zip"))
                    {
                        if (string.IsNullOrEmpty(downloadUrl) || lower.EndsWith(".exe"))
                        {
                            downloadUrl = url;
                            fileName = name;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
                return null;

            // Sanitize filename to prevent path traversal
            fileName = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(fileName) || !Regex.IsMatch(fileName, @"^[a-zA-Z0-9._\-]+$"))
                return null;

            sha256Map.TryGetValue(fileName, out sha256Url);

            return new UpdateInfo
            {
                Version = latestVersion,
                DownloadUrl = downloadUrl,
                FileName = fileName,
                ReleaseNotes = body.Length > 300 ? body[..300] + "..." : body,
                Sha256Url = sha256Url
            };
        }
        catch (Exception ex)
        {
            Logger.Warn("Update check failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Download the installer to temp folder and launch it.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, Action<int>? onProgress = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"AIUsageTrackerUpdate_{Guid.NewGuid():N}");
        string? filePath = null;

        try
        {
            // Validate download URL is from GitHub
            if (!info.DownloadUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return false;

            Directory.CreateDirectory(tempDir);
            filePath = Path.Combine(tempDir, Path.GetFileName(info.FileName));

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloadedBytes = 0;

                await using var contentStream = await response.Content.ReadAsStreamAsync(downloadCts.Token);
                await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, downloadCts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), downloadCts.Token);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var pct = (int)(downloadedBytes * 100 / totalBytes);
                            onProgress?.Invoke(pct);
                        }
                    }
                }
            }

            onProgress?.Invoke(100);

            // SHA256 integrity check (mandatory if sha256 sidecar provided)
            if (!string.IsNullOrEmpty(info.Sha256Url))
            {
                if (!await VerifySha256Async(filePath, info.Sha256Url))
                {
                    Logger.Error($"SHA256 verification failed for {info.FileName} — installer aborted");
                    try { File.Delete(filePath); } catch { }
                    try { Directory.Delete(tempDir, true); } catch { }
                    return false;
                }
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Update download/install failed", ex);
            try { if (filePath != null && File.Exists(filePath)) File.Delete(filePath); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            return false;
        }
    }

    private static async Task<bool> VerifySha256Async(string filePath, string sha256Url)
    {
        try
        {
            if (!sha256Url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                return false;

            var expected = (await Http.GetStringAsync(sha256Url))
                .Trim().Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0]
                .ToLowerInvariant();

            if (!Regex.IsMatch(expected, @"^[0-9a-f]{64}$"))
                return false;

            await using var stream = File.OpenRead(filePath);
            var hashBytes = await SHA256.HashDataAsync(stream);
            var actual = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return actual == expected;
        }
        catch (Exception ex)
        {
            Logger.Error("SHA256 verification error", ex);
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
