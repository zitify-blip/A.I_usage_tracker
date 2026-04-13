using System.Text.Json.Serialization;

namespace ClaudeUsageTracker.Models;

public class UsageCategory
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; set; }

    public string? GetResetTime() => ResetsAt ?? ResetAt;
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("monthly_limit")]
    public double? MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }
}

public class UsageApiResponse
{
    [JsonPropertyName("five_hour")]
    public UsageCategory? FiveHour { get; set; }

    [JsonPropertyName("fiveHour")]
    public UsageCategory? FiveHourAlt { get; set; }

    [JsonPropertyName("session")]
    public UsageCategory? Session { get; set; }

    [JsonPropertyName("current_session")]
    public UsageCategory? CurrentSession { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageCategory? SevenDay { get; set; }

    [JsonPropertyName("sevenDay")]
    public UsageCategory? SevenDayAlt { get; set; }

    [JsonPropertyName("weekly")]
    public UsageCategory? Weekly { get; set; }

    [JsonPropertyName("seven_day_all")]
    public UsageCategory? SevenDayAll { get; set; }

    [JsonPropertyName("seven_day_all_models")]
    public UsageCategory? SevenDayAllModels { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageCategory? SevenDaySonnet { get; set; }

    [JsonPropertyName("sevenDaySonnet")]
    public UsageCategory? SevenDaySonnetAlt { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageCategory? SevenDayOpus { get; set; }

    [JsonPropertyName("sevenDayOpus")]
    public UsageCategory? SevenDayOpusAlt { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }

    [JsonPropertyName("extraUsage")]
    public ExtraUsage? ExtraUsageAlt { get; set; }

    public UsageCategory? GetFiveHour() => FiveHour ?? FiveHourAlt ?? Session ?? CurrentSession;
    public UsageCategory? GetSevenDay() => SevenDay ?? SevenDayAlt ?? Weekly ?? SevenDayAll ?? SevenDayAllModels;
    public UsageCategory? GetSevenDaySub() => SevenDaySonnet ?? SevenDaySonnetAlt ?? SevenDayOpus ?? SevenDayOpusAlt;
    public string GetSubModelName() => (SevenDaySonnet ?? SevenDaySonnetAlt) != null ? "Sonnet" : "Opus";
    public ExtraUsage? GetExtraUsage() => ExtraUsage ?? ExtraUsageAlt;
}

public class UsageSnapshot
{
    public long Timestamp { get; set; }
    public double FiveHourUtilization { get; set; }
    public string? FiveHourResetsAt { get; set; }
    public double SevenDayUtilization { get; set; }
    public string? SevenDayResetsAt { get; set; }
    public double SubModelUtilization { get; set; }
    public string? SubModelResetsAt { get; set; }
}

public class AppSettings
{
    public int PollIntervalMinutes { get; set; } = 5;
    public int NotifyThreshold { get; set; } = 80;
}

public class StorageData
{
    public List<UsageSnapshot> UsageHistory { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}

public class LatestUsage
{
    public double SessionPct { get; set; }
    public string? SessionResetAt { get; set; }
    public double WeekPct { get; set; }
    public string? WeekResetAt { get; set; }
    public double SubPct { get; set; }
    public string? SubResetAt { get; set; }
    public string SubModelName { get; set; } = "Sonnet";
    public ExtraUsage? Extra { get; set; }
}
