using System.Net.Http;
using System.Text.Json;

namespace AIUsageTracker.Services.Providers;

/// <summary>
/// xAI (Grok) API client. xAI does not currently expose an organization-wide usage report
/// endpoint, so this provider only validates keys and reports the per-key info from /v1/api-key.
/// </summary>
public class GrokApiProvider : IUsageProvider
{
    public string Id => "grok-api";
    public string DisplayName => "Grok (xAI)";

    private const string BaseUrl = "https://api.x.ai";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public record KeyInfo(
        string? Name,
        string? UserId,
        string? TeamId,
        bool? Disabled,
        DateTimeOffset? CreatedAt,
        IReadOnlyList<string> AllowedModels,
        IReadOnlyList<string> AllowedAcls);

    public async Task<(bool ok, string? error, KeyInfo? info)> ValidateKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Empty API key", null);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/api-key");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            using var res = await Http.SendAsync(req);

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "Invalid API key (401)", null);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? "unknown"}", null);
            }

            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? userId = root.TryGetProperty("user_id", out var u) ? u.GetString() : null;
            string? teamId = root.TryGetProperty("team_id", out var t) ? t.GetString() : null;
            bool? disabled = null;
            if (root.TryGetProperty("api_key_disabled", out var d) && d.ValueKind == JsonValueKind.True)
                disabled = true;
            else if (d.ValueKind == JsonValueKind.False) disabled = false;
            DateTimeOffset? created = null;
            if (root.TryGetProperty("create_time", out var c) && c.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(c.GetString(), out var ts)) created = ts;

            var models = ReadStrings(root, "allowed_models");
            var acls = ReadStrings(root, "acls");

            return (true, null, new KeyInfo(name, userId, teamId, disabled, created, models, acls));
        }
        catch (TaskCanceledException) { return (false, "Timeout", null); }
        catch (HttpRequestException ex) { return (false, $"Network: {ex.Message}", null); }
        catch (Exception ex)
        {
            Logger.Warn("Grok key validation failed", ex);
            return (false, $"Error: {ex.Message}", null);
        }
    }

    public async Task<(bool ok, string? error, IReadOnlyList<string> models)> ListModelsAsync(string apiKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v1/models");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)res.StatusCode}: {TryExtractError(body) ?? body}", Array.Empty<string>());
            }
            var json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var arr))
                foreach (var item in arr.EnumerateArray())
                    if (item.TryGetProperty("id", out var idEl)) list.Add(idEl.GetString() ?? "");
            return (true, null, list);
        }
        catch (Exception ex) { return (false, ex.Message, Array.Empty<string>()); }
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? "");
        return list;
    }

    private static string? TryExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
                if (err.TryGetProperty("message", out var m)) return m.GetString();
            }
        }
        catch { }
        return null;
    }
}
