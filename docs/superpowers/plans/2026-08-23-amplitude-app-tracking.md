# Amplitude App Usage Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Amplitude usage tracking — frontend page views plus two backend milestone events (invite accepted, route import completed) — all keyed to `ApplicationUser.Id`, active only in the deployed environment.

**Architecture:** A shared `IAnalyticsTracker` abstraction in `Acordanza.Core`, real (`AmplitudeAnalyticsTracker`, HTTP POST to Amplitude's ingestion API) only when `Amplitude:ApiKey` is configured, `NoOpAnalyticsTracker` otherwise — exactly the `IEmailSender`/`ResendEmailSender`/`ConsoleEmailSender` split already in this codebase. `Acordanza.Api` and `Acordanza.Worker` each register and call it independently (they share `Acordanza.Core` but never each other). The frontend wraps `@amplitude/analytics-browser` in `lib/analytics.ts`, gated the same way on `VITE_AMPLITUDE_API_KEY`, identifying the user after `/auth/me` and tracking one "Page Viewed" event per route change.

**Tech Stack:** .NET 10 (`Acordanza.Core`/`Acordanza.Api`/`Acordanza.Worker`, xUnit v3 + Shouldly), React 19 + TypeScript + Vite (Vitest + Testing Library + MSW), `@amplitude/analytics-browser`, Terraform (Azure Container Apps).

**Spec:** `docs/superpowers/specs/2026-08-23-amplitude-app-tracking-design.md`

## Global Constraints

- Amplitude is active only in the deployed environment — no API key anywhere in local dev or in any test fixture, so every test exercises `NoOpAnalyticsTracker` (backend) or the disabled path (frontend) unless a test explicitly constructs the real tracker/module state itself.
- `TrackEventAsync` (backend) and every exported `lib/analytics.ts` function (frontend) must never throw — an analytics failure must never break the invite-acceptance flow, the worker's route-import drain, or page rendering.
- One Amplitude project/API key for both frontend and backend — the same secret value is reused for `Amplitude:ApiKey` (backend) and `VITE_AMPLITUDE_API_KEY` (frontend build).
- All user-facing/event copy stays out of scope here — event names (`"Invite Accepted"`, `"Route Import Completed"`, `"Page Viewed"`) are internal Amplitude taxonomy, not user-facing text, so the root `CLAUDE.md`'s Spanish-only rule doesn't apply to them.

---

### Task 1: `IAnalyticsTracker` abstraction in `Acordanza.Core`

**Files:**
- Create: `backend/Acordanza.Core/Services/IAnalyticsTracker.cs`
- Create: `backend/Acordanza.Core/Services/NoOpAnalyticsTracker.cs`
- Create: `backend/Acordanza.Core/Services/AmplitudeAnalyticsTracker.cs`
- Test: `backend/Acordanza.Core.Tests/Services/AmplitudeAnalyticsTrackerTests.cs`

**Interfaces:**
- Produces: `IAnalyticsTracker.TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null): Task` — the one method Tasks 2 and 3 call. Never throws.
- Produces: `NoOpAnalyticsTracker` (parameterless constructor) and `AmplitudeAnalyticsTracker(HttpClient httpClient, IConfiguration configuration, ILogger<AmplitudeAnalyticsTracker> logger)` — the two DI-registered implementations Tasks 2 and 3 wire up.

- [ ] **Step 1: Write the failing tests**

```csharp
// backend/Acordanza.Core.Tests/Services/AmplitudeAnalyticsTrackerTests.cs
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend/Acordanza.Core.Tests && dotnet test --filter "FullyQualifiedName~AmplitudeAnalyticsTrackerTests"`
Expected: FAIL to compile — `Acordanza.Core.Services.AmplitudeAnalyticsTracker` doesn't exist yet.

- [ ] **Step 3: Write the interface and the two implementations**

```csharp
// backend/Acordanza.Core/Services/IAnalyticsTracker.cs
namespace Acordanza.Core.Services;

public interface IAnalyticsTracker
{
    Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null);
}
```

```csharp
// backend/Acordanza.Core/Services/NoOpAnalyticsTracker.cs
namespace Acordanza.Core.Services;

// Registered whenever Amplitude:ApiKey is absent (local dev, any environment that
// hasn't set it) — see AmplitudeAnalyticsTracker and each Program.cs's registration.
public class NoOpAnalyticsTracker : IAnalyticsTracker
{
    public Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null) =>
        Task.CompletedTask;
}
```

```csharp
// backend/Acordanza.Core/Services/AmplitudeAnalyticsTracker.cs
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
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend/Acordanza.Core.Tests && dotnet test --filter "FullyQualifiedName~AmplitudeAnalyticsTrackerTests"`
Expected: PASS, 4/4.

- [ ] **Step 5: Commit**

```bash
git add backend/Acordanza.Core/Services/IAnalyticsTracker.cs backend/Acordanza.Core/Services/NoOpAnalyticsTracker.cs backend/Acordanza.Core/Services/AmplitudeAnalyticsTracker.cs backend/Acordanza.Core.Tests/Services/AmplitudeAnalyticsTrackerTests.cs
git commit -m "feat: add IAnalyticsTracker abstraction for Amplitude tracking"
```

---

### Task 2: Wire the tracker into `Acordanza.Api` (invite acceptance)

**Files:**
- Modify: `backend/Acordanza.Api/Program.cs`
- Modify: `backend/Acordanza.Api/Controllers/AuthController.cs`

**Interfaces:**
- Consumes: `IAnalyticsTracker.TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null)` from Task 1.

No new test in this task: `NoOpAnalyticsTracker` is what every existing integration test resolves (no `Amplitude:ApiKey` in `ApiFixture`'s env — see Global Constraints), so the wiring is verified by the existing `AcceptInvite` integration test continuing to pass, not by a new assertion. `AmplitudeAnalyticsTracker`'s own behavior is already covered by Task 1's unit tests.

- [ ] **Step 1: Register the tracker in `Program.cs`**

Find the Resend registration block (`backend/Acordanza.Api/Program.cs`, right after the `DataProtectionTokenProviderOptions` configuration):

```csharp
// Resend needs a real API key (production); without one (local dev) fall
// back to logging the email instead, so the forgot-password flow can be
// exercised end-to-end with zero email-provider setup.
if (string.IsNullOrEmpty(builder.Configuration["Resend:ApiKey"]))
{
    builder.Services.AddSingleton<IEmailSender, ConsoleEmailSender>();
}
else
{
    builder.Services.AddHttpClient<ResendEmailSender>();
    builder.Services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<ResendEmailSender>());
}
```

Add immediately after it:

```csharp
// Amplitude is active only when a real API key is configured (deployed
// environments); local dev has none, so invite-acceptance tracking is a
// no-op — see backend/CLAUDE.md's Configuration table.
if (string.IsNullOrEmpty(builder.Configuration["Amplitude:ApiKey"]))
{
    builder.Services.AddSingleton<IAnalyticsTracker, NoOpAnalyticsTracker>();
}
else
{
    builder.Services.AddHttpClient<IAnalyticsTracker, AmplitudeAnalyticsTracker>();
}
```

(`Acordanza.Core.Services` is already `using`d in `Program.cs`.)

- [ ] **Step 2: Add `analyticsTracker` to `AuthController` and fire the event in `AcceptInvite`**

In `backend/Acordanza.Api/Controllers/AuthController.cs`, add `IAnalyticsTracker analyticsTracker` to the primary constructor parameter list:

```csharp
public class AuthController(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    JwtTokenService tokenService,
    IFido2 fido2,
    IMemoryCache cache,
    IConfiguration configuration,
    IEmailSender emailSender,
    IAnalyticsTracker analyticsTracker,
    ILogger<AuthController> logger) : ControllerBase
```

In `AcceptInvite`, right after the transaction block that saves the password and name, fire the event before the best-effort admin-notification loop:

```csharp
            await transaction.CommitAsync();
        }

        await analyticsTracker.TrackEventAsync(user.Id, "Invite Accepted");

        // Best-effort from here on — the invite has already been fully accepted (password
```

- [ ] **Step 3: Build and run the existing auth integration tests**

Run: `cd backend/Acordanza.Api.Tests && dotnet test --filter "FullyQualifiedName~AuthFlowsTests"`
Expected: PASS, including `Accepting_an_invite_sets_a_usable_password_on_a_pending_account` — the new tracker is a no-op in this suite, so behavior is unchanged.

- [ ] **Step 4: Commit**

```bash
git add backend/Acordanza.Api/Program.cs backend/Acordanza.Api/Controllers/AuthController.cs
git commit -m "feat: track invite acceptance as an Amplitude event"
```

---

### Task 3: Wire the tracker into `Acordanza.Worker` (route import completion)

**Files:**
- Modify: `backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs`
- Modify: `backend/Acordanza.Worker/Services/RouteImportService.cs`
- Modify: `backend/Acordanza.Worker.Tests/Services/RouteImportServiceTests.cs`
- Modify: `backend/Acordanza.Worker.Tests/Services/UrlImportServiceTests.cs`

**Interfaces:**
- Consumes: `IAnalyticsTracker.TrackEventAsync(...)` from Task 1.
- Produces: `RouteImportService`'s constructor now takes `IAnalyticsTracker analyticsTracker` as its second-to-last parameter (before `ILogger<RouteImportService> logger`) — every direct-construction call site in the test project must pass one.

- [ ] **Step 1: Write the failing tests**

Add to `backend/Acordanza.Worker.Tests/Services/RouteImportServiceTests.cs`, inside the `// --- Fakes ---` region (near `FakeExtraction`):

```csharp
    private sealed record TrackedEvent(string UserId, string EventType, IReadOnlyDictionary<string, object>? Properties);

    private sealed class FakeAnalyticsTracker : IAnalyticsTracker
    {
        public List<TrackedEvent> Events { get; } = [];

        public Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null)
        {
            Events.Add(new TrackedEvent(userId, eventType, eventProperties));
            return Task.CompletedTask;
        }
    }
```

Update `BuildService` to accept and pass an `IAnalyticsTracker`, defaulting to the fixture's registered (no-op) one so every existing call site keeps compiling unchanged:

```csharp
    private RouteImportService BuildService(
        AppDbContext db, ITopoImportGateProvider gate, ITopoExtractionProvider? extraction = null,
        IAnalyticsTracker? analyticsTracker = null)
    {
        var reader = new TopoImageReader(
            new AreaHierarchyService(db),
            gate,
            extraction ?? new FakeExtraction(CleanRead()),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            NullLogger<TopoImageReader>.Instance);

        return new RouteImportService(
            db,
            new AreaHierarchyService(db),
            [reader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            analyticsTracker ?? Fixture.Services.GetRequiredService<IAnalyticsTracker>(),
            NullLogger<RouteImportService>.Instance);
    }
```

Add two new tests, right after `A_clean_extraction_with_no_match_creates_a_pending_route`:

```csharp
    [Fact]
    public async Task A_created_route_fires_a_route_import_completed_analytics_event()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);
        var import = await SeedImportAsync(db, area, user.Id);
        var tracker = new FakeAnalyticsTracker();

        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead()), tracker)
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        var route = await db.Routes.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var trackedEvent = tracker.Events.ShouldHaveSingleItem();
        trackedEvent.UserId.ShouldBe(user.Id);
        trackedEvent.EventType.ShouldBe("Route Import Completed");
        trackedEvent.Properties.ShouldNotBeNull();
        trackedEvent.Properties!["routeId"].ShouldBe(route.Id);
        trackedEvent.Properties!["kind"].ShouldBe(RouteImportKind.Image.ToString());
    }

    [Fact]
    public async Task Matching_an_existing_route_does_not_fire_an_analytics_event()
    {
        using var scope = Fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await SeedData.CreateUserAsync(db);
        var area = await CreateAreaAsync(db);

        var existing = new ClimbingRoute
        {
            Name = "La Loca",
            Slug = "la-loca",
            AreaId = area.Id,
            Grade = "6a",
            Length = 30,
            AddedByUserId = user.Id,
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow
        };
        db.Routes.Add(existing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var import = await SeedImportAsync(db, area, user.Id);
        var tracker = new FakeAnalyticsTracker();

        await BuildService(db, new FakeGate(isTopo: true), new FakeExtraction(CleanRead(name: "LA LOCA")), tracker)
            .ImportAsync(import.Id, TestContext.Current.CancellationToken);

        tracker.Events.ShouldBeEmpty();
    }
```

Update the two direct-construction call sites in `backend/Acordanza.Worker.Tests/Services/UrlImportServiceTests.cs` to pass the fixture's tracker (they aren't asserting on it, so the plain registered one is enough):

```csharp
    private RouteImportService BuildService(AppDbContext db, IImportReader reader) =>
        new(db,
            new AreaHierarchyService(db),
            [reader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            Fixture.Services.GetRequiredService<IAnalyticsTracker>(),
            NullLogger<RouteImportService>.Instance);
```

and, further down, the second `new RouteImportService(...)` (the one built inline for the two-imports-in-one-run test):

```csharp
        var service = new RouteImportService(
            db,
            new AreaHierarchyService(db),
            [urlReader, imageReader],
            Fixture.Services.GetRequiredService<MediaUploadService>(),
            Fixture.Services.GetRequiredService<BlobStorageService>(),
            Fixture.Services.GetRequiredService<IAnalyticsTracker>(),
            NullLogger<RouteImportService>.Instance);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend/Acordanza.Worker.Tests && dotnet test --filter "FullyQualifiedName~RouteImportServiceTests|FullyQualifiedName~UrlImportServiceTests"`
Expected: FAIL to compile — `RouteImportService`'s constructor doesn't yet take an `IAnalyticsTracker`.

- [ ] **Step 3: Add the constructor parameter, register the tracker, and fire the event**

In `backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs`, add right after `services.AddScoped<AreaHierarchyService>();`:

```csharp
        services.AddScoped<AreaHierarchyService>();

        // Amplitude is active only when a real API key is configured (deployed
        // environments); local dev/tests have none, so route-import tracking is a
        // no-op — mirrors Acordanza.Api's Resend:ApiKey-gated IEmailSender registration.
        if (string.IsNullOrEmpty(configuration["Amplitude:ApiKey"]))
        {
            services.AddSingleton<IAnalyticsTracker, NoOpAnalyticsTracker>();
        }
        else
        {
            services.AddHttpClient<IAnalyticsTracker, AmplitudeAnalyticsTracker>();
        }
```

In `backend/Acordanza.Worker/Services/RouteImportService.cs`, add `IAnalyticsTracker analyticsTracker` to the primary constructor, before `logger`:

```csharp
public class RouteImportService(
    AppDbContext db,
    AreaHierarchyService areaHierarchy,
    IEnumerable<IImportReader> readers,
    MediaUploadService mediaUpload,
    BlobStorageService blobStorage,
    IAnalyticsTracker analyticsTracker,
    ILogger<RouteImportService> logger)
```

In `ImportAsync`, right after `import.RouteWasCreated = created;`, fire the event only when a route was actually created:

```csharp
        import.RouteId = route.Id;
        import.RouteWasCreated = created;
        if (created)
        {
            await analyticsTracker.TrackEventAsync(import.SubmittedByUserId, "Route Import Completed",
                new Dictionary<string, object> { ["routeId"] = route.Id, ["kind"] = import.Kind.ToString() });
        }
        var headline = created
            ? RouteImportOutcomes.RouteCreated
            : notes.Count > 0 ? RouteImportOutcomes.RouteExisted : RouteImportOutcomes.RouteMatched;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend/Acordanza.Worker.Tests && dotnet test --filter "FullyQualifiedName~RouteImportServiceTests|FullyQualifiedName~UrlImportServiceTests"`
Expected: PASS, including the full existing `RouteImportServiceTests`/`UrlImportServiceTests` suites (they still compile and pass unchanged) plus the two new tests.

Then run the full worker suite to make sure nothing else constructs `RouteImportService` directly:

Run: `cd backend/Acordanza.Worker.Tests && dotnet test`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs backend/Acordanza.Worker/Services/RouteImportService.cs backend/Acordanza.Worker.Tests/Services/RouteImportServiceTests.cs backend/Acordanza.Worker.Tests/Services/UrlImportServiceTests.cs
git commit -m "feat: track successful route imports as an Amplitude event"
```

---

### Task 4: Frontend `lib/analytics.ts` module

**Files:**
- Modify: `frontend/package.json` (add `@amplitude/analytics-browser`)
- Create: `frontend/src/lib/analytics.ts`
- Test: `frontend/src/lib/analytics.test.ts`

**Interfaces:**
- Produces: `init(): void`, `identify(userId: string): void`, `reset(): void`, `trackPageView(path: string): void` — the four functions Task 5 calls from `main.tsx`/`AuthContext.tsx`/`App.tsx`.

- [ ] **Step 1: Add the dependency**

Run (from `frontend/`): `bun add @amplitude/analytics-browser`

- [ ] **Step 2: Write the failing tests**

```ts
// frontend/src/lib/analytics.test.ts
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { initMock, setUserIdMock, resetMock, trackMock } = vi.hoisted(() => ({
  initMock: vi.fn(),
  setUserIdMock: vi.fn(),
  resetMock: vi.fn(),
  trackMock: vi.fn(),
}));

vi.mock('@amplitude/analytics-browser', () => ({
  init: initMock,
  setUserId: setUserIdMock,
  reset: resetMock,
  track: trackMock,
}));

describe('lib/analytics', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('does nothing when no API key is configured (local dev)', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', '');
    const analytics = await import('./analytics');

    analytics.init();
    analytics.identify('user-1');
    analytics.trackPageView('/routes');
    analytics.reset();

    expect(initMock).not.toHaveBeenCalled();
    expect(setUserIdMock).not.toHaveBeenCalled();
    expect(trackMock).not.toHaveBeenCalled();
    expect(resetMock).not.toHaveBeenCalled();
  });

  it('initializes and forwards every call when an API key is configured', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.init();
    analytics.identify('user-1');
    analytics.trackPageView('/routes');
    analytics.reset();

    expect(initMock).toHaveBeenCalledWith('test-key', expect.objectContaining({ defaultTracking: false }));
    expect(setUserIdMock).toHaveBeenCalledWith('user-1');
    expect(trackMock).toHaveBeenCalledWith('Page Viewed', { path: '/routes' });
    expect(resetMock).toHaveBeenCalledOnce();
  });

  it('stays disabled if identify/trackPageView/reset are called before init', async () => {
    vi.stubEnv('VITE_AMPLITUDE_API_KEY', 'test-key');
    const analytics = await import('./analytics');

    analytics.identify('user-1');

    expect(setUserIdMock).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd frontend && bun run test src/lib/analytics.test.ts`
Expected: FAIL — `./analytics` doesn't exist yet.

- [ ] **Step 4: Write the module**

```ts
// frontend/src/lib/analytics.ts
import * as amplitude from '@amplitude/analytics-browser';

const apiKey = import.meta.env.VITE_AMPLITUDE_API_KEY;
let enabled = false;

// defaultTracking is off on purpose: Amplitude's autocapture would track clicks/sessions/
// attribution beyond the scoped "one Page Viewed event per route change" this app tracks
// itself (see App.tsx) — see docs/superpowers/specs/2026-08-23-amplitude-app-tracking-design.md.
export function init(): void {
  if (!apiKey) return;
  amplitude.init(apiKey, { defaultTracking: false });
  enabled = true;
}

export function identify(userId: string): void {
  if (!enabled) return;
  amplitude.setUserId(userId);
}

export function reset(): void {
  if (!enabled) return;
  amplitude.reset();
}

export function trackPageView(path: string): void {
  if (!enabled) return;
  amplitude.track('Page Viewed', { path });
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && bun run test src/lib/analytics.test.ts`
Expected: PASS, 3/3.

- [ ] **Step 6: Commit**

```bash
git add frontend/package.json frontend/bun.lock frontend/src/lib/analytics.ts frontend/src/lib/analytics.test.ts
git commit -m "feat: add lib/analytics.ts wrapper over the Amplitude browser SDK"
```

---

### Task 5: Wire the frontend module into the app (identify, reset, page views)

**Files:**
- Modify: `frontend/src/main.tsx`
- Modify: `frontend/src/context/AuthContext.tsx`
- Modify: `frontend/src/context/AuthContext.test.tsx`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/App.test.tsx`

**Interfaces:**
- Consumes: `init`, `identify`, `reset`, `trackPageView` from `lib/analytics.ts` (Task 4).

- [ ] **Step 1: Write the failing tests**

In `frontend/src/context/AuthContext.test.tsx`, add the hoisted mock near the top (after the existing imports, before `const STORAGE_KEY = ...`):

```tsx
const { identifyMock, resetAnalyticsMock } = vi.hoisted(() => ({
  identifyMock: vi.fn(),
  resetAnalyticsMock: vi.fn(),
}));
vi.mock('../lib/analytics', () => ({
  init: vi.fn(),
  identify: identifyMock,
  reset: resetAnalyticsMock,
  trackPageView: vi.fn(),
}));
```

Add these two tests inside the `describe('AuthProvider', ...)` block:

```tsx
  it('identifies the user in Amplitude after a successful session restore', async () => {
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ id: 'user-9' }))));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });

    await waitFor(() => expect(identifyMock).toHaveBeenCalledWith('user-9'));
  });

  it('resets Amplitude identity on logout', async () => {
    const user = userEvent.setup();
    localStorage.setItem(STORAGE_KEY, storedSession('good-token'));
    server.use(http.get(`${API_URL}/auth/me`, () => HttpResponse.json(makeMeResponse({ id: 'user-9' }))));

    renderWithProviders(<Probe />, { useRealAuthProvider: true });
    await waitFor(() => expect(screen.getByTestId('probe')).toHaveTextContent('alex@example.com'));

    await user.click(screen.getByRole('button', { name: 'Cerrar sesión' }));

    expect(resetAnalyticsMock).toHaveBeenCalled();
  });
```

In `frontend/src/App.test.tsx`, add the hoisted mock near the top (after the existing imports):

```tsx
const { trackPageViewMock } = vi.hoisted(() => ({ trackPageViewMock: vi.fn() }));
vi.mock('./lib/analytics', () => ({
  init: vi.fn(),
  identify: vi.fn(),
  reset: vi.fn(),
  trackPageView: trackPageViewMock,
}));
```

Add a new `describe` block:

```tsx
describe('App page-view tracking', () => {
  it('tracks a page view on initial render and again after navigating', async () => {
    mockRouteList();
    mockRouteDetail();

    renderApp('/routes');
    await screen.findByPlaceholderText('Nombre de la vía o descripción…');
    expect(trackPageViewMock).toHaveBeenCalledWith('/routes');

    const card = await screen.findByText('Travesía del Gato');
    act(() => card.click());

    await waitFor(() => expect(trackPageViewMock).toHaveBeenCalledWith('/routes/pena-rua/travesia-del-gato'));
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && bun run test src/context/AuthContext.test.tsx src/App.test.tsx`
Expected: FAIL — `../lib/analytics`/`./lib/analytics` mocks target a module that isn't imported/called anywhere yet, so `identifyMock`/`resetAnalyticsMock`/`trackPageViewMock` are never invoked.

- [ ] **Step 3: Wire `main.tsx`**

```tsx
// frontend/src/main.tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import './styles.css';
import App from './App.tsx';
import { AuthProvider } from './context/AuthContext.tsx';
import { PendingTasksProvider } from './context/PendingTasksContext.tsx';
import { LoadingBar } from './components/LoadingBar.tsx';
import { queryClient } from './queryClient.ts';
import * as analytics from './lib/analytics.ts';

analytics.init();

createRoot(document.getElementById('root')!).render(
```

(rest of the file unchanged)

- [ ] **Step 4: Wire `AuthContext.tsx`**

Add the import near the top (`lib/analytics.ts` exports named functions, so import it as a namespace):

```tsx
import * as analytics from '../lib/analytics';
```

In the bootstrap `useEffect`'s success callback, after `setIsAreaExpert(me.isAreaExpert);`:

```tsx
        setIsAreaExpert(me.isAreaExpert);
        analytics.identify(me.id);
      })
```

In `applySession`, after `setIsAreaExpert(me.isAreaExpert);`:

```tsx
    setIsAreaExpert(me.isAreaExpert);
    analytics.identify(me.id);
  }
```

In `clearSession`, add the reset call (anywhere in the body — e.g. right after the `localStorage.removeItem`/`setAuthToken(null)` lines):

```tsx
  function clearSession() {
    localStorage.removeItem(STORAGE_KEY);
    setAuthToken(null);
    analytics.reset();
    queryClient.clear();
```

- [ ] **Step 5: Wire `App.tsx`**

Add `useEffect` to the React import:

```tsx
import { useEffect } from 'react';
import { Link, NavLink, Navigate, Outlet, Route, Routes, useLocation } from 'react-router';
```

Add the analytics import alongside the other top-level imports:

```tsx
import * as analytics from './lib/analytics';
```

In `App()`, right after `const location = useLocation();` and before the `/reset-password` early return:

```tsx
function App() {
  const { username, loading } = useAuth();
  const location = useLocation();

  useEffect(() => {
    analytics.trackPageView(location.pathname);
  }, [location.pathname]);

  // Reachable while logged out — this is where the password-reset email links.
  if (location.pathname === '/reset-password') return <ResetPasswordScreen />;
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd frontend && bun run test src/context/AuthContext.test.tsx src/App.test.tsx`
Expected: PASS, including the four new tests.

Then run the full frontend suite to catch any other regression:

Run: `cd frontend && bun run test`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/main.tsx frontend/src/context/AuthContext.tsx frontend/src/context/AuthContext.test.tsx frontend/src/App.tsx frontend/src/App.test.tsx
git commit -m "feat: identify users and track page views with Amplitude"
```

---

### Task 6: Infra, CSP, and docs

**Files:**
- Modify: `frontend/nginx.conf`
- Modify: `frontend/Dockerfile`
- Modify: `.github/workflows/ci-cd.yml`
- Modify: `infra/terraform/variables.tf`
- Modify: `infra/terraform/container_app.tf`
- Modify: `infra/terraform/container_app_job.tf`
- Modify: `infra/README.md`
- Modify: `frontend/.env.example`
- Modify: `backend/CLAUDE.md`
- Modify: `frontend/CLAUDE.md`
- Modify: `CLAUDE.md` (root)

No new automated test — this task is config/infra/docs plumbing, verified by a local Docker build (frontend) and `terraform validate` (infra), per the steps below.

- [ ] **Step 1: Allow Amplitude in the frontend's CSP**

In `frontend/nginx.conf`, the `Content-Security-Policy` header currently ends `connect-src 'self' __API_ORIGIN__`. The Amplitude browser SDK posts events to `https://api2.amplitude.com` — add it to `connect-src`:

```
add_header Content-Security-Policy "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data: https://*.tile.opentopomap.org https://*.blob.core.windows.net; connect-src 'self' __API_ORIGIN__ https://api2.amplitude.com" always;
```

Also update the CSP comment block above it (the `connect-src` bullet) to mention Amplitude:

```
    # - connect-src: the API origin (substituted at image build time from
    #   VITE_API_URL, see Dockerfile) plus Amplitude's fixed ingestion host
    #   (https://api2.amplitude.com) for the browser SDK — nothing else.
```

- [ ] **Step 2: Bake `VITE_AMPLITUDE_API_KEY` into the frontend build**

In `frontend/Dockerfile`, right after the existing `VITE_API_URL` `ARG`/`ENV` pair:

```dockerfile
ARG VITE_API_URL=http://localhost:5073/api
ENV VITE_API_URL=$VITE_API_URL
ARG VITE_AMPLITUDE_API_KEY=""
ENV VITE_AMPLITUDE_API_KEY=$VITE_AMPLITUDE_API_KEY
RUN bun run --cwd design-system build
```

- [ ] **Step 3: Verify the frontend image still builds**

Run (from the repo root): `docker build -f frontend/Dockerfile -t acordanza-frontend-verify --build-arg VITE_AMPLITUDE_API_KEY=test-key .`
Expected: build succeeds (this also exercises the existing `__API_ORIGIN__` CSP substitution check, unaffected by this change).

- [ ] **Step 4: Pass the key through CI/CD and Terraform**

In `.github/workflows/ci-cd.yml`'s top-level `env:` block, alongside the existing `TF_VAR_resend_api_key`:

```yaml
  TF_VAR_amplitude_api_key: ${{ secrets.AMPLITUDE_API_KEY }}
```

In the `build-and-deploy-frontend` job's "Build and push image" step, add the build arg:

```yaml
      - name: Build and push image
        env:
          IMAGE: ${{ needs.terraform.outputs.acr_login_server }}/${{ env.ACR_IMAGE_NAME_WEB }}:${{ github.sha }}
        run: |
          docker build \
            --build-arg VITE_API_URL="https://${{ needs.terraform.outputs.backend_custom_domain }}/api" \
            --build-arg VITE_AMPLITUDE_API_KEY="${{ secrets.AMPLITUDE_API_KEY }}" \
            -t "$IMAGE" -f frontend/Dockerfile .
          docker push "$IMAGE"
```

- [ ] **Step 5: Add the Terraform variable and wire it into both container apps**

In `infra/terraform/variables.tf`, alongside `resend_api_key` — optional (default empty string) since Amplitude tracking is allowed to be unconfigured, unlike Resend which the app already depends on in production:

```hcl
variable "amplitude_api_key" {
  description = "Amplitude project API key (Amplitude:ApiKey on the API/worker, VITE_AMPLITUDE_API_KEY on the frontend build). Supply via TF_VAR_amplitude_api_key (a GitHub Actions secret) — never commit this. Leave empty to disable tracking."
  type        = string
  sensitive   = true
  default     = ""
}
```

In `infra/terraform/container_app.tf`, add the secret alongside `resend-api-key`:

```hcl
  secret {
    name  = "amplitude-api-key"
    value = var.amplitude_api_key
  }
```

and the env var alongside `Resend__ApiKey`:

```hcl
      env {
        name        = "Amplitude__ApiKey"
        secret_name = "amplitude-api-key"
      }
```

In `infra/terraform/container_app_job.tf`, add the same secret and env var (mirroring the `db-connection-string` secret's shape):

```hcl
  secret {
    name  = "amplitude-api-key"
    value = var.amplitude_api_key
  }
```

```hcl
      env {
        name        = "Amplitude__ApiKey"
        secret_name = "amplitude-api-key"
      }
```

- [ ] **Step 6: Validate the Terraform changes**

Run: `cd infra/terraform && terraform fmt -check && terraform validate`
Expected: no formatting diffs, `Success! The configuration is valid.` (Terraform is pinned via `.prototools` — run `proto install` first if it isn't on `PATH`.)

- [ ] **Step 7: Document the new secret and config keys**

In `infra/README.md`, under "**Set GitHub Actions repo secrets**", add a bullet after `RESEND_API_KEY`:

```markdown
   - `AMPLITUDE_API_KEY` — an Amplitude project API key, used both to send
     backend events (`Amplitude:ApiKey`) and to initialize the frontend's
     browser SDK (`VITE_AMPLITUDE_API_KEY`) — the same value serves both.
     Optional: leave the GitHub secret unset (or empty) to deploy with
     tracking disabled.
```

In `backend/CLAUDE.md`'s Configuration table, add a row after `Resend:ApiKey` / `Resend:FromEmail` / `Resend:FromName`:

```markdown
| `Amplitude:ApiKey` | — | Amplitude project API key for `IAnalyticsTracker` (invite-accepted/route-import-completed events); absent locally and gates `AmplitudeAnalyticsTracker` vs `NoOpAnalyticsTracker`, same pattern as `Resend:ApiKey` |
```

In `frontend/CLAUDE.md`'s Architecture section, add a bullet after the `api/client.ts` bullet documenting the new module:

```markdown
- `lib/analytics.ts` — thin wrapper over `@amplitude/analytics-browser`, gated on `VITE_AMPLITUDE_API_KEY` (unset locally, so every exported function no-ops). `main.tsx` calls `init()` once; `AuthContext` calls `identify(user.id)` after every successful `/auth/me` and `reset()` on logout; `App.tsx` calls `trackPageView(pathname)` on every route change.
```

In the root `CLAUDE.md`'s "Deploying" bullet, add `Amplitude:ApiKey` to the list of API env vars and mention the frontend's build-time key, right after the existing `Resend:ApiKey` mention:

```markdown
an Azure Blob Storage account (`Storage:*`, for topo images and Data Protection key persistence), `Amplitude:ApiKey` (optional — Amplitude usage tracking, no-op when unset), and ideally `Resend:ApiKey` (transactional email; falls back to console logging without one)
```

and, in the same bullet's frontend sentence:

```markdown
frontend is a static build (`bun run build` → `frontend/dist`, which builds `design-system/` first) with `VITE_API_URL` and `VITE_AMPLITUDE_API_KEY` (same value as the backend's `Amplitude:ApiKey`, optional) baked in at build time
```

In `frontend/.env.example`, add a line for the new variable, following whatever format `VITE_API_URL` already uses there (e.g. `VITE_AMPLITUDE_API_KEY=`).

- [ ] **Step 8: Commit**

```bash
git add frontend/nginx.conf frontend/Dockerfile .github/workflows/ci-cd.yml infra/terraform/variables.tf infra/terraform/container_app.tf infra/terraform/container_app_job.tf infra/README.md frontend/.env.example backend/CLAUDE.md frontend/CLAUDE.md CLAUDE.md
git commit -m "chore: wire Amplitude API key through infra, CI, and docs"
```
