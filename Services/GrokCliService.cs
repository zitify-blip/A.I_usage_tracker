using System.IO;
using System.Text.Json;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

/// <summary>
/// Best-effort scanner for community Grok CLI session logs. Several open-source Grok CLI
/// projects exist (vibe-kit/grok-cli, superagent-ai/grok-cli, etc.) and there is no canonical
/// log location, so we probe common paths under the user profile.
/// </summary>
public class GrokCliService
{
    public IReadOnlyList<string> CandidateDirs { get; }

    public GrokCliService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        CandidateDirs = new[]
        {
            Path.Combine(home, ".grok"),
            Path.Combine(home, ".grok-cli"),
            Path.Combine(home, ".grok", "sessions"),
            Path.Combine(home, ".config", "grok"),
            Path.Combine(home, ".config", "grok-cli"),
        };
    }

    public class GrokModelRow
    {
        public string Model { get; set; } = "";
        public int Sessions { get; set; }
        public long Input { get; set; }
        public long Output { get; set; }
        public double Cost { get; set; }
        public string InputDisplay => Input.ToString("N0");
        public string OutputDisplay => Output.ToString("N0");
        public string CostDisplay => $"${Cost:F4}";
    }

    public class GrokSummary
    {
        public List<GrokModelRow> Models { get; set; } = new();
        public int SessionsTotal { get; set; }
        public long InputTotal { get; set; }
        public long OutputTotal { get; set; }
        public double CostTotal { get; set; }
        public List<string> ScannedDirs { get; set; } = new();
    }

    public GrokSummary Aggregate(DateTimeOffset since)
    {
        var summary = new GrokSummary();
        var perModel = new Dictionary<string, GrokModelRow>();

        foreach (var dir in CandidateDirs)
        {
            if (!Directory.Exists(dir)) continue;
            summary.ScannedDirs.Add(dir);

            foreach (var path in EnumerateLogFiles(dir))
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (fi.LastWriteTimeUtc < since.UtcDateTime) continue;

                    var (touched, model, inT, outT) = ParseFile(path);
                    if (!touched) continue;

                    summary.SessionsTotal++;
                    var key = string.IsNullOrEmpty(model) ? "unknown" : model;
                    if (!perModel.TryGetValue(key, out var row))
                    {
                        row = new GrokModelRow { Model = key };
                        perModel[key] = row;
                    }
                    row.Sessions++;
                    row.Input += inT;
                    row.Output += outT;
                    row.Cost += GrokPricing.CalculateCost(key, inT, outT);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Grok CLI parse failed: {path}", ex);
                }
            }
        }

        summary.Models = perModel.Values.OrderByDescending(r => r.Cost).ToList();
        summary.InputTotal = summary.Models.Sum(r => r.Input);
        summary.OutputTotal = summary.Models.Sum(r => r.Output);
        summary.CostTotal = summary.Models.Sum(r => r.Cost);
        return summary;
    }

    private static IEnumerable<string> EnumerateLogFiles(string dir)
    {
        IEnumerable<string> WithPattern(string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
        return WithPattern("*.jsonl")
            .Concat(WithPattern("session-*.json"))
            .Concat(WithPattern("history-*.json"));
    }

    private static (bool touched, string model, long input, long output) ParseFile(string path)
    {
        string model = "";
        long input = 0, output = 0;
        bool touched = false;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);

        if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    AccumulateUsage(doc.RootElement, ref model, ref input, ref output, ref touched);
                }
                catch (JsonException) { }
            }
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                AccumulateUsage(doc.RootElement, ref model, ref input, ref output, ref touched);
            }
            catch (JsonException) { }
        }
        return (touched, model, input, output);
    }

    private static void AccumulateUsage(JsonElement el, ref string model, ref long input, ref long output, ref bool touched)
    {
        if (el.ValueKind != JsonValueKind.Object) return;

        if (string.IsNullOrEmpty(model) && el.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
            model = m.GetString() ?? "";

        var usage = TryGet(el, "usage") ?? TryGet(el, "token_usage") ?? TryGet(el, "tokens");
        if (usage is { } u)
        {
            input += FindLong(u, "prompt_tokens", "input_tokens");
            output += FindLong(u, "completion_tokens", "output_tokens");
            touched = true;
        }
    }

    private static JsonElement? TryGet(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object) return v;
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
