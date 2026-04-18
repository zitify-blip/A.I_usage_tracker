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
            Logger.Error("Storage save failed", ex);
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

}
