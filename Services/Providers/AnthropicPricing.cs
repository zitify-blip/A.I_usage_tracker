namespace AIUsageTracker.Services.Providers;

public record AnthropicModelPrice(
    string ModelId,
    string DisplayName,
    double InputPricePerMTok,
    double OutputPricePerMTok,
    double CacheWritePricePerMTok,
    double CacheReadPricePerMTok);

public static class AnthropicPricing
{
    public static readonly IReadOnlyList<AnthropicModelPrice> Models = new List<AnthropicModelPrice>
    {
        new("claude-opus-4-5",       "Claude Opus 4.5",       15.00, 75.00, 18.75, 1.50),
        new("claude-sonnet-4-5",     "Claude Sonnet 4.5",      3.00, 15.00,  3.75, 0.30),
        new("claude-haiku-4-5",      "Claude Haiku 4.5",       1.00,  5.00,  1.25, 0.10),
        new("claude-3-7-sonnet",     "Claude 3.7 Sonnet",      3.00, 15.00,  3.75, 0.30),
        new("claude-3-5-sonnet",     "Claude 3.5 Sonnet",      3.00, 15.00,  3.75, 0.30),
        new("claude-3-5-haiku",      "Claude 3.5 Haiku",       0.80,  4.00,  1.00, 0.08),
        new("claude-3-opus",         "Claude 3 Opus",         15.00, 75.00, 18.75, 1.50),
    };

    public static AnthropicModelPrice? Find(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;
        var m = Models.FirstOrDefault(x => string.Equals(x.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (m != null) return m;
        return Models.FirstOrDefault(x => modelId.StartsWith(x.ModelId, StringComparison.OrdinalIgnoreCase));
    }

    public static double CalculateCost(string modelId, long inputTok, long outputTok,
        long cacheWriteTok = 0, long cacheReadTok = 0)
    {
        var p = Find(modelId);
        if (p == null) return 0;
        return inputTok      / 1_000_000.0 * p.InputPricePerMTok
             + outputTok     / 1_000_000.0 * p.OutputPricePerMTok
             + cacheWriteTok / 1_000_000.0 * p.CacheWritePricePerMTok
             + cacheReadTok  / 1_000_000.0 * p.CacheReadPricePerMTok;
    }
}
