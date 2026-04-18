namespace AIUsageTracker.Services.Providers;

public interface IUsageProvider
{
    string Id { get; }
    string DisplayName { get; }
}
