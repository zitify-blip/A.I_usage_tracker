using System.Net.Http;
using System.Text.Json;

namespace AIUsageTracker.Services.Providers;

public class GeminiProvider : IUsageProvider
{
    public string Id => "gemini";
    public string DisplayName => "Gemini";

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Validates the API key by calling the models list endpoint.
    /// Returns (ok, errorMessage, modelCount).
    /// </summary>
    public async Task<(bool ok, string? error, int modelCount)> ValidateKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "Empty API key", 0);

        try
        {
            var url = $"{BaseUrl}/models?key={Uri.EscapeDataString(apiKey)}";
            using var res = await Http.GetAsync(url);

            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                res.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Invalid API key (401/403)", 0);

            if (!res.IsSuccessStatusCode)
                return (false, $"HTTP {(int)res.StatusCode}", 0);

            var body = await res.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var count = doc.RootElement.TryGetProperty("models", out var models)
                ? models.GetArrayLength() : 0;
            return (true, null, count);
        }
        catch (TaskCanceledException)
        {
            return (false, "Timeout", 0);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network: {ex.Message}", 0);
        }
        catch (Exception ex)
        {
            Logger.Warn("Gemini key validation failed", ex);
            return (false, $"Error: {ex.Message}", 0);
        }
    }

    /// <summary>
    /// Minimal test call: counts tokens for a short prompt. Used to verify key works.
    /// Returns input token count of the prompt or -1 on failure.
    /// </summary>
    public async Task<long> CountTokensAsync(string apiKey, string model = "gemini-2.5-flash")
    {
        try
        {
            var url = $"{BaseUrl}/models/{model}:countTokens?key={Uri.EscapeDataString(apiKey)}";
            var payload = """{"contents":[{"parts":[{"text":"ping"}]}]}""";
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync(url, content);
            if (!res.IsSuccessStatusCode) return -1;
            var body = await res.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("totalTokens", out var t) ? t.GetInt64() : 0;
        }
        catch (Exception ex)
        {
            Logger.Warn("Gemini countTokens failed", ex);
            return -1;
        }
    }

    public record GenerateResult(
        bool Ok,
        string? Error,
        string? ResponseText,
        long InputTokens,
        long OutputTokens,
        long CacheTokens,
        long ThinkingTokens,
        long ToolTokens,
        int LatencyMs);

    /// <summary>
    /// Calls generateContent and returns token usage parsed from usageMetadata.
    /// </summary>
    public async Task<GenerateResult> GenerateContentAsync(string apiKey, string model, string prompt)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var url = $"{BaseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            var payload = JsonSerializer.Serialize(new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            });

            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            using var res = await Http.PostAsync(url, content);
            sw.Stop();

            var body = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                var errMsg = TryExtractError(body) ?? $"HTTP {(int)res.StatusCode}";
                return new GenerateResult(false, errMsg, null, 0, 0, 0, 0, 0, (int)sw.ElapsedMilliseconds);
            }

            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? text = null;
            if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
            {
                var first = cands[0];
                if (first.TryGetProperty("content", out var c) &&
                    c.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textEl))
                {
                    text = textEl.GetString();
                }
            }

            long inTok = 0, outTok = 0, cacheTok = 0, thinkTok = 0, toolTok = 0;
            if (root.TryGetProperty("usageMetadata", out var meta))
            {
                inTok    = ReadLong(meta, "promptTokenCount");
                outTok   = ReadLong(meta, "candidatesTokenCount");
                cacheTok = ReadLong(meta, "cachedContentTokenCount");
                thinkTok = ReadLong(meta, "thoughtsTokenCount");
                toolTok  = ReadLong(meta, "toolUsePromptTokenCount");
            }

            return new GenerateResult(true, null, text, inTok, outTok, cacheTok, thinkTok, toolTok,
                (int)sw.ElapsedMilliseconds);
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return new GenerateResult(false, "Timeout", null, 0, 0, 0, 0, 0, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Warn("Gemini generateContent failed", ex);
            return new GenerateResult(false, ex.Message, null, 0, 0, 0, 0, 0, (int)sw.ElapsedMilliseconds);
        }
    }

    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

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
