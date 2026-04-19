using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

public class AnthropicApiAccountService
{
    private readonly StorageService _storage;
    private readonly AnthropicApiProvider _provider;

    public event Action? AccountsChanged;
    public event Action? SelectedAccountChanged;

    public AnthropicApiAccountService(StorageService storage, AnthropicApiProvider provider)
    {
        _storage = storage;
        _provider = provider;
    }

    public IReadOnlyList<AnthropicApiAccount> GetAccounts() => _storage.AnthropicApiAccounts;

    public AnthropicApiAccount? GetSelected()
    {
        var id = _storage.SelectedAnthropicApiAccountId;
        if (string.IsNullOrEmpty(id))
            return _storage.AnthropicApiAccounts.FirstOrDefault(a => a.IsPrimary)
                ?? _storage.AnthropicApiAccounts.FirstOrDefault();
        return _storage.AnthropicApiAccounts.FirstOrDefault(a => a.Id == id)
            ?? _storage.AnthropicApiAccounts.FirstOrDefault();
    }

    public void SelectAccount(string accountId)
    {
        if (_storage.AnthropicApiAccounts.All(a => a.Id != accountId)) return;
        _storage.SetSelectedAnthropicApiAccount(accountId);
        SelectedAccountChanged?.Invoke();
    }

    public async Task<(bool ok, string? error, AnthropicApiAccount? account)> AddAccountAsync(string alias, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API 키가 비어있습니다", null);

        var (ok, err, orgId) = await _provider.ValidateKeyAsync(apiKey);
        if (!ok)
        {
            Logger.Warn($"Anthropic AddAccount validation failed: {err}");
            return (false, err ?? "키 검증 실패", null);
        }

        var account = new AnthropicApiAccount
        {
            Alias = string.IsNullOrWhiteSpace(alias)
                ? $"account-{_storage.AnthropicApiAccounts.Count + 1}"
                : alias.Trim(),
            EncryptedApiKey = DpapiHelper.Protect(apiKey),
            KeyPreview = DpapiHelper.Mask(apiKey),
            OrganizationId = orgId,
            IsPrimary = _storage.AnthropicApiAccounts.Count == 0,
            IsActive = true
        };

        _storage.AddAnthropicApiAccount(account);
        Logger.Info($"Anthropic API account added: alias='{account.Alias}' id={account.Id} org={orgId}");
        AccountsChanged?.Invoke();
        if (account.IsPrimary) SelectAccount(account.Id);
        return (true, null, account);
    }

    public void RemoveAccount(string accountId)
    {
        _storage.RemoveAnthropicApiAccount(accountId);
        AccountsChanged?.Invoke();
        SelectedAccountChanged?.Invoke();
    }

    public void RenameAccount(string accountId, string newAlias)
    {
        var acc = _storage.AnthropicApiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return;
        acc.Alias = newAlias.Trim();
        _storage.Save();
        AccountsChanged?.Invoke();
    }

    public string? GetApiKey(string accountId)
    {
        var acc = _storage.AnthropicApiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return null;
        return DpapiHelper.Unprotect(acc.EncryptedApiKey);
    }

    public async Task<AnthropicApiProvider.UsageResult> FetchUsageAsync(string accountId, DateTimeOffset start, DateTimeOffset end)
    {
        var key = GetApiKey(accountId);
        if (string.IsNullOrEmpty(key))
            return new AnthropicApiProvider.UsageResult(false, "API 키 없음", Array.Empty<AnthropicApiProvider.UsageBucket>());
        var result = await _provider.FetchUsageAsync(key, start, end);
        if (result.Ok)
        {
            var acc = _storage.AnthropicApiAccounts.FirstOrDefault(a => a.Id == accountId);
            if (acc != null) acc.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snapshots = result.Buckets.Select(b => new AnthropicApiUsageSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccountId = accountId,
                Model = b.ModelId,
                InputTokens = b.InputTokens,
                OutputTokens = b.OutputTokens,
                CacheWriteTokens = b.CacheCreationTokens,
                CacheReadTokens = b.CacheReadTokens,
                CostUsd = b.CostUsd,
                PeriodStart = start.UtcDateTime.ToString("yyyy-MM-dd"),
                PeriodEnd = end.UtcDateTime.ToString("yyyy-MM-dd")
            }).ToList();
            _storage.SaveAnthropicApiUsage(snapshots);
        }
        return result;
    }
}
