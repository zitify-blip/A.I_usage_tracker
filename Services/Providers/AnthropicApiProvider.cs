using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsageTracker.Services.Providers;

/// <summary>
/// Anthropic Admin API client. Requires an Admin API key (sk-ant-admin*).
/// Provides usage report queries via /v1/organizations/usage_report and a basic key validation.
/// </summary>
public class AnthropicApiProvider : IUsageProvider
{
    public string Id => "anthropic-api";
    public string DisplayName => "Claude API";

    private const string BaseUrl = "https://api.anthropic.com";
    private const string AnthropicVersion = "2023-06-01";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// Validates an admin API key by calling /v1/organizations.
    /// Returns (ok, errorMessage, organizationId).
    /// </summary>
    public async Task<(bool ok, string? error, string? orgId)> ValidateKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Empty API key", null);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/organizations");
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", AnthropicVersion);

            using var res = await Http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "Invalid API key (401)", null);
            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Forbidden — Admin key required (403)", null);

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? "unknown"}", null);
            }

            var json = await res.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            string? orgId = null;
            if (doc.RootElement.TryGetProperty("data", out var arr) && arr.GetArrayLength() > 0)
                orgId = arr[0].TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            return (true, null, orgId);
        }
        catch (TaskCanceledException) { return (false, "Timeout", null); }
        catch (HttpRequestException ex) { return (false, $"Network: {ex.Message}", null); }
        catch (Exception ex)
        {
            Logger.Warn("Anthropic admin key validation failed", ex);
            return (false, $"Error: {ex.Message}", null);
        }
    }

    public record UsageBucket(
        string ModelId,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        double CostUsd);

    public record UsageResult(bool Ok, string? Error, IReadOnlyList<UsageBucket> Buckets);

    /// <summary>
    /// Fetches usage data for the given period via the messages usage report endpoint.
    /// Aggregates token counts per model across the time range.
    /// </summary>
    public async Task<UsageResult> FetchUsageAsync(string apiKey, DateTimeOffset start, DateTimeOffset end)
    {
        try
        {
            var startIso = start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endIso = end.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var url = $"{BaseUrl}/v1/organizations/usage_report/messages" +
                      $"?starting_at={Uri.EscapeDataString(startIso)}" +
                      $"&ending_at={Uri.EscapeDataString(endIso)}" +
                      $"&bucket_width=1d&group_by[]=model";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", AnthropicVersion);

            using var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                return new UsageResult(false, $"HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? body}", Array.Empty<UsageBucket>());

            var aggregated = new Dictionary<string, (long inTok, long outTok, long cacheW, long cacheR, double cost)>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var bucket in data.EnumerateArray())
                {
                    if (!bucket.TryGetProperty("results", out var results)) continue;
                    foreach (var r in results.EnumerateArray())
                    {
                        var model = r.TryGetProperty("model", out var mEl) ? mEl.GetString() ?? "unknown" : "unknown";
                        var inT = ReadLong(r, "uncached_input_tokens") + ReadLong(r, "input_tokens");
                        var outT = ReadLong(r, "output_tokens");
                        var cacheW = ReadLong(r, "cache_creation_input_tokens");
                        var cacheR = ReadLong(r, "cache_read_input_tokens");
                        var costFromApi = ReadDouble(r, "cost");

                        if (!aggregated.TryGetValue(model, out var cur))
                            cur = (0, 0, 0, 0, 0);
                        aggregated[model] = (
                            cur.inTok + inT,
                            cur.outTok + outT,
                            cur.cacheW + cacheW,
                            cur.cacheR + cacheR,
                            cur.cost + costFromApi
                        );
                    }
                }
            }

            var buckets = new List<UsageBucket>();
            foreach (var kv in aggregated)
            {
                var (inT, outT, cW, cR, cost) = kv.Value;
                if (cost <= 0)
                    cost = AnthropicPricing.CalculateCost(kv.Key, inT, outT, cW, cR);
                buckets.Add(new UsageBucket(kv.Key, inT, outT, cW, cR, cost));
            }
            return new UsageResult(true, null, buckets);
        }
        catch (TaskCanceledException) { return new UsageResult(false, "Timeout", Array.Empty<UsageBucket>()); }
        catch (Exception ex)
        {
            Logger.Warn("Anthropic FetchUsage failed", ex);
            return new UsageResult(false, ex.Message, Array.Empty<UsageBucket>());
        }
    }

    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static double ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string? TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m)) return m.GetString();
            }
        }
        catch { }
        return null;
    }
}
