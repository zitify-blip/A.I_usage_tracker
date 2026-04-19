using System.IO;
using System.Text.Json;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

/// <summary>
/// Best-effort parser for OpenAI Codex CLI session logs at ~/.codex/sessions/rollout-*.jsonl.
/// Each rollout file records a single Codex CLI session as JSON-Lines events; we look for
/// usage payloads that carry input/output/cached token counts and a model id.
/// </summary>
public class CodexCliService
{
    public string SessionsDir { get; }

    public CodexCliService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SessionsDir = Path.Combine(home, ".codex", "sessions");
    }

    public class CodexModelRow
    {
        public string Model { get; set; } = "";
        public int Sessions { get; set; }
        public long Input { get; set; }
        public long Output { get; set; }
        public long Cached { get; set; }
        public double Cost { get; set; }
        public string InputDisplay => Input.ToString("N0");
        public string OutputDisplay => Output.ToString("N0");
        public string CachedDisplay => Cached.ToString("N0");
        public string CostDisplay => $"${Cost:F4}";
    }

    public class CodexSummary
    {
        public List<CodexModelRow> Models { get; set; } = new();
        public int SessionsTotal { get; set; }
        public long InputTotal { get; set; }
        public long OutputTotal { get; set; }
        public double CostTotal { get; set; }
    }

    public CodexSummary Aggregate(DateTimeOffset since)
    {
        var summary = new CodexSummary();
        if (!Directory.Exists(SessionsDir)) return summary;

        var sinceMs = since.ToUnixTimeMilliseconds();
        var perModel = new Dictionary<string, CodexModelRow>();
        var sessionFiles = 0;

        foreach (var path in EnumerateSessionFiles(SessionsDir))
        {
            try
            {
                var fi = new FileInfo(path);
                if (fi.LastWriteTimeUtc < since.UtcDateTime) continue;

                var (touched, model, inT, outT, cached) = ParseFile(path);
                if (!touched) continue;

                sessionFiles++;
                var key = string.IsNullOrEmpty(model) ? "unknown" : model;
                if (!perModel.TryGetValue(key, out var row))
                {
                    row = new CodexModelRow { Model = key };
                    perModel[key] = row;
                }
                row.Sessions++;
                row.Input += inT;
                row.Output += outT;
                row.Cached += cached;
                row.Cost += OpenAiPricing.CalculateCost(key, inT, outT, cached);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Codex parse failed: {path}", ex);
            }
        }

        summary.Models = perModel.Values.OrderByDescending(r => r.Cost).ToList();
        summary.SessionsTotal = sessionFiles;
        summary.InputTotal = summary.Models.Sum(r => r.Input);
        summary.OutputTotal = summary.Models.Sum(r => r.Output);
        summary.CostTotal = summary.Models.Sum(r => r.Cost);
        return summary;
    }

    private static IEnumerable<string> EnumerateSessionFiles(string dir)
    {
        // Codex stores files as ~/.codex/sessions/<yyyy>/<mm>/<dd>/rollout-*.jsonl in newer
        // builds and flat in older builds. EnumerateFiles with recursive search covers both.
        return Directory.EnumerateFiles(dir, "rollout-*.jsonl", SearchOption.AllDirectories);
    }

    private static (bool touched, string model, long input, long output, long cached)
        ParseFile(string path)
    {
        string model = "";
        long input = 0, output = 0, cached = 0;
        bool touched = false;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (string.IsNullOrEmpty(model))
                {
                    var m = FindString(root, "model");
                    if (!string.IsNullOrEmpty(m)) model = m;
                }

                var usage = FindObject(root, "usage")
                         ?? FindObject(root, "token_usage")
                         ?? FindObject(root, "tokens");
                if (usage is { } u)
                {
                    input += FindLong(u, "input_tokens", "prompt_tokens", "total_input_tokens");
                    output += FindLong(u, "output_tokens", "completion_tokens", "total_output_tokens");
                    cached += FindLong(u, "input_cached_tokens", "cached_tokens", "cache_read_input_tokens");
                    touched = true;
                }
            }
            catch (JsonException) { /* skip malformed line */ }
        }
        return (touched, model, input, output, cached);
    }

    private static string FindString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        return "";
    }

    private static JsonElement? FindObject(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object)
                return v;
        return null;
    }

    private static long FindLong(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt64();
        return 0;
    }
}
