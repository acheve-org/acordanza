# Route enrichment worker: web-searched links and a topo candidate, found off the request path

Status: planned (not started)
Issue: not filed yet — file one and link it here before the `plan:` commit
Depends on: [16](16-route-new-fields.md) (the `Link` entity and its validation rules — enrichment results land as route links). Backend + infra only: a Storage Queue on the existing storage account, an Azure AI Foundry resource hosting a Claude deployment (Entra ID auth via the job's managed identity — no stored API key, §6), a `process-enrichment` CLI branch in `Program.cs`, an event-driven Container Apps Job running the existing image, and one Claude API call per new route — gated to admin-authored routes in v1, a stand-in for a real feature flag (decision below). **Zero frontend changes** — found links surface through plan 16's `Links` collection, which the edit form already renders and the route-display revamp will render publicly.

## Problem

When someone adds a route, the app knows only what they typed. The web usually knows more: a blog post about the first ascent, a video, a page on the sector — and often a published topo image. Finding those is exactly the kind of work that should not happen inside `POST /routes` (it takes seconds-to-minutes, calls external services, and can fail without the route creation caring), so it belongs in a worker process.

Several architectural decisions shape everything below:

**Azure Storage Queue, not Service Bus, and not an in-process `BackgroundService`.** The queue rides the storage account Terraform already provisions (`azurerm_storage_account.topos`) — one new resource block, no new account. Azurite emulates Storage Queues, so local dev stays `docker compose up -d` (with one compose fix — §2). Service Bus would add an Azure resource for sessions/topics/dedup this workload doesn't use. An in-process `BackgroundService` + `Channel<T>` would need no infra at all, but Container Apps scale-to-zero restarts would silently drop queued work — the same failure mode that pushed Data Protection keys into blob storage (`Program.cs`'s comment on it). The queue gives at-least-once delivery, dequeue-count-based retries, and poison handling for cents.

**An event-driven Container Apps Job reusing the API image, not a second project.** `Program.cs` already dispatches CLI subcommands (`create-user`); the worker is one more branch, `process-enrichment`, sharing `AppDbContext`, the EF models, and every service registration. The Container Apps Job runs the same ACR image with different args, scales from zero on queue depth via KEDA, and exits when the queue is drained — for a friends-group app where routes arrive occasionally, that's a worker that costs nothing while idle. No second Dockerfile, no second CI artifact, no drift between API and worker models.

Considered and rejected: a dedicated worker image, trimmed to just the worker's dependencies (EF Core, `Azure.Storage.Queues`, the Foundry SDK — no ASP.NET Core hosting, controllers, Swagger, JWT/CORS/WebAuthn middleware). It would shrink the image Trivy scans and speed up KEDA's per-execution cold pull (Container Apps Jobs pull fresh per execution, unlike a long-lived API revision). At this app's volume — occasional route creation, not a hot path — that's outweighed by a second Dockerfile, a second CI build/push, a second Trivy target, and a live drift risk between the API's and worker's EF models if they ever stop sharing a compile unit — exactly the cost this decision exists to avoid. Revisit only if the API image grows large enough that job cold-start becomes user-visible.

**One Claude API call does the search-and-extract, instead of a search API plus scraping.** Bing's Search APIs were retired in 2025; raw alternatives (Brave, Google CSE) return URLs and leave "is this page actually about *this* route in *this* sector, and is that image a topo?" to hand-rolled scraping. A single Messages API request with the server-side web search tool (`web_search_20260209` — searches, fetches, and filters server-side; no search-provider account) plus structured outputs returns exactly the shape to persist. A second, cheap vision call verifies the topo candidate before anything is imported (§5). Cost is a few cents per new route (web search is $10 per 1,000 searches plus tokens), bounded by `max_uses`.

**Reached through Azure AI Foundry, not `api.anthropic.com` directly.** Anthropic's Claude models are available in the Microsoft Foundry model catalog with two hosting types per model: *Hosted on Anthropic* and *Hosted on Azure*. As of mid-2026, structured outputs and server-side tools (including web search) are Anthropic-hosted-only — a request using either against an Azure-hosted deployment gets a `400` by design — so the Foundry deployment must be pinned to **Hosted on Anthropic**; that's the one requirement this decision imposes on provisioning (§7). In exchange, both hosting types support Entra ID token auth on the same resource, so the worker authenticates with the Container Apps Job's existing managed identity instead of a stored `Anthropic:ApiKey` secret — one less secret, and consistent with how `Storage:*` already avoids one via `DefaultAzureCredential` (§1). Per-token pricing is identical to calling Anthropic directly (billed as Claude Consumption Units on the Azure invoice instead of a separate Anthropic one). The trade-off, spelled out in §6: local dev now needs the developer's own `az login` session to carry the Foundry RBAC role, a heavier bar than pasting a personal API key into user-secrets.

