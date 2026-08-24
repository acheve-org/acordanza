# Amplitude app usage tracking

Status: Draft
Issue: none (first pass at analytics)

## Purpose

Acordanza currently has no visibility into which parts of the app people actually use. This adds Amplitude tracking so usage (which sections/pages get visited, and two meaningful server-side milestones) can be analyzed per user, without adding a UI-visible feature or changing any existing behavior.

## Scope

- Frontend: page-view tracking only, keyed to the logged-in user's id.
- Backend: two server-side events that have no corresponding page view — invite acceptance and successful route import — sent from `Acordanza.Api` and `Acordanza.Worker` respectively.
- Amplitude is active only in the deployed environment. Local dev has no API key configured anywhere, so all tracking is a no-op — no local setup, no dev-data pollution of the production Amplitude project.
- Out of scope for v1: custom UI interaction events (ticks, opens, invites sent, etc.), a second dev/staging Amplitude project, consent banners (this is a closed app for a known group of friends, not a public product).

## Architecture

Two independent tracking paths that both key events to the same identifier (`ApplicationUser.Id`, a string GUID) so a person's frontend and backend activity can be stitched together in Amplitude:

- **Frontend**: Amplitude's browser SDK (`@amplitude/analytics-browser`), initialized once at app bootstrap, identifies the user after login/session-restore and emits one "Page Viewed" event per route change.
- **Backend**: a small shared abstraction in `Acordanza.Core` (`IAnalyticsTracker`), following the exact `IEmailSender`/`ResendEmailSender`/`ConsoleEmailSender` pattern already in the codebase — a real implementation that calls Amplitude's HTTP API when an API key is configured, and a no-op otherwise. Used by both `Acordanza.Api` (invite acceptance) and `Acordanza.Worker` (route import completion), since they share `Acordanza.Core` but not each other.

No new database tables, no new queue, no new background job — this is a synchronous, best-effort side effect on top of two existing flows.

## Components

### Backend: `IAnalyticsTracker` (`Acordanza.Core/Services/`)

```csharp
public interface IAnalyticsTracker
{
    Task TrackEventAsync(string userId, string eventType, IReadOnlyDictionary<string, object>? eventProperties = null);
}
```

