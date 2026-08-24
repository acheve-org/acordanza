namespace Acordanza.Core.Services;

// Registered whenever Amplitude:ApiKey is absent (local dev, any environment that
// hasn't set it) — see AmplitudeAnalyticsTracker and each Program.cs's registration.
public class NoOpAnalyticsTracker : IAnalyticsTracker
{
    public Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null) =>
        Task.CompletedTask;
}
