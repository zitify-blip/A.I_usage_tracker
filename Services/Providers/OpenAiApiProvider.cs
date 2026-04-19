using System.Net.Http;
using System.Text.Json;

namespace AIUsageTracker.Services.Providers;

/// <summary>
/// OpenAI Admin API client. Requires an Admin API key (sk-admin-*).
/// Uses /v1/organization/usage/{completions,embeddings,...} endpoints with Unix-second timestamps.
/// </summary>
public class OpenAiApiProvider : IUsageProvider
{
    public string Id => "openai-api";
    public string DisplayName => "OpenAI API";

    private const string BaseUrl = "https://api.openai.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<(bool ok, string? error, string? orgId)> ValidateKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Empty API key", null);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/organization/projects?limit=1");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            using var res = await Http.SendAsync(req);

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "Invalid API key (401)", null);
            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Forbidden — Admin key required (sk-admin-…)", null);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? "unknown"}", null);
            }

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            string? orgId = null;
            if (doc.RootElement.TryGetProperty("data", out var arr) && arr.GetArrayLength() > 0)
            {
                var first = arr[0];
                orgId = first.TryGetProperty("organization_id", out var oid) ? oid.GetString()
                      : first.TryGetProperty("id", out var pid) ? pid.GetString()
                      : null;
            }
            return (true, null, orgId);
        }
        catch (TaskCanceledException) { return (false, "Timeout", null); }
        catch (HttpRequestException ex) { return (false, $"Network: {ex.Message}", null); }
        catch (Exception ex)
        {
            Logger.Warn("OpenAI admin key validation failed", ex);
            return (false, $"Error: {ex.Message}", null);
        }
    }

    public record UsageBucket(
        string ModelId,
        long InputTokens,
        long OutputTokens,
        long CachedInputTokens,
        double CostUsd);

    public record UsageResult(bool Ok, string? Error, IReadOnlyList<UsageBucket> Buckets);

    /// <summary>
    /// Fetches usage from /v1/organization/usage/completions and /embeddings, then aggregates per model.
    /// Costs are derived from local OpenAiPricing because the usage endpoints don't return cost.
    /// </summary>
    public async Task<UsageResult> FetchUsageAsync(string apiKey, DateTimeOffset start, DateTimeOffset end)
    {
        try
        {
            var startUnix = start.ToUnixTimeSeconds();
            var endUnix = end.ToUnixTimeSeconds();
            var aggregated = new Dictionary<string, (long inTok, long outTok, long cached)>();

            await FetchEndpointAsync(apiKey, "completions", startUnix, endUnix, aggregated);
            await FetchEndpointAsync(apiKey, "embeddings", startUnix, endUnix, aggregated);

            var buckets = new List<UsageBucket>();
            foreach (var kv in aggregated)
            {
                var (inT, outT, cached) = kv.Value;
                var cost = OpenAiPricing.CalculateCost(kv.Key, inT, outT, cached);
                buckets.Add(new UsageBucket(kv.Key, inT, outT, cached, cost));
            }
            return new UsageResult(true, null, buckets);
        }
        catch (TaskCanceledException) { return new UsageResult(false, "Timeout", Array.Empty<UsageBucket>()); }
        catch (Exception ex)
        {
            Logger.Warn("OpenAI FetchUsage failed", ex);
            return new UsageResult(false, ex.Message, Array.Empty<UsageBucket>());
        }
    }

    private async Task FetchEndpointAsync(
        string apiKey, string endpoint, long startUnix, long endUnix,
        Dictionary<string, (long, long, long)> agg)
    {
        var url = $"{BaseUrl}/v1/organization/usage/{endpoint}" +
                  $"?start_time={startUnix}&end_time={endUnix}" +
                  $"&bucket_width=1d&group_by[]=model&limit=180";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            Logger.Warn($"OpenAI usage/{endpoint} HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? body}");
            return;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return;
        foreach (var bucket in data.EnumerateArray())
        {
            if (!bucket.TryGetProperty("results", out var results)) continue;
            foreach (var r in results.EnumerateArray())
            {
                var model = r.TryGetProperty("model", out var mEl) ? mEl.GetString() ?? "unknown" : "unknown";
                var inT = ReadLong(r, "input_tokens");
                var outT = ReadLong(r, "output_tokens");
                var cached = ReadLong(r, "input_cached_tokens");
                if (!agg.TryGetValue(model, out var cur)) cur = (0, 0, 0);
                agg[model] = (cur.Item1 + inT, cur.Item2 + outT, cur.Item3 + cached);
            }
        }
    }

    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static string? TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var m))
                return m.GetString();
        }
        catch { }
        return null;
    }
}
