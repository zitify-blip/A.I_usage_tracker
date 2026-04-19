using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

public class GrokApiAccountService
{
    private readonly StorageService _storage;
    private readonly GrokApiProvider _provider;

    public event Action? AccountsChanged;
    public event Action? SelectedAccountChanged;

    public GrokApiAccountService(StorageService storage, GrokApiProvider provider)
    {
        _storage = storage;
        _provider = provider;
    }

    public IReadOnlyList<GrokApiAccount> GetAccounts() => _storage.GrokApiAccounts;

    public GrokApiAccount? GetSelected()
    {
        var id = _storage.SelectedGrokApiAccountId;
        if (string.IsNullOrEmpty(id))
            return _storage.GrokApiAccounts.FirstOrDefault(a => a.IsPrimary)
                ?? _storage.GrokApiAccounts.FirstOrDefault();
        return _storage.GrokApiAccounts.FirstOrDefault(a => a.Id == id)
            ?? _storage.GrokApiAccounts.FirstOrDefault();
    }

    public void SelectAccount(string accountId)
    {
        if (_storage.GrokApiAccounts.All(a => a.Id != accountId)) return;
        _storage.SetSelectedGrokApiAccount(accountId);
        SelectedAccountChanged?.Invoke();
    }

    public async Task<(bool ok, string? error, GrokApiAccount? account)> AddAccountAsync(string alias, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API 키가 비어있습니다", null);

        var (ok, err, info) = await _provider.ValidateKeyAsync(apiKey);
        if (!ok)
        {
            Logger.Warn($"Grok AddAccount validation failed: {err}");
            return (false, err ?? "키 검증 실패", null);
        }

        var account = new GrokApiAccount
        {
            Alias = string.IsNullOrWhiteSpace(alias)
                ? $"account-{_storage.GrokApiAccounts.Count + 1}"
                : alias.Trim(),
            EncryptedApiKey = DpapiHelper.Protect(apiKey),
            KeyPreview = DpapiHelper.Mask(apiKey),
            KeyName = info?.Name,
            UserId = info?.UserId,
            TeamId = info?.TeamId,
            AllowedModels = info?.AllowedModels?.ToList() ?? new(),
            IsPrimary = _storage.GrokApiAccounts.Count == 0,
            IsActive = true
        };

        _storage.AddGrokApiAccount(account);
        Logger.Info($"Grok API account added: alias='{account.Alias}' id={account.Id}");
        AccountsChanged?.Invoke();
        if (account.IsPrimary) SelectAccount(account.Id);
        return (true, null, account);
    }

    public void RemoveAccount(string accountId)
    {
        _storage.RemoveGrokApiAccount(accountId);
        AccountsChanged?.Invoke();
        SelectedAccountChanged?.Invoke();
    }

    public string? GetApiKey(string accountId)
    {
        var acc = _storage.GrokApiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return null;
        return DpapiHelper.Unprotect(acc.EncryptedApiKey);
    }

    public async Task<(bool ok, string? error, GrokApiProvider.KeyInfo? info)> RefreshKeyInfoAsync(string accountId)
    {
        var key = GetApiKey(accountId);
        if (string.IsNullOrEmpty(key)) return (false, "API 키 없음", null);
        var (ok, err, info) = await _provider.ValidateKeyAsync(key);
        if (ok)
        {
            var acc = _storage.GrokApiAccounts.FirstOrDefault(a => a.Id == accountId);
            if (acc != null)
            {
                acc.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                acc.KeyName = info?.Name;
                acc.UserId = info?.UserId;
                acc.TeamId = info?.TeamId;
                acc.AllowedModels = info?.AllowedModels?.ToList() ?? new();
                _storage.Save();
            }
        }
        return (ok, err, info);
    }
}
