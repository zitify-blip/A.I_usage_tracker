using System.IO;
using System.Text.Json;
using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

public class StorageService
{
    private static readonly string StorageDir = AppPaths.Root;

    private static readonly string StoragePath = Path.Combine(StorageDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private StorageData _data = new();

    public StorageService()
    {
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = File.ReadAllText(StoragePath);
                _data = JsonSerializer.Deserialize<StorageData>(json, JsonOptions) ?? new StorageData();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Storage load failed, using defaults", ex);
            _data = new StorageData();
        }
        _data.Settings ??= new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(StorageDir);
            var json = JsonSerializer.Serialize(_data, JsonOptions);

            var tempPath = StoragePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StoragePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error($"Storage save failed (accounts={_data.GeminiAccounts.Count})", ex);
        }
    }

    public List<UsageSnapshot> GetHistory() => _data.UsageHistory;

    public AppSettings Settings => _data.Settings;

    public void SaveSettings(AppSettings settings)
    {
        _data.Settings = settings;
        Save();
    }

    public void SaveSnapshot(UsageSnapshot snapshot)
    {
        if (snapshot.FiveHourUtilization is < 0 or > 100) return;
        if (snapshot.SevenDayUtilization is < 0 or > 100) return;

        _data.UsageHistory.Add(snapshot);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        _data.UsageHistory.RemoveAll(s => s.Timestamp < cutoff);

        Save();
    }

    // ────────── Gemini ──────────

    public List<GeminiAccount> GeminiAccounts => _data.GeminiAccounts;
    public string? SelectedGeminiAccountId => _data.SelectedGeminiAccountId;

    public void AddGeminiAccount(GeminiAccount account)
    {
        _data.GeminiAccounts.Add(account);
        if (string.IsNullOrEmpty(_data.SelectedGeminiAccountId))
            _data.SelectedGeminiAccountId = account.Id;
        Save();
    }

    public void RemoveGeminiAccount(string accountId)
    {
        _data.GeminiAccounts.RemoveAll(a => a.Id == accountId);
        _data.GeminiUsageHistory.RemoveAll(r => r.AccountId == accountId);
        if (_data.SelectedGeminiAccountId == accountId)
            _data.SelectedGeminiAccountId = _data.GeminiAccounts.FirstOrDefault()?.Id;
        Save();
    }

    public void SetSelectedGeminiAccount(string? accountId)
    {
        _data.SelectedGeminiAccountId = accountId;
        Save();
    }

    public void SaveGeminiUsage(GeminiUsageRecord record)
    {
        _data.GeminiUsageHistory.Add(record);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeMilliseconds();
        _data.GeminiUsageHistory.RemoveAll(r => r.Timestamp < cutoff);
        Save();
    }

    public IReadOnlyList<GeminiUsageRecord> GetGeminiUsageHistory(string? accountId = null)
    {
        if (accountId == null) return _data.GeminiUsageHistory;
        return _data.GeminiUsageHistory.Where(r => r.AccountId == accountId).ToList();
    }

    // ────────── Gemini pricing overrides ──────────

    public IReadOnlyList<GeminiPricingOverride> GetPricingOverrides() => _data.GeminiPricingOverrides;

    public GeminiModelPrice GetEffectivePrice(string modelId)
    {
        var preset = GeminiPricing.Find(modelId);
        var ov = _data.GeminiPricingOverrides.FirstOrDefault(
            p => string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (ov != null)
        {
            var name = preset?.DisplayName ?? modelId;
            return new GeminiModelPrice(modelId, name,
                ov.InputPricePerMTok, ov.OutputPricePerMTok, ov.CachePricePerMTok);
        }
        return preset ?? new GeminiModelPrice(modelId, modelId, 0, 0, 0);
    }

    public void SetPricingOverride(string modelId, double inputPerMTok, double outputPerMTok, double cachePerMTok)
    {
        var existing = _data.GeminiPricingOverrides.FirstOrDefault(
            p => string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.InputPricePerMTok = inputPerMTok;
            existing.OutputPricePerMTok = outputPerMTok;
            existing.CachePricePerMTok = cachePerMTok;
        }
        else
        {
            _data.GeminiPricingOverrides.Add(new GeminiPricingOverride
            {
                ModelId = modelId,
                InputPricePerMTok = inputPerMTok,
                OutputPricePerMTok = outputPerMTok,
                CachePricePerMTok = cachePerMTok
            });
        }
        Save();
    }

    public void RemovePricingOverride(string modelId)
    {
        _data.GeminiPricingOverrides.RemoveAll(
            p => string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}
