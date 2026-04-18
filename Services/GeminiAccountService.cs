using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

public class GeminiAccountService
{
    private readonly StorageService _storage;
    private readonly GeminiProvider _provider;

    public event Action? AccountsChanged;
    public event Action? SelectedAccountChanged;

    public GeminiAccountService(StorageService storage, GeminiProvider provider)
    {
        _storage = storage;
        _provider = provider;
    }

    public IReadOnlyList<GeminiAccount> GetAccounts() => _storage.GeminiAccounts;

    public GeminiAccount? GetSelected()
    {
        var id = _storage.SelectedGeminiAccountId;
        if (string.IsNullOrEmpty(id)) return _storage.GeminiAccounts.FirstOrDefault(a => a.IsPrimary)
                                              ?? _storage.GeminiAccounts.FirstOrDefault();
        return _storage.GeminiAccounts.FirstOrDefault(a => a.Id == id)
               ?? _storage.GeminiAccounts.FirstOrDefault();
    }

    public void SelectAccount(string accountId)
    {
        if (_storage.GeminiAccounts.All(a => a.Id != accountId)) return;
        _storage.SetSelectedGeminiAccount(accountId);
        SelectedAccountChanged?.Invoke();
    }

    public async Task<(bool ok, string? error, GeminiAccount? account)> AddAccountAsync(string alias, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Logger.Warn("Gemini AddAccount rejected: empty key");
            return (false, "API 키가 비어있습니다", null);
        }

        var (ok, err, count) = await _provider.ValidateKeyAsync(apiKey);
        if (!ok)
        {
            Logger.Warn($"Gemini AddAccount validation failed: {err}");
            return (false, err ?? "키 검증 실패", null);
        }

        var account = new GeminiAccount
        {
            Alias = string.IsNullOrWhiteSpace(alias) ? $"account-{_storage.GeminiAccounts.Count + 1}" : alias.Trim(),
            EncryptedApiKey = DpapiHelper.Protect(apiKey),
            KeyPreview = DpapiHelper.Mask(apiKey),
            IsPrimary = _storage.GeminiAccounts.Count == 0,
            IsActive = true
        };

        _storage.AddGeminiAccount(account);
        Logger.Info($"Gemini account added: alias='{account.Alias}' id={account.Id} primary={account.IsPrimary} models={count} persisted={_storage.GeminiAccounts.Count}");
        AccountsChanged?.Invoke();
        if (account.IsPrimary) SelectAccount(account.Id);
        return (true, null, account);
    }

    public void RemoveAccount(string accountId)
    {
        _storage.RemoveGeminiAccount(accountId);
        AccountsChanged?.Invoke();
        SelectedAccountChanged?.Invoke();
    }

    public void RenameAccount(string accountId, string newAlias)
    {
        var acc = _storage.GeminiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return;
        acc.Alias = newAlias.Trim();
        _storage.Save();
        AccountsChanged?.Invoke();
    }

    public void SetPrimary(string accountId)
    {
        foreach (var a in _storage.GeminiAccounts)
            a.IsPrimary = (a.Id == accountId);
        _storage.Save();
        AccountsChanged?.Invoke();
    }

    public void SetActive(string accountId, bool isActive)
    {
        var acc = _storage.GeminiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return;
        acc.IsActive = isActive;
        _storage.Save();
        AccountsChanged?.Invoke();
    }

    public void SetBudget(string accountId, double dailyUsd, double monthlyUsd, int alertThresholdPct)
    {
        var acc = _storage.GeminiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return;
        acc.DailyBudgetUsd = Math.Max(0, dailyUsd);
        acc.MonthlyBudgetUsd = Math.Max(0, monthlyUsd);
        acc.AlertThresholdPct = Math.Clamp(alertThresholdPct, 1, 100);
        _storage.Save();
        AccountsChanged?.Invoke();
    }

    public async Task<(bool ok, string? error)> RotateKeyAsync(string accountId, string newApiKey)
    {
        var acc = _storage.GeminiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return (false, "계정을 찾을 수 없음");

        var (ok, err, _) = await _provider.ValidateKeyAsync(newApiKey);
        if (!ok) return (false, err ?? "키 검증 실패");

        acc.EncryptedApiKey = DpapiHelper.Protect(newApiKey);
        acc.KeyPreview = DpapiHelper.Mask(newApiKey);
        _storage.Save();
        AccountsChanged?.Invoke();
        return (true, null);
    }

    public string? GetApiKey(string accountId)
    {
        var acc = _storage.GeminiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return null;
        return DpapiHelper.Unprotect(acc.EncryptedApiKey);
    }
}
