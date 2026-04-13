using System.Text.Json;
using ClaudeUsageTracker.Models;

namespace ClaudeUsageTracker.Services;

public class UsageService
{
    private readonly StorageService _storage;
    private readonly ClaudeApiService _api;

    public LatestUsage Latest { get; private set; } = new();
    public bool IsLoggedIn { get; private set; }
    public string StatusText { get; private set; } = "Loading...";
    public string StatusKind { get; private set; } = "loading";

    public event Action? UsageUpdated;
    public event Action? StatusChanged;

    public UsageService(StorageService storage, ClaudeApiService api)
    {
        _storage = storage;
        _api = api;
    }

    public static double ToPercent(double v)
    {
        if (double.IsNaN(v)) return 0;
        if (v >= 0 && v <= 1) return v * 100;
        return v;
    }

    public void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusKind = kind;
        StatusChanged?.Invoke();
    }

    public void Logout()
    {
        IsLoggedIn = false;
        Latest = new LatestUsage();
        SetStatus("Login required", "error");
        UsageUpdated?.Invoke();
    }

    /// <summary>Returns: true=success, false=error, null=needs login</summary>
    public async Task<bool?> FetchUsageAsync()
    {
        if (!_api.IsReady)
        {
            SetStatus("WebView not ready", "loading");
            return false;
        }

        SetStatus("Refreshing...", "loading");

        var (ok, error, data) = await _api.FetchUsageAsync();

        if (ok && data != null)
        {
            IsLoggedIn = true;
            SetStatus("Connected", "connected");
            ParseUsageData(data);
            return true;
        }

        if (error == "not_logged_in")
        {
            IsLoggedIn = false;
            SetStatus("Login required", "error");
            return null;
        }

        SetStatus($"Error: {error}", "error");
        return false;
    }

    private void ParseUsageData(UsageApiResponse data)
    {
        var fiveHour = data.GetFiveHour();
        var sevenDay = data.GetSevenDay();
        var sub = data.GetSevenDaySub();
        var extra = data.GetExtraUsage();

        Latest = new LatestUsage
        {
            SessionPct = ToPercent(fiveHour?.Utilization ?? 0),
            SessionResetAt = fiveHour?.GetResetTime(),
            WeekPct = ToPercent(sevenDay?.Utilization ?? 0),
            WeekResetAt = sevenDay?.GetResetTime(),
            SubPct = ToPercent(sub?.Utilization ?? 0),
            SubResetAt = sub?.GetResetTime(),
            SubModelName = data.GetSubModelName(),
            Extra = extra
        };

        var snapshot = new UsageSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            FiveHourUtilization = Latest.SessionPct,
            FiveHourResetsAt = Latest.SessionResetAt,
            SevenDayUtilization = Latest.WeekPct,
            SevenDayResetsAt = Latest.WeekResetAt,
            SubModelUtilization = Latest.SubPct,
            SubModelResetsAt = Latest.SubResetAt
        };

        _storage.SaveSnapshot(snapshot);
        UsageUpdated?.Invoke();
    }

    /// <summary>
    /// Process a raw JSON result string (from LoginWindow's direct fetch).
    /// Returns true if data was parsed successfully.
    /// </summary>
    public bool ProcessRawFetchResult(string resultJson)
    {
        try
        {
            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (!ok) return false;

            var dataStr = result.GetProperty("data").GetString();
            if (dataStr == null) return false;

            var usage = JsonSerializer.Deserialize<UsageApiResponse>(dataStr,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (usage == null) return false;

            IsLoggedIn = true;
            SetStatus("Connected", "connected");
            ParseUsageData(usage);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<UsageSnapshot> GetHistory() => _storage.GetHistory();
}
