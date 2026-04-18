using System.Security.Cryptography;
using System.Text;

namespace AIUsageTracker.Services;

public static class DpapiHelper
{
    // Entropy literal must stay as-is to decrypt keys stored under the old name.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClaudeUsageTracker.v1.GeminiKey");

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return "";
        try
        {
            var encrypted = Convert.FromBase64String(cipherText);
            var bytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Logger.Warn("DPAPI unprotect failed", ex);
            return "";
        }
    }

    public static string Mask(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "";
        if (apiKey.Length <= 8) return new string('*', apiKey.Length);
        return apiKey[..4] + new string('*', 4) + apiKey[^4..];
    }
}
