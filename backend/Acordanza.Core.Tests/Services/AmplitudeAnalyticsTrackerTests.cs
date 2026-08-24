using System.Net;
using System.Text.Json;
using Acordanza.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Acordanza.Core.Tests.Services;

// AmplitudeAnalyticsTracker.TrackEventAsync must never throw — a delivery failure here
// must never break the invite-acceptance flow (Acordanza.Api) or the worker's route-import
// drain (Acordanza.Worker), the only two call sites. See backend/CLAUDE.md's Configuration
// table for Amplitude:ApiKey, and the spec at
// docs/superpowers/specs/2026-08-23-amplitude-app-tracking-design.md.
public class AmplitudeAnalyticsTrackerTests
{
    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("network down");
    }

    private static AmplitudeAnalyticsTracker BuildTracker(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Amplitude:ApiKey"] = "test-api-key" })
            .Build();
        return new AmplitudeAnalyticsTracker(new HttpClient(handler), configuration, NullLogger<AmplitudeAnalyticsTracker>.Instance);
    }

    [Fact]
    public async Task TrackEventAsync_posts_the_event_with_the_api_key_user_id_and_properties()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var tracker = BuildTracker(handler);

        await tracker.TrackEventAsync("user-42", "Invite Accepted", new Dictionary<string, object> { ["routeId"] = 7 });

        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.RequestUri.ShouldBe(new Uri("https://api2.amplitude.com/2/httpapi"));
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.GetProperty("api_key").GetString().ShouldBe("test-api-key");
        var evt = body.RootElement.GetProperty("events")[0];
        evt.GetProperty("user_id").GetString().ShouldBe("user-42");
        evt.GetProperty("event_type").GetString().ShouldBe("Invite Accepted");
        evt.GetProperty("event_properties").GetProperty("routeId").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task TrackEventAsync_omits_event_properties_when_none_are_given()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var tracker = BuildTracker(handler);

        await tracker.TrackEventAsync("user-42", "Invite Accepted");

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var evt = body.RootElement.GetProperty("events")[0];
        evt.GetProperty("event_properties").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task TrackEventAsync_swallows_a_failing_response_instead_of_throwing()
    {
        var tracker = BuildTracker(new FakeHttpMessageHandler(HttpStatusCode.BadRequest));

        await Should.NotThrowAsync(() => tracker.TrackEventAsync("user-42", "Invite Accepted"));
    }

    [Fact]
    public async Task TrackEventAsync_swallows_a_transport_exception_instead_of_throwing()
    {
        var tracker = BuildTracker(new ThrowingHttpMessageHandler());

        await Should.NotThrowAsync(() => tracker.TrackEventAsync("user-42", "Invite Accepted"));
    }
}
