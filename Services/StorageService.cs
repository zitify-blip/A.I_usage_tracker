using System.IO;
using System.Text.Json;
using ClaudeUsageTracker.Models;

namespace ClaudeUsageTracker.Services;

public class StorageService
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTracker");

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
        catch
        {
            _data = new StorageData();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(StorageDir);
            var json = JsonSerializer.Serialize(_data, JsonOptions);

            // Write to temp file first, then replace atomically to prevent corruption
            var tempPath = StoragePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StoragePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Storage] Save failed: {ex.Message}");
        }
    }

    public List<UsageSnapshot> GetHistory() => _data.UsageHistory;

    public void SaveSnapshot(UsageSnapshot snapshot)
    {
        if (snapshot.FiveHourUtilization is < 0 or > 100) return;
        if (snapshot.SevenDayUtilization is < 0 or > 100) return;

        _data.UsageHistory.Add(snapshot);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
        _data.UsageHistory.RemoveAll(s => s.Timestamp < cutoff);

        Save();
    }

}