**Results are written directly, not staged for review.** The found links are appended to `route.Links` and a verified topo is imported as a `Topo` — no suggestion/approval UI. Two reasons: it's what the feature *is* (the issue's ask), and the blast radius is small — links are visible-but-deletable in the edit form, a wrong topo is deletable like any uploaded one, and the write path never touches `ValidationStatus` (§4), so enrichment can't push a validated route back to the review queue. If experience shows the model finds junk, the escape hatch is a review-first flow — deliberately out of scope (§8).

**V1 ships gated to admin-authored routes, as a stand-in for a real feature flag.** The app has no feature-flag component today, and this is a direct-write feature (decision above) whose behavior — cost, link quality, false-topo rate — is still unproven in practice. Rather than build a flag system just for this, or ship it open to everyone on day one, the enqueue in `CreateRoute` (§1) only fires when the route's creator has the `admin` role; routes added by anyone else are created exactly as they are today, with no queue message and no `EnrichedAt`. It's a single conditional at one call site, not a new authorization primitive or policy — nothing about who can *create a route* changes, only whether that specific creation also triggers enrichment. Widening it (to all users, or behind a real flag) is follow-up work once the direct-write approach has proven itself on admins' own routes; see §8.

## Design

### 1. The queue and the enqueue point

A queue named `route-enrichment` on the existing storage account. Message body, JSON:

```json
{ "routeId": 123, "force": false }
```

`QueueClient` registration in `Program.cs` follows the exact dual-credential pattern of `BlobStorageService`/Data Protection: `Storage:ConnectionString` (Azurite) when present, else `https://{Storage:AccountName}.queue.core.windows.net/` with `DefaultAzureCredential` + `Storage:ManagedIdentityClientId`. Queue name from `Storage:EnrichmentQueueName` (default `route-enrichment` in `appsettings.json`). Use `QueueMessageEncoding.Base64` on `QueueClientOptions` — KEDA's `azure-queue` scaler and the portal's queue browser both assume Base64-encoded messages.