- `AmplitudeAnalyticsTracker(HttpClient, IConfiguration)` — POSTs to `https://api2.amplitude.com/2/httpapi` with `{ api_key, events: [{ user_id, event_type, event_properties, time }] }`. Reads `Amplitude:ApiKey` from configuration (same key name in both `Acordanza.Api` and `Acordanza.Worker` config, mirroring how `Anthropic:ManagedIdentityClientId` is duplicated per-process rather than shared). `time` is Unix millis (`DateTimeOffset.UtcNow`).
- `NoOpAnalyticsTracker` — does nothing; used whenever `Amplitude:ApiKey` is absent (local dev, and any environment that hasn't set it).
- Both processes register the tracker exactly like `Program.cs` already registers `IEmailSender`:
  ```csharp
  if (!string.IsNullOrEmpty(configuration["Amplitude:ApiKey"]))
      builder.Services.AddHttpClient<IAnalyticsTracker, AmplitudeAnalyticsTracker>();
  else
      builder.Services.AddSingleton<IAnalyticsTracker, NoOpAnalyticsTracker>();
  ```
- Failures never propagate: `TrackEventAsync` catches and logs (`LogWarning`) any HTTP failure or exception rather than throwing. Analytics must never break an invite acceptance or a route-import completion — same best-effort spirit as `Notifications.BestEffortAsync`, but inlined into the tracker itself since there's only one call, not a shared wrapper used by multiple controllers.

### Backend call sites

- **`AuthController.AcceptInvite`** (`Acordanza.Api`) — after the account is confirmed (`ConfirmedAt` set, existing logic unchanged), fire `TrackEventAsync(user.Id, "Invite Accepted")`. No properties needed; the fact of acceptance plus the user id is the whole signal.
- **`RouteImportService.CreateOrMatchRouteAsync`-equivalent path** (`Acordanza.Worker`, around the existing `import.RouteWasCreated = created;` assignment, backend/Acordanza.Worker/Services/RouteImportService.cs:183) — when `created == true`, fire `TrackEventAsync(import.SubmittedByUserId, "Route Import Completed", new Dictionary<string, object> { ["routeId"] = route.Id, ["kind"] = import.Kind.ToString() })` before `FinishAsync`. Matched-to-an-existing-route and Discarded/Failed outcomes do **not** fire an event — the metric means "a route was actually added," not "the pipeline ran."

### Frontend: `frontend/src/lib/analytics.ts`

Thin wrapper over `@amplitude/analytics-browser`, so call sites never touch the SDK directly and the no-op-without-a-key behavior lives in one place:

```ts
export function init(): void        // amplitude.init(apiKey) if VITE_AMPLITUDE_API_KEY set, else no-op
export function identify(userId: string): void   // amplitude.setUserId(userId)
export function reset(): void                     // amplitude.reset() — called on logout
export function trackPageView(path: string): void // amplitude.track('Page Viewed', { path })
```

Every exported function checks an internal `enabled` flag (true only if `init()` saw an API key) and returns immediately if disabled — no conditional checks needed at call sites.

### Frontend call sites

- `frontend/src/main.tsx` — call `analytics.init()` once, before rendering `<App />`.
- `frontend/src/contexts/AuthContext.tsx` (or wherever `/auth/me` is fetched into state) — call `analytics.identify(user.id)` right after a successful `/auth/me` response, whether that came from login or from restoring a token on page load; call `analytics.reset()` in the logout path.
- `frontend/src/App.tsx` — a small effect keyed on `useLocation().pathname` calls `analytics.trackPageView(pathname)` on every route change. Uses the raw pathname (`/routes`, `/fa/some-slug`, ...), not a translated section label — route slugs are already English identifiers per the root `CLAUDE.md`'s language rule, and Amplitude's own UI is where humans read the data, not the app's UI.

## Configuration

New keys, following the existing table shape in each `CLAUDE.md`:

| Key | Where | Default | Notes |
|---|---|---|---|
| `Amplitude:ApiKey` | `Acordanza.Api`, `Acordanza.Worker` | — | absent locally; set only in the deployed environment's config/Terraform. Presence gates `AmplitudeAnalyticsTracker` vs `NoOpAnalyticsTracker`, same pattern as `Resend:ApiKey`. |
| `VITE_AMPLITUDE_API_KEY` | `frontend` build env | — | baked in at build time like `VITE_API_URL`; unset locally and in any build that shouldn't report (PR preview builds, if any exist). |

Both keys share the same Amplitude project — there is no separate dev project for v1 (per the "deployed only" decision), so nothing needs to distinguish environments inside Amplitude itself yet.

## Infra & CI changes

- `infra/terraform/`: add `Amplitude:ApiKey` (or `Amplitude__ApiKey`, matching the double-underscore convention already used for the worker's env vars) as a secret-backed env var on both the API and worker Container Apps.
- `.github/workflows/ci-cd.yml`: add `VITE_AMPLITUDE_API_KEY` as a build-time secret/env var for the frontend build step, alongside the existing `VITE_API_URL` wiring.
- Docs: update `backend/CLAUDE.md`'s Configuration table (new `Amplitude:ApiKey` row) and the "worker needs exactly..." sentence in its Route enrichment/Route import cross-reference paragraph; update `frontend/CLAUDE.md` with the new env var and `lib/analytics.ts`; update root `CLAUDE.md`'s Deploying bullet to mention the new keys alongside the existing ones.

## Error handling

- Backend tracker: any HTTP failure, timeout, or serialization error is caught inside `TrackEventAsync` and logged at `Warning`; the calling code (`AcceptInvite`, `RouteImportService`) proceeds exactly as if the call had succeeded. No retries — a dropped analytics event is an acceptable loss, unlike a dropped invite email.
- Frontend: the Amplitude browser SDK already fails silently/queues on network errors by design; no additional handling needed beyond the `enabled` no-op gate.

## Testing

- `Acordanza.Core.Tests`: unit tests for `AmplitudeAnalyticsTracker` against a fake `HttpMessageHandler` (request shape — `api_key`, `event_type`, `user_id` land in the right place) and for its catch-and-log behavior on a failing response. `NoOpAnalyticsTracker` needs no test beyond a trivial "does nothing, doesn't throw" check if any.
- `Acordanza.Api.Tests`: extend the existing `AcceptInvite` integration test to assert `IAnalyticsTracker.TrackEventAsync` was called once with `"Invite Accepted"` — inject a fake/mock tracker via the test's DI overrides, the same way other integration tests substitute fakes for external providers (e.g. `NoOpRouteEnrichmentProvider`).
- `Acordanza.Worker.Tests`: extend the existing route-import "route created" test case similarly — assert the event fires only on `created == true` and not on the matched/discarded/failed paths, using a fake tracker.
- Frontend: a Vitest unit test for `lib/analytics.ts`'s `enabled`-gating logic (mocking `@amplitude/analytics-browser`), and an assertion in the existing `AuthContext`/routing tests (if any exist that already render through a route change) that `identify`/`trackPageView` are called — otherwise, a small dedicated test is fine. No real network calls in any test tier; the Amplitude SDK/HTTP client is always mocked.

## Non-goals / deferred

- No custom UI-interaction events beyond page views (ticks, route opens, invites sent, area claims, ...) — can be added later as additional `TrackEventAsync`/`analytics.track()` calls once page-view data shows what's worth instrumenting next.
- No separate dev/staging Amplitude project.
- No consent UI — not applicable to a closed, invite-only app for a known group.
