namespace Acordanza.Core.Services;

public interface IAnalyticsTracker
{
    Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null);
}
