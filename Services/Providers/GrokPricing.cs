namespace AIUsageTracker.Services.Providers;

public record GrokModelPrice(
    string ModelId,
    string DisplayName,
    double InputPricePerMTok,
    double OutputPricePerMTok);

public static class GrokPricing
{
    public static readonly IReadOnlyList<GrokModelPrice> Models = new List<GrokModelPrice>
    {
        new("grok-4",          "Grok 4",          5.00, 15.00),
        new("grok-4-fast",     "Grok 4 Fast",     0.20,  0.50),
        new("grok-3",          "Grok 3",          3.00, 15.00),
        new("grok-3-mini",     "Grok 3 mini",     0.30,  0.50),
        new("grok-2",          "Grok 2",          2.00, 10.00),
        new("grok-2-vision",   "Grok 2 Vision",   2.00, 10.00),
        new("grok-beta",       "Grok beta",       5.00, 15.00),
    };

    public static GrokModelPrice? Find(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;
        var m = Models.FirstOrDefault(x => string.Equals(x.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (m != null) return m;
        return Models.FirstOrDefault(x => modelId.StartsWith(x.ModelId, StringComparison.OrdinalIgnoreCase));
    }

    public static double CalculateCost(string modelId, long inputTok, long outputTok)
    {
        var p = Find(modelId);
        if (p == null) return 0;
        return inputTok  / 1_000_000.0 * p.InputPricePerMTok
             + outputTok / 1_000_000.0 * p.OutputPricePerMTok;
    }
}
