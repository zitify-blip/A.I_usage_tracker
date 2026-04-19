using AIUsageTracker.Models;
using AIUsageTracker.Services.Providers;

namespace AIUsageTracker.Services;

public class OpenAiApiAccountService
{
    private readonly StorageService _storage;
    private readonly OpenAiApiProvider _provider;

    public event Action? AccountsChanged;
    public event Action? SelectedAccountChanged;

    public OpenAiApiAccountService(StorageService storage, OpenAiApiProvider provider)
    {
        _storage = storage;
        _provider = provider;
    }

    public IReadOnlyList<OpenAiApiAccount> GetAccounts() => _storage.OpenAiApiAccounts;

    public OpenAiApiAccount? GetSelected()
    {
        var id = _storage.SelectedOpenAiApiAccountId;
        if (string.IsNullOrEmpty(id))
            return _storage.OpenAiApiAccounts.FirstOrDefault(a => a.IsPrimary)
                ?? _storage.OpenAiApiAccounts.FirstOrDefault();
        return _storage.OpenAiApiAccounts.FirstOrDefault(a => a.Id == id)
            ?? _storage.OpenAiApiAccounts.FirstOrDefault();
    }

    public void SelectAccount(string accountId)
    {
        if (_storage.OpenAiApiAccounts.All(a => a.Id != accountId)) return;
        _storage.SetSelectedOpenAiApiAccount(accountId);
        SelectedAccountChanged?.Invoke();
    }

    public async Task<(bool ok, string? error, OpenAiApiAccount? account)> AddAccountAsync(string alias, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "API 키가 비어있습니다", null);

        var (ok, err, orgId) = await _provider.ValidateKeyAsync(apiKey);
        if (!ok)
        {
            Logger.Warn($"OpenAI AddAccount validation failed: {err}");
            return (false, err ?? "키 검증 실패", null);
        }

        var account = new OpenAiApiAccount
        {
            Alias = string.IsNullOrWhiteSpace(alias)
                ? $"account-{_storage.OpenAiApiAccounts.Count + 1}"
                : alias.Trim(),
            EncryptedApiKey = DpapiHelper.Protect(apiKey),
            KeyPreview = DpapiHelper.Mask(apiKey),
            OrganizationId = orgId,
            IsPrimary = _storage.OpenAiApiAccounts.Count == 0,
            IsActive = true
        };

        _storage.AddOpenAiApiAccount(account);
        Logger.Info($"OpenAI API account added: alias='{account.Alias}' id={account.Id} org={orgId}");
        AccountsChanged?.Invoke();
        if (account.IsPrimary) SelectAccount(account.Id);
        return (true, null, account);
    }

    public void RemoveAccount(string accountId)
    {
        _storage.RemoveOpenAiApiAccount(accountId);
        AccountsChanged?.Invoke();
        SelectedAccountChanged?.Invoke();
    }

    public string? GetApiKey(string accountId)
    {
        var acc = _storage.OpenAiApiAccounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return null;
        return DpapiHelper.Unprotect(acc.EncryptedApiKey);
    }

    public async Task<OpenAiApiProvider.UsageResult> FetchUsageAsync(string accountId, DateTimeOffset start, DateTimeOffset end)
    {
        var key = GetApiKey(accountId);
        if (string.IsNullOrEmpty(key))
            return new OpenAiApiProvider.UsageResult(false, "API 키 없음", Array.Empty<OpenAiApiProvider.UsageBucket>());
        var result = await _provider.FetchUsageAsync(key, start, end);
        if (result.Ok)
        {
            var acc = _storage.OpenAiApiAccounts.FirstOrDefault(a => a.Id == accountId);
            if (acc != null) acc.LastUsedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snapshots = result.Buckets.Select(b => new OpenAiApiUsageSnapshot
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                AccountId = accountId,
                Model = b.ModelId,
                InputTokens = b.InputTokens,
                OutputTokens = b.OutputTokens,
                CachedInputTokens = b.CachedInputTokens,
                CostUsd = b.CostUsd,
                PeriodStart = start.UtcDateTime.ToString("yyyy-MM-dd"),
                PeriodEnd = end.UtcDateTime.ToString("yyyy-MM-dd")
            }).ToList();
            _storage.SaveOpenAiApiUsage(snapshots);
        }
        return result;
    }
}