`RoutesController.CreateRoute` enqueues **after** `SaveChangesAsync` (the message carries the id, which doesn't exist before) and **only when `User.IsInRole(Roles.Admin)`** — the v1 admin-only gate (decision above), checked inline in the controller the same way `FirstAscensionistsController.UpdateFirstAscensionist` and `UsersController.GetInviteLimit` already do role checks beyond their `[Authorize(Policy = ...)]` attribute, using the existing `Roles.Admin` constant (`Authorization/Roles.cs`) — no new authorization primitive. The enqueue call itself is wrapped in the same best-effort posture as decision-notification emails: a try/catch-and-log, because a queue hiccup must not fail route creation. A small `RouteEnrichmentQueue` service (Services/) owns the `QueueClient` and the `EnqueueAsync(routeId, force)` call so the controller stays one line; the role check sits in the controller next to the call, not inside the service, since it's about who gets the feature, not how enqueueing works.

Ensure-queue-exists mirrors the blob container: `CreateIfNotExistsAsync` in the startup block that already calls `EnsureContainerExistsAsync` (Azurite side; Terraform owns it in production, like the containers).

A second CLI branch, `dotnet run -- enqueue-enrichment <routeId> [--force]`, enqueues manually — this is both the local smoke-test path and the "re-run enrichment for this route" affordance, without building an admin endpoint for it (§8). `--force` re-runs a route that already has `EnrichedAt` set (§4's idempotency marker). This CLI path **bypasses the admin-authored gate** (decision above) by design — it already requires server/CLI access, a higher trust bar than the role check it would otherwise duplicate, and it's useful precisely for exercising enrichment against a non-admin route while the feature is gated.

### 2. Local dev: Azurite must serve queues

`docker-compose.yml` currently runs `azurite-blob --blobHost 0.0.0.0 --blobPort 10000` — **blob only**. Change the command to full Azurite with the queue service and expose it:

```yaml
command: "azurite --blobHost 0.0.0.0 --blobPort 10000 --queueHost 0.0.0.0 --queuePort 10001 --skipApiVersionCheck"
ports:
  - "${AZURITE_BLOB_PORT:-10000}:10000"
  - "${AZURITE_QUEUE_PORT:-10001}:10001"
```

and append `QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;` to the connection string in `appsettings.Development.json`.

The integration-test fixture needs **no change** for this: `Testcontainers.Azurite`'s `AzuriteBuilder` starts all three services by default and `GetConnectionString()` already carries the queue endpoint. (The `--skipApiVersionCheck` flag stays, same reason as the compose file.)

### 3. The worker: `process-enrichment` CLI branch + Container Apps Job

A third CLI branch in `Program.cs`, shaped like `create-user` (runs before the web host pipeline): build the host, resolve services, then drain the queue and exit:

```
receive (visibility timeout 5 min, batch of up to 8)
  → for each message: process (§4–§5) → DeleteMessage on success
  → on processing failure: log, leave the message (visibility timeout redelivers it)
  → if DequeueCount > 5: log as poison, DeleteMessage (give up)
loop until ReceiveMessages returns empty → exit 0
```

The drain-and-exit shape is what makes the Container Apps Job model work: KEDA fires a job execution when the queue has messages, the execution processes everything currently queued, exits, and the environment scales back to zero. The loop lives in a `Services/EnrichmentQueueProcessor.cs` class (not inline in `Program.cs`) so integration tests can drive it directly against the fixture's Postgres + Azurite (§Testing).

The visibility timeout (5 min) must exceed the worst-case processing time of one message (two Claude calls + an image download — normally well under a minute) so a slow message isn't redelivered mid-flight to a parallel execution.

Poison handling is delete-and-log rather than a dead-letter queue: the failure mode this protects against is a malformed message or a persistently-failing route, and for this app's volume a `LogError` with the body is a sufficient audit trail. OpenTelemetry already ships worker logs to Grafana Cloud in production (the CLI branch boots the same host, so the same exporters are wired).

### 4. The enrichment call and the link write

`Services/RouteEnrichmentService.cs` does the work for one route. Claude access goes behind a one-method interface — `IRouteEnrichmentProvider` (`Task<EnrichmentResult> FindAsync(RouteContext ctx)`) — so integration tests fake the provider and never touch the network. The real implementation uses Anthropic's Foundry C# SDK (`Anthropic.Foundry` on NuGet — a distinct client type from the plain `Anthropic` package, `AnthropicFoundryClient` in place of `AnthropicClient`, but the same `Messages.Create` request/response shapes), authenticated with `AnthropicFoundryIdentityTokenCredentials(new DefaultAzureCredential(), resource)` — the same `DefaultAzureCredential` chain already in use for `Storage:*` (§1), here exercised for real in every environment including local dev, since Foundry has no Azurite-style emulator (§6). Model `claude-opus-5`, deployed on the Foundry resource as *Hosted on Anthropic* (required — decision above), with:

- the **web search tool** (`web_search_20260209`), `max_uses: 5` — the cost cap;
- **structured outputs** (`output_config.format`, json_schema) returning:

```json
{
  "links": ["https://..."],
  "topoImageUrl": "https://... or null"
}
```

The prompt carries the route's name, grade, and full area breadcrumb (via `AreaHierarchyService` — a bare route name like "La Fiesta" is useless without "…in Riglos"), asks for pages *specifically about this route or its sector* (first-ascent stories, videos, guidebook pages, topo publications), tells the model most sources will be Spanish, and instructs it to return nothing rather than tangential results. Exact SDK bindings (tool declaration, schema types) are an implementation-time concern from the SDK's own docs — the plan pins the surface, not the C# spelling.

**Writing links reuses plan 16's rules, hoisted.** Plan 16 puts trim/cap-10/max-2048/http-https-allowlist/dedupe in a `RoutesController` helper; this plan hoists that pure helper into `Services/LinkRules.cs` (static, `(IEnumerable<string>) -> (List<string> Urls, string? Error)`) and has both the controller and the worker call it — the scheme allowlist is the stored-XSS guard and must not be re-derived in two places. The worker then:

- loads the route with `.Include(r => r.Links)`;
- filters model links through `LinkRules`, drops any URL already present (case-sensitive, same as plan 16), and **appends** up to the cap of 10 total — enrichment never removes or reorders what a human entered;
- sets `route.EnrichedAt = DateTimeOffset.UtcNow` — a new nullable `timestamptz` column on `ClimbingRoute` (one migration), the idempotency marker: a redelivered or duplicate message finds `EnrichedAt` set and completes without a second API call, unless `force` is set;
- **never touches `ValidationStatus`.** The worker writes via `AppDbContext` directly, not through `UpdateRoute`, so `Validation.InvalidateIfChanged` is structurally out of the path — stated here as an invariant, and tested, because auto-appended links must not bounce an approved route back to `Pending`.

Known, accepted race: if the author opens the edit form before enrichment lands and saves after, `UpdateRoute`'s replace-the-whole-set semantics (plan 16) drop the appended links. The window is seconds wide, the loss is re-runnable (`enqueue-enrichment --force`), and merging in `UpdateRoute` would break "the client sends the full intended set". Not worth complicating the write path.

### 5. The topo candidate: verify with vision, then import

`topoImageUrl` is a *candidate*. Before it becomes a `Topo`:

1. **Download** with a dedicated `HttpClient` (10 s timeout, 10 MB cap, content-type must be an image type `MediaUploadService` accepts). Anything else → skip the topo, keep the links, log why.
2. **Verify with one vision call**: the image (base64) plus "is this a climbing topo/route-line photo plausibly matching *{name, grade, sector}*? Answer via the schema `{ "isTopo": bool }`" — structured output again. `false` → skip the import, but append the *page* the image came from as a link if the model surfaced it (a topo publication page is a good link even when auto-import isn't confident).
3. **Import** through `MediaUploadService.UploadNumberedAsync`, exactly like a user upload from `RoutesController.UploadTopo` — same blob-name scheme (`{area-slug-path}/{route-slug}-{NN}.{ext}`), same DB-save-vs-blob-orphan cleanup. `UploadedByUserId = route.AddedByUserId`: the enrichment runs on the route creator's behalf, and `Topo` has no nullable uploader — no system-user hack.

Skip the whole step when the route already has any topo (`route.Topos.Any()`) — a human upload always outranks an import, and this also makes the step idempotent independently of `EnrichedAt`.

This is the plan's highest-risk write (a scraped image, attributed to a user who didn't pick it), which is why it's gated on an explicit vision check rather than trusting the search call's own claim, and why it's skipped on any ambiguity. A wrong import is one `DELETE /topos/{id}` away from gone.

### 6. No Foundry access: degrade, don't break

Without `Anthropic:FoundryResource` configured, the enqueue still happens (the plumbing stays exercisable locally) and the worker logs `enrichment skipped: no Anthropic:FoundryResource configured` and deletes the message — the exact philosophy of `ConsoleEmailSender`. Unlike an API key, `FoundryResource` isn't sensitive (it's just a resource name, used to build `https://{resource}.services.ai.azure.com/anthropic/`), so it lives in `appsettings.json`/`appsettings.Development.json` like any other config value — nothing about Claude access is a stored secret.

Authentication is Entra ID end to end via `DefaultAzureCredential`, not a key:

- **Production**: the Container Apps Job's existing managed identity (`azurerm_user_assigned_identity.container_app`, already used for blob/queue access — §7), granted the **Cognitive Services User** role on the Foundry resource. `DefaultAzureCredential` picks this up the same way it already does for the `Storage:*` non-Azurite path (§1).
- **Local dev**: `DefaultAzureCredential` falls through to the developer's own `az login` session (`AzureCliCredential`). This is new relative to everything else in this plan — Storage/queue is Azurite-emulated locally, but Foundry has no emulator, so `dotnet run -- process-enrichment` run locally makes a real call against the real Foundry resource under the developer's own identity. **Each contributor who wants to run enrichment locally needs the Cognitive Services User (or Foundry User) role granted on the Foundry resource** — a heavier bar than the previous design's "paste your own Anthropic API key into user-secrets," and one that needs whoever administers the Azure subscription to grant it per contributor. Document this as a setup step alongside the JWT signing key in the README/backend `CLAUDE.md` (§Files touched).

Config summary (all under existing patterns): `Anthropic:FoundryResource` (not a secret — the Foundry resource name), `Storage:EnrichmentQueueName` (default `route-enrichment`).

### 7. Terraform and CI

`infra/terraform/`:

- `storage.tf`: `azurerm_storage_queue "route_enrichment"` (name `route-enrichment`) on `azurerm_storage_account.topos`; a second `azurerm_role_assignment` granting the existing `azurerm_user_assigned_identity.container_app` **Storage Queue Data Contributor** on the account (the blob one covers blobs only).
- new `ai_foundry.tf`: an Azure AI Foundry resource (Cognitive Services-family — the exact `azurerm_*` resource name, and whether the pinned provider version models a Foundry model deployment as a nested block or a separate resource, is an implementation-time lookup against `providers.tf`, same caveat as the scaler-identity question below) plus one Claude deployment on it — model `claude-opus-5`, hosting type **Hosted on Anthropic** (not Hosted on Azure — §4, decision 3: structured outputs and the web search tool 400 on Azure-hosted as of mid-2026), Global Standard. A third `azurerm_role_assignment` grants `azurerm_user_assigned_identity.container_app` **Cognitive Services User** on the Foundry resource. **First deployment needs one manual portal step**: Azure Marketplace terms acceptance for the Claude offer is a click-through, not Terraform-automatable the first time per subscription — document it as a one-time setup step in `infra/README.md`.
- new `container_app_job.tf`: an `azurerm_container_app_job` in the existing environment — same ACR image as the backend, args `["process-enrichment"]`, the same env/secret set as `azurerm_container_app.main` (it boots the same host: DB connection string, `Storage__*`, `Jwt__Key` is *not* needed but harmless to share the template) plus `Anthropic__FoundryResource` as a **plain env var, not a secret** (§6 — there's no API key to store; auth rides the job's managed identity), event-trigger config with a KEDA `azure-queue` scale rule (`queueName = "route-enrichment"`, `queueLength = 1`, `accountName = ...`), `replica_timeout` 600 s, parallelism 1. **Scaler auth**: prefer the job's managed identity on the scale rule; if the pinned azurerm provider version doesn't yet expose scale-rule identity for jobs, fall back to a queue-scoped connection-string secret *for the scaler only* (the app code itself stays on managed identity) and leave a comment saying why. Resolve which at implementation time against the provider version in `providers.tf`.
- `.github/workflows/ci-cd.yml`: the backend deploy step must also roll the job to the new image tag (same `az containerapp job update --image` / Terraform apply motion as the app — match however the workflow updates `azurerm_container_app.main` today). An image deployed to the API but not the worker is model drift waiting to bite a migration.

`security-scan` (Trivy) sees no new Dockerfile and no secrets in code (the Foundry resource name isn't sensitive); nothing to ignore.

### 8. Not doing

- **A review/suggestion UI for found links or topos.** Direct write with guardrails (§4–§5) is the design; if it proves too noisy, a `pending`-style flow is the follow-up, not a first version.
- **An admin HTTP endpoint to retrigger enrichment.** `dotnet run -- enqueue-enrichment <id> --force` covers it; an endpoint + button is UI surface this doesn't need yet.
- **Enrichment on route *edit* or rename.** Only `CreateRoute` enqueues. A renamed route can be re-enriched manually with `--force`. Auto-retrigger-on-rename is a one-line follow-up once wanted.
- **Other enrichment sources or message types.** The queue/job/CLI-branch shape generalizes (any future "do X after Y" is another message type and another branch), but this plan ships exactly one.
- **Link titles or per-link provenance ("found automatically").** `Link` is `Url`-only by plan 16; widening it is that plan's follow-up territory.
- **A Service Bus migration path.** If volume ever demands it, the `RouteEnrichmentQueue`/`EnrichmentQueueProcessor` seams are where it would slot in.
- **A real feature-flag system.** The admin-only gate (decision above) is deliberately the cheapest thing that works — one role check at one call site — not a general flag mechanism. Widening enrichment past admins (per-user rollout, a kill switch, percentage rollout) is exactly the kind of need that would justify building one later; this plan doesn't.

## Files touched

| File | Change |
|---|---|
| `docker-compose.yml` | Azurite full mode + queue port 10001 (§2) |
| `backend/Acordanza.Api/appsettings.json` | `Storage:EnrichmentQueueName` |
| `backend/Acordanza.Api/appsettings.Development.json` | `QueueEndpoint` appended to the connection string |
| `backend/Acordanza.Api/Acordanza.Api.csproj` | + `Azure.Storage.Queues`, + `Anthropic.Foundry` (`Azure.Identity` already present, per `BlobStorageService`) |
| `backend/Acordanza.Api/Program.cs` | `QueueClient` registration (dual-credential), queue ensure-exists, `process-enrichment` + `enqueue-enrichment` CLI branches |
| `backend/Acordanza.Api/Models/ClimbingRoute.cs` | `EnrichedAt` (`DateTimeOffset?`) |
| `backend/Acordanza.Api/Migrations/*` | one migration: add `EnrichedAt` |
| `backend/Acordanza.Api/Controllers/RoutesController.cs` | best-effort enqueue in `CreateRoute`, gated to `User.IsInRole(Roles.Admin)` (§1); link helper calls moved to `LinkRules` |
| `backend/Acordanza.Api/Services/LinkRules.cs` | new — plan 16's link validation, hoisted (§4) |
| `backend/Acordanza.Api/Services/RouteEnrichmentQueue.cs` | new — enqueue side (§1) |
| `backend/Acordanza.Api/Services/EnrichmentQueueProcessor.cs` | new — drain loop, retry/poison (§3) |
| `backend/Acordanza.Api/Services/RouteEnrichmentService.cs` | new — per-route flow: provider call, link write, topo verify/import (§4–§5) |
| `backend/Acordanza.Api/Services/IRouteEnrichmentProvider.cs` (+ Claude impl) | new — the test seam (§4) |
| `infra/terraform/storage.tf` | queue + queue-role assignment (§7) |
| `infra/terraform/ai_foundry.tf` | new — Foundry resource + Claude deployment + role assignment (§7) |
| `infra/terraform/container_app_job.tf` | new — the event-driven job (§7) |
| `.github/workflows/ci-cd.yml` | deploy step also rolls the job image (§7) |
| `backend/Acordanza.Api.Tests/Services/LinkRulesTests.cs` | new (unit — moves with the hoist) |
| `backend/Acordanza.Api.Tests/Controllers/RouteEnrichmentTests.cs` | new (integration — §Testing) |
| `backend/CLAUDE.md` | CLI commands (+2), Services list, Storage section (queue), config keys, local-dev Foundry RBAC prerequisite (§6) |
| `CLAUDE.md`, `README.md`, `infra/README.md` | docker-compose note (queue), deploy config (`Anthropic:FoundryResource`), job resource, Foundry Marketplace one-time setup step (§7), local `az login` + Foundry role prerequisite (§6) |

No DTO changes → no OpenAPI regeneration, no `generated.ts` diff, no frontend edits.

## Testing

**Unit** — `LinkRulesTests` carries plan 16's helper tests to the hoisted class (scheme allowlist, cap, dedupe — same assertions, new home). If plan 16 already landed them controller-side, they move, not duplicate.

**Integration** — `RouteEnrichmentTests.cs`, `IntegrationTestBase` + `[Trait("Category", "Integration")]`. The fixture's Azurite already serves queues (§2), so these run against a real queue and real Postgres, with `IRouteEnrichmentProvider` replaced by a fake (registered via the fixture's env-var seam is wrong for a service — override via a test-only `WebApplicationFactory` service replacement is unavailable here, so make the provider resolution config-gated: no `Anthropic:FoundryResource` → a no-op provider; tests construct `RouteEnrichmentService` directly with the fake and the scoped `AppDbContext`, and drive `EnrichmentQueueProcessor` with it):

- `POST /routes` as an admin leaves exactly one message on the queue carrying the new route's id; the same call as a non-admin (`user`/`moderator`) leaves the queue untouched (the admin-only gate, decision above); a failed enqueue for an admin-created route (point the queue service at a bad endpoint) still returns 201.
- Processing a message appends the provider's links, filtered: a `javascript:` URL from the fake is dropped (the allowlist holds on the worker path too), duplicates against existing links are dropped, the 10-link cap holds, ordering of pre-existing links is untouched.
- `EnrichedAt` is set; reprocessing the same message is a no-op (no second provider call — assert on the fake's call count); `force: true` re-runs.
- An **approved** route stays approved after enrichment (`ValidationStatus` unchanged — the §4 invariant, asserted explicitly).
- Topo path with a stubbed `HttpMessageHandler`: a verified candidate becomes a `Topo` blob + row attributed to the route's creator; `isTopo: false` imports nothing; a route with an existing topo skips the step; an over-size/wrong-content-type download skips cleanly.
- Poison: a message whose processing throws is redelivered (dequeue count grows) and deleted after the fifth attempt, with the queue left empty.

**Not in CI**: any real Claude call (cost, auth, nondeterminism). The real-provider smoke is manual and goes in the PR description: `docker compose up -d`, create a real route (or `enqueue-enrichment <id>`), `az login` under an identity holding the Cognitive Services User role on the Foundry resource, `dotnet run -- process-enrichment` with `Anthropic:FoundryResource` set, then eyeball the route's links and topo in the app via the `run-acordanza` skill.

`dotnet test Acordanza.slnx` from the repo root (Docker up); `terraform validate`/`plan` under `infra/terraform/` for the new resources (queue, Foundry resource + deployment, job) before the PR.
