namespace AIUsageTracker.Services.Providers;

public record GeminiModelPrice(
    string ModelId,
    string DisplayName,
    double InputPricePerMTok,
    double OutputPricePerMTok,
    double CachePricePerMTok);

public static class GeminiPricing
{
    public static readonly IReadOnlyList<GeminiModelPrice> Models = new List<GeminiModelPrice>
    {
        new("gemini-2.5-pro",        "Gemini 2.5 Pro",        1.25, 10.00, 0.31),
        new("gemini-2.5-flash",      "Gemini 2.5 Flash",      0.30,  2.50, 0.075),
        new("gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite", 0.10,  0.40, 0.025),
        new("gemini-2.0-flash",      "Gemini 2.0 Flash",      0.10,  0.40, 0.025),
    };

    public static GeminiModelPrice? Find(string modelId) =>
        Models.FirstOrDefault(m => string.Equals(m.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

    public static double CalculateCost(string modelId, long inputTokens, long outputTokens, long cacheTokens = 0)
    {
        var p = Find(modelId);
        if (p == null) return 0;
        return CalculateCost(p, inputTokens, outputTokens, cacheTokens);
    }

    public static double CalculateCost(GeminiModelPrice p, long inputTokens, long outputTokens, long cacheTokens = 0)
    {
        return inputTokens / 1_000_000.0 * p.InputPricePerMTok
             + outputTokens / 1_000_000.0 * p.OutputPricePerMTok
             + cacheTokens / 1_000_000.0 * p.CachePricePerMTok;
    }
}
