namespace AIUsageTracker.Services.Providers;

public record OpenAiModelPrice(
    string ModelId,
    string DisplayName,
    double InputPricePerMTok,
    double OutputPricePerMTok,
    double CachedInputPricePerMTok);

public static class OpenAiPricing
{
    public static readonly IReadOnlyList<OpenAiModelPrice> Models = new List<OpenAiModelPrice>
    {
        new("gpt-5.5",       "GPT-5.5",        5.00, 15.00, 0.50),
        new("gpt-5",         "GPT-5",          5.00, 15.00, 0.50),
        new("gpt-5-mini",    "GPT-5 mini",     0.15,  0.60, 0.015),
        new("gpt-5-nano",    "GPT-5 nano",     0.05,  0.40, 0.005),
        new("gpt-4.1",       "GPT-4.1",        2.00,  8.00, 0.50),
        new("gpt-4.1-mini",  "GPT-4.1 mini",   0.40,  1.60, 0.10),
        new("gpt-4o",        "GPT-4o",         2.50, 10.00, 1.25),
        new("gpt-4o-mini",   "GPT-4o mini",    0.15,  0.60, 0.075),
        new("o3",            "o3",            15.00, 60.00, 7.50),
        new("o3-mini",       "o3-mini",        1.10,  4.40, 0.55),
        new("o4-mini",       "o4-mini",        1.10,  4.40, 0.275),
        new("text-embedding-3-large", "text-embedding-3-large", 0.13, 0, 0),
        new("text-embedding-3-small", "text-embedding-3-small", 0.02, 0, 0),
    };

    public static OpenAiModelPrice? Find(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;
        var m = Models.FirstOrDefault(x => string.Equals(x.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (m != null) return m;
        return Models.FirstOrDefault(x => modelId.StartsWith(x.ModelId, StringComparison.OrdinalIgnoreCase));
    }

    public static double CalculateCost(string modelId, long inputTok, long outputTok, long cachedInputTok = 0)
    {
        var p = Find(modelId);
        if (p == null) return 0;
        return inputTok       / 1_000_000.0 * p.InputPricePerMTok
             + outputTok      / 1_000_000.0 * p.OutputPricePerMTok
             + cachedInputTok / 1_000_000.0 * p.CachedInputPricePerMTok;
    }
}
