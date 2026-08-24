using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Acordanza.Core.Services;

// Fire-and-forget: a delivery failure must never break the flow that produced the event
// (invite acceptance in Acordanza.Api, route-import completion in Acordanza.Worker) —
// same best-effort spirit as Acordanza.Api's Notifications.BestEffortAsync, inlined here
// since this is the only caller of the HTTP call. Registered only when Amplitude:ApiKey is
// configured (see each Program.cs) — NoOpAnalyticsTracker covers the rest.
public class AmplitudeAnalyticsTracker(HttpClient httpClient, IConfiguration configuration, ILogger<AmplitudeAnalyticsTracker> logger)
    : IAnalyticsTracker
{
    private const string IngestUrl = "https://api2.amplitude.com/2/httpapi";

    public async Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null)
    {
        try
        {
            var apiKey = configuration["Amplitude:ApiKey"]
                ?? throw new InvalidOperationException("Missing Amplitude:ApiKey configuration");

            var payload = new
            {
                api_key = apiKey,
                events = new[]
                {
                    new
                    {
                        user_id = userId,
                        event_type = eventType,
                        event_properties = eventProperties,
                        time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    },
                },
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(IngestUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Amplitude event {EventType} for user {UserId} failed ({StatusCode}): {Body}",
                    eventType, userId, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Amplitude event {EventType} for user {UserId} failed to send", eventType, userId);
        }
    }
}
