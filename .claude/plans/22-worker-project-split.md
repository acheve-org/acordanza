# Split the enrichment worker into its own project and image

Status: completed
Issue: [#176](https://github.com/acheve-org/acordanza/issues/176)
Depends on: [18](18-route-enrichment-worker.md) — this reverses that plan's "an event-driven Container Apps Job reusing the API image, not a second project" decision, and nothing else about it. The queue, the message shape, the trigger endpoint, the permission, the button, the provider seam and every write path stay exactly as plan 18 built them. Backend + infra only: no schema change, no migration, no API surface change, no frontend change.

## Problem

Plan 18 shipped the route-enrichment worker as a third CLI branch (`process-enrichment`) in the API's `Program.cs`, run by a Container Apps Job against the same ACR image with different args. The stated reasons were real — one compile unit for the EF models, no second Dockerfile, no second CI artifact — but the design has produced two live defects, and both are the same defect wearing different clothes.

**The job cannot start.** `Program.cs:98-99` reads `Jwt:Key` unconditionally and throws when it's absent:

```csharp
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Missing Jwt:Key configuration");
```

That runs at line 99. The `process-enrichment` branch is at line 378. `appsettings.json` carries no `Jwt:Key` (correctly — it's a secret), and `container_app_job.tf` maps no `Jwt__Key` env var; the only mapping in the repo is `container_app.tf:135`, on the API. Every job execution throws before it reaches the worker branch. Commit a49f0ab ("fix: add missing secret to job") added `secret { name = "jwt-key" }` to the job, but a Container Apps secret is inert unless an `env { secret_name = ... }` references it, and none does — so the fix didn't take, and nothing in the build or the test suite could have told anyone.

**The worker's telemetry is mislabelled.** `container_app_job.tf` sets `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production,service.name=acordanza-enrichment-worker`, but `Program.cs:33` hardcodes `.AddService("acordanza-api")` on the shared resource builder. Verified against OpenTelemetry 1.17.0: the explicit `AddService` wins and the env var's `service.name` is silently dropped (`deployment.environment` does land, because nothing else sets it). Every worker log and trace arrives in Grafana Cloud labelled as the API — so the one signal that would have surfaced the crash above is filed under the wrong service.

Two more instances of the same shape, not yet biting: the job stores a `resend-api-key` secret it never maps and could never use (the worker sends no email), and it must be handed `Storage__DataProtectionContainerName` to satisfy a shared-prelude requirement (`Program.cs:142`) for a subsystem it has no use for.

The common cause is not carelessness. It is that **two processes with different needs share one configuration contract that exists only as an implicit reading order in a 456-line `Program.cs`**, plus two hand-synced Terraform blocks. Nothing declares what the worker requires; nothing checks it; the failure surfaces only as a crashed job execution in a service name that points somewhere else.

Patching the three symptoms in place is roughly fifteen lines and leaves the trap armed: the next `builder.Configuration["…"] ?? throw` added above line 378 breaks the worker again, silently, at deploy time. Making the *prelude* worker-aware means hoisting the CLI branch above the DI registrations — but then the branch can't resolve its services, so it becomes `if (isWorker) { …registrations… } else { …registrations… }`. That is this plan's split, performed badly, inside one file.

**Plan 18's objection does not apply to what's proposed here.** It rejected "a dedicated worker image, trimmed to just the worker's dependencies," citing "a live drift risk between the API's and worker's EF models if they ever stop sharing a compile unit." That compares one project against two *independent* ones. This plan extracts a third project, `Acordanza.Core`, that both reference — so the models and migrations stay in a single compile unit and drift stays structurally impossible, while each process gets its own entrypoint, DI graph, config contract, base image and service name.

What plan 18 got right and this keeps: the worker is still event-driven, still scales to zero, still drains and exits, still costs nothing idle.

## Design

### 1. Three projects

| Project | SDK | References | Ships as |
|---|---|---|---|
| `backend/Acordanza.Core` | `Microsoft.NET.Sdk` | — | (library) |
| `backend/Acordanza.Api` | `Microsoft.NET.Sdk.Web` | Core | `acordanza-api` image |
| `backend/Acordanza.Worker` | `Microsoft.NET.Sdk.Worker` | Core | `acordanza-worker` image |

`Acordanza.Api.Tests` references **both** Api and Worker (§Testing). All four go into `Acordanza.slnx` under the existing `/backend/` folder.

The Api → Worker edge does not exist and must not be added: the API enqueues, the worker drains, and they meet at the queue and at Core. That is the whole point of the shape — the compiler is now the thing that stops a controller from reaching into the enrichment pipeline, or the worker from reaching into an ASP.NET filter.

### 2. What lands in Core, and the rule that decides it

**Core holds exactly what both processes use.** Not "what looks reusable", not "what is pure" — what both actually consume. Applied literally, that gives:

- `Models/` — all 17 files. `ApplicationUser : IdentityUser` means Core takes a `Microsoft.AspNetCore.Identity.EntityFrameworkCore` package reference; that's a plain NuGet package (`IdentityUser` lives in `Microsoft.Extensions.Identity.Stores`), not the shared framework, so it does not drag ASP.NET Core into a class library.
- `Data/AppDbContext.cs` and `Migrations/` — the single compile unit that makes drift impossible.
- `Services/Slugify.cs`, `LinkRules.cs`, `AreaHierarchyService.cs`, `BlobStorageService.cs`, `MediaUploadService.cs`.
- **`EnrichmentMessage`**, extracted out of `RouteEnrichmentQueue.cs` into `Services/EnrichmentMessage.cs`. This record is the wire contract between the two processes — the API serializes it, the worker deserializes it — and it is the one thing in this plan that genuinely must not drift. Giving it its own file in Core says so.

And, deliberately, what does **not**:

- **All of `Authorization/`** stays in the API. `Permissions.cs`/`Roles.cs`/`RolePermissions.cs` are pure constants and would sit happily in a class library, but the worker performs no authorization — it holds no `ClaimsPrincipal` and answers to nobody. Moving them on "purity" grounds would be the first crack in the rule, and `Acordanza.Core` becoming a junk drawer is the main long-run risk of this shape. `DynamicPermissions`, `AreaExpertAuthorizer` and `RoleSeeder` stay for the same reason plus the obvious one (they need Identity or `ClaimsPrincipal`).
- **`Dtos/`, `Controllers/`, `JwtTokenService`, `InviteTokenProvider`, the email stack, `Notifications`, `ReviewRequestService`** — API only, unambiguously.
- **`RouteEnrichmentQueue`** — the *enqueue* side, used by `POST /routes/{id}/enrich` and the `enqueue-enrichment` CLI branch. The API owns enqueueing; the worker never enqueues. Only the message record it used to house moves to Core.
- **`EnrichmentQueueProcessor`, `RouteEnrichmentService`, `IRouteEnrichmentProvider` + the four provider implementations** — worker only. The API stops shipping them, and with them **drops the `Anthropic.Foundry` package reference entirely**: the public-facing image no longer carries the Foundry SDK, and Trivy's API-image surface shrinks. `Azure.Storage.Queues` stays on the API (enqueue) and is taken independently by the Worker (drain).

One consequence worth naming: `Acordanza.Core` is a plain `Microsoft.NET.Sdk` library, so it loses the Web SDK's implicit usings. Files moving into it need explicit `using Microsoft.Extensions.Configuration;` / `Logging;` / `DependencyInjection;` added — `BlobStorageService`'s `IConfiguration` constructor parameter is the clearest case, since that file currently has no `Microsoft.Extensions.*` using at all. Mechanical, but it will be a compile error per file, not a silent one.

### 3. `MediaUploadService` loses its `IFormFile` dependency

`MediaUploadService.UploadNumberedAsync(prefix, extension, usedBlobNames, IFormFile file)` and `ValidateImage(IFormFile?)` are the only reason Core would need `Microsoft.AspNetCore.App`, and taking that `FrameworkReference` would force the worker image back onto the `aspnet` base — losing most of what §7 buys. So:

- `UploadNumberedAsync` takes `(string blobPrefix, string extension, IReadOnlyList<string> usedBlobNames, Stream content, string contentType)`. Its body already only calls `file.OpenReadStream()` and `file.ContentType`.
- `ValidateImage(IFormFile?)` moves to the API (`Services/ImageValidation.cs`) — its two callers are both controllers, and it validates an *upload*, which is an HTTP concept. `ImageValidationError`, `AllowedImageContentTypes` and `MaxImageBytes` stay in Core, since `RouteEnrichmentService` checks the content-type allowlist on its download path.

This is a fix, not a tax. `RouteEnrichmentService.cs:148` currently fabricates a `FormFile` around a `MemoryStream` for no reason other than to satisfy a signature shaped by the other caller:

```csharp
var file = new FormFile(uploadStream, 0, bytes.Length, "topo", $"topo{extension}") { Headers = new HeaderDictionary(), ContentType = contentType };
```

That line and its `Microsoft.AspNetCore.Http` using both disappear. The two controller call sites (`RoutesController.UploadTopo`, `AreasController.UploadAreaMedia`) each gain `file.OpenReadStream(), file.ContentType` at the call.

With this done, **nothing in Core or Worker references ASP.NET Core**, which also sidesteps the transitive-package hazard documented in `Acordanza.Api.Tests.csproj` (ASP.NET assemblies resolving as NuGet packages pull in a vulnerable `System.Security.Cryptography.Xml`): the worker never pulls them in at all.

### 4. The worker's `Program.cs`

`Host.CreateApplicationBuilder(args)`, register, resolve, drain, exit. **No args, no CLI branch** — `process-enrichment` was a subcommand only because it shared a binary with the API; now the binary *is* the worker, so `args = ["process-enrichment"]` comes out of the Terraform too.

Registrations, all of which the worker actually uses, and none of which it doesn't:

```
AddDbContext<AppDbContext>(UseNpgsql)      AddSingleton<BlobStorageService>
AddSingleton<MediaUploadService>           AddScoped<AreaHierarchyService>
AddHttpClient()                            QueueClient (dual-credential, §8)
AddScoped<RouteEnrichmentService>          AddScoped<EnrichmentQueueProcessor>
IRouteEnrichmentProvider / ITopoVerificationProvider  (real or no-op, config-gated)
OpenTelemetry logging + tracing + metrics
```

Gone, relative to what the shared prelude registered: Identity and its token providers, JWT bearer auth, the per-permission authorization policies, Fido2, CORS, three rate limiters, forwarded headers, HTTPS redirection, controllers, OpenAPI, Data Protection blob persistence, the email stack, `RoleSeeder`. The worker consequently needs **no `Jwt:Key`, no `Resend:*`, no `Cors:*`, no `Fido2:*`, no `Storage:DataProtectionContainerName`** — which is §Problem's first defect made impossible rather than fixed.

The registration block lives in `Services/ServiceCollectionExtensions.cs` as `AddEnrichmentWorker(this IServiceCollection, IConfiguration)`, called by `Program.cs` and by the integration tests (§Testing) — one definition of "what the worker is", two callers.

Two details carried over deliberately:

- **OpenTelemetry resource: `.AddService("acordanza-enrichment-worker")`**, set in code, because that is what actually wins (§Problem). `OTEL_RESOURCE_ATTRIBUTES` in Terraform drops its `service.name` entry and keeps only `deployment.environment=production`, so there is exactly one place the name is written. Instrumentation is `AddHttpClientInstrumentation` + `AddRuntimeInstrumentation` + `AddNpgsql`/`AddNpgsqlInstrumentation`; **not** `AddAspNetCoreInstrumentation`, whose package the worker doesn't reference.
- **`await db.Database.MigrateAsync()` stays** at the top of the run, as it is today. Purity argues the API should own schema, but EF 10's `MigrateAsync` takes a migration lock, so the two processes racing is handled; keeping it preserves today's self-healing behaviour and matches the `create-user` branch. Dropping it is a one-line change if that preference ever flips.

The API's `Program.cs` loses the `process-enrichment` branch and the provider/processor/service registrations, and keeps `create-user` and `enqueue-enrichment` — the latter because it is the *enqueue* side, which the API owns.

### 5. Namespaces

`Acordanza.Api.Models` → `Acordanza.Core.Models`, `Acordanza.Api.Data` → `Acordanza.Core.Data`, and `Acordanza.Api.Services` splits three ways by destination. The alternative — keeping `Acordanza.Api.*` namespaces inside a `Acordanza.Core` assembly — costs nothing mechanically but leaves every file lying about where it lives, which is the opposite of what this plan is for.

The churn is real and should be expected in review: nearly every controller, DTO file and test gains a `using Acordanza.Core.Models;` (and often `.Data`/`.Services`). It is a compile-error-driven, one-pass edit with no behavioural content — worth landing as its own commit inside the PR so the substantive diff is readable next to it.

### 6. Migrations

`AppDbContext` and `Migrations/` move together, so the default migrations assembly follows the context to Core and no `MigrationsAssembly(...)` call is needed. `Microsoft.EntityFrameworkCore.Design` stays on the API (the startup project, which is where the tooling requires it). The commands in `backend/CLAUDE.md` gain project flags, run from `backend/Acordanza.Api/`:

```
dotnet ef migrations add <Name> --project ../Acordanza.Core --startup-project .
dotnet ef database update --project ../Acordanza.Core --startup-project .
```

### 7. Docker: two images, one context

Both Dockerfiles need Core in scope, so the build context moves up to `backend/` for both, and each copies the two csproj files, restores, then copies the two project directories. The API's final stage is unchanged (`aspnet:10.0-azurelinux3.0-distroless-extra`). The worker's is **`mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0-distroless-extra`** — runtime rather than aspnet, which is what §3 bought: no ASP.NET Core shared framework in an image whose job is to read a queue and call an HTTP API. `-extra` for the same ICU/tzdata reason the API states.

`backend/Acordanza.Api/.dockerignore` moves to `backend/.dockerignore` and gains `Acordanza.Api.Tests/` (the wider context would otherwise ship the test project into both build stages).

`Dockerfile.dev` and `docker-compose.override.yml` move with it: context `./backend`, `--project Acordanza.Api/Acordanza.Api.csproj`, and the bind mount becomes `./backend:/src` with the `bin`/`obj` named volumes repointed at `/src/Acordanza.Api/{bin,obj}` (plus Core's, or the watcher rebuilds it into the bind mount on every start). **The worker gets no compose service** — it is drain-and-exit, run on demand with `dotnet run` from `backend/Acordanza.Worker/`, exactly as `process-enrichment` is today.

`.trivyignore`'s `AVD-DS-0026` justification says "either Dockerfile" and needs to say three; the worker's distroless base has no shell either, so the reasoning carries unchanged.

### 8. Terraform: the job's config contract, honestly stated

`container_app_job.tf` is where §Problem's defects live, and most of the change is deletion:

- **Delete** the `jwt-key` secret and the `resend-api-key` secret (a49f0ab's inert additions), the `Storage__DataProtectionContainerName` env var, and `args = ["process-enrichment"]`.
- **Change** `image` to the new `acordanza-worker` repository (`var.worker_bootstrap_image` still covers the first apply before CI pushes a real tag), and strip `service.name` out of `OTEL_RESOURCE_ATTRIBUTES` (§4).
- **Keep** unchanged: the user-assigned identity, the registry block, `db-connection-string`, `grafana-otlp-headers`, the `queue-scaler-connection-string` secret and its documented provider-limitation comment, the whole `event_trigger_config` block, `replica_timeout_in_seconds`, and the `Storage__AccountName` / `Storage__ContainerName` / `Storage__EnrichmentQueueName` / `Storage__ManagedIdentityClientId` / `Anthropic__FoundryResource` / `OTEL_*` env vars.

What remains is precisely what the worker's `Program.cs` reads — and now, when someone adds a required config key to the API, the worker keeps running, because they are different programs.

No change to `storage.tf`, `ai_foundry.tf`, `container_app.tf`, or any role assignment: the job keeps the same managed identity with the same Blob/Queue Data Contributor and Cognitive Services User grants.

### 9. CI

`.github/workflows/ci-cd.yml`, all inside the existing `build-and-deploy-backend` job:

- New top-level env `ACR_IMAGE_NAME_WORKER: acordanza-worker`.
- Two build/push steps instead of one, both with context `backend`:
  `docker build -t "$IMAGE" -f backend/Acordanza.Api/Dockerfile backend` and the worker equivalent, both tagged `${{ github.sha }}`.
- The existing `az containerapp job update --image` step switches to the worker repository. It stays in the same job, immediately after the API's `az containerapp update`, so a single workflow run still rolls both from one commit — the drift risk moves from the compile unit (where §1 eliminates it) to a deploy step that already exists and already has this exact discipline.

`build-test-backend` needs no change (`dotnet build`/`dotnet test Acordanza.slnx` pick up the new projects from the solution file). `security-scan` picks up the third Dockerfile automatically. `openapi-contract-check` needs no change — and **should produce no diff**, which is the cheapest possible regression check that this refactor changed no behaviour (§Testing).

### 10. Not doing

- **Touching anything plan 18 decided.** Queue vs Service Bus, manual trigger vs enqueue-on-create, the `route.enrich` permission, direct-write vs review UI, the two Claude calls, the cost caps, the Foundry/Entra ID auth path: all unchanged. This plan moves code between assemblies and rewrites one Terraform block. If it changes behaviour anywhere, it has a bug.
- **A second worker, or generalizing the job.** The shape now supports one, and one is what exists.
- **Extracting `Authorization/` or the DTOs into Core** (§2's rule).
- **Splitting the test project.** One test assembly referencing both remains the right size for this suite (§Testing).
- **A `Directory.Build.props`** to share `TargetFramework`/`Nullable`/`ImplicitUsings` across four projects. Tempting at four, and worth doing the first time a fifth appears or the three settings actually disagree; right now it would add a file to save six lines.
- **Fixing the `Jwt__Key` gap separately first.** It could ship as a one-line Terraform patch today, but this plan deletes the env var rather than adding it, so a pre-patch would just be reverted. If this PR is going to sit, land the one-liner first and drop it here.

## Files touched

| File | Change |
|---|---|
| `Acordanza.slnx` | + `Acordanza.Core`, + `Acordanza.Worker` |
| `backend/Acordanza.Core/Acordanza.Core.csproj` | new — Identity.EFCore, Npgsql.EFCore, Azure.Storage.Blobs, Azure.Identity, Extensions.Configuration/Logging abstractions |
| `backend/Acordanza.Core/{Models,Data,Migrations}/*` | moved wholesale from the API (§2), namespaces rewritten (§5) |
| `backend/Acordanza.Core/Services/{Slugify,LinkRules,AreaHierarchyService,BlobStorageService,MediaUploadService}.cs` | moved; explicit `Microsoft.Extensions.*` usings added (§2) |
| `backend/Acordanza.Core/Services/EnrichmentMessage.cs` | new — extracted from `RouteEnrichmentQueue.cs`, the API↔worker wire contract (§2) |
| `backend/Acordanza.Core/Services/MediaUploadService.cs` | `UploadNumberedAsync` takes `Stream`+`contentType`; `ValidateImage` moves out (§3) |
| `backend/Acordanza.Api/Acordanza.Api.csproj` | + ProjectReference Core; **− `Anthropic.Foundry`**; EF Core/Blobs/Identity packages drop to Core where no longer directly used |
| `backend/Acordanza.Api/Program.cs` | − `process-enrichment` branch, − provider/processor/`RouteEnrichmentService` registrations; keeps `create-user`, `enqueue-enrichment`, `QueueClient`, `RouteEnrichmentQueue` (§4) |
| `backend/Acordanza.Api/Services/ImageValidation.cs` | new — `ValidateImage(IFormFile?)`, relocated (§3) |
| `backend/Acordanza.Api/Services/RouteEnrichmentQueue.cs` | keeps `EnqueueAsync`; `EnrichmentMessage` moves to Core |
| `backend/Acordanza.Api/Controllers/{RoutesController,AreasController}.cs` | `UploadNumberedAsync` call sites pass `file.OpenReadStream(), file.ContentType`; `ValidateImage` import (§3) |
| `backend/Acordanza.Api/**` | `using Acordanza.Core.*` across controllers/DTOs/authorization (§5) |
| `backend/Acordanza.Worker/Acordanza.Worker.csproj` | new — ProjectReference Core; Azure.Storage.Queues, Azure.Identity, Anthropic.Foundry, Extensions.Http, OpenTelemetry (no `Instrumentation.AspNetCore`) |
| `backend/Acordanza.Worker/Program.cs` | new — host, migrate, drain, exit; no args (§4) |
| `backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs` | new — `AddEnrichmentWorker`, shared with the tests (§4) |
| `backend/Acordanza.Worker/Services/{EnrichmentQueueProcessor,RouteEnrichmentService,IRouteEnrichmentProvider}.cs` | moved from the API; `FormFile` hack deleted (§3) |
| `backend/Acordanza.Worker/{appsettings.json,appsettings.Development.json}` | new — only `ConnectionStrings:Default`, `Storage:*`, `Anthropic:FoundryResource`, `Logging` |
| `backend/Acordanza.Worker/Dockerfile` | new — `dotnet/runtime:10.0-azurelinux3.0-distroless-extra`, context `backend/` (§7) |
| `backend/Acordanza.Api/Dockerfile`, `Dockerfile.dev` | context `backend/`, two-csproj restore (§7) |
| `backend/.dockerignore` | moved up from `Acordanza.Api/`, + `Acordanza.Api.Tests/` (§7) |
| `docker-compose.override.yml` | build context `./backend`, `--project` path, bind mount + bin/obj volume paths (§7) |
| `.trivyignore` | `AVD-DS-0026` justification: three Dockerfiles, not two |
| `infra/terraform/container_app_job.tf` | − 2 secrets, − 1 env var, − `args`, worker image, `OTEL_RESOURCE_ATTRIBUTES` without `service.name` (§8) |
| `.github/workflows/ci-cd.yml` | + `ACR_IMAGE_NAME_WORKER`, second build/push, job deploy uses the worker image (§9) |
| `backend/Acordanza.Api.Tests/Acordanza.Api.Tests.csproj` | + ProjectReference Worker |
| `backend/Acordanza.Api.Tests/**` | namespace usings; `RouteEnrichmentTests` builds a worker service provider (§Testing) |
| `backend/CLAUDE.md` | project layout, `dotnet ef` project flags (§6), the worker's own commands, Services lists per project, Route enrichment section |
| `CLAUDE.md`, `README.md`, `infra/README.md` | the two-image deployment, the worker project's run instructions, the job's config set |

## Steps

Ordered so the tree compiles at the end of each step.

1. **Create `Acordanza.Core`** and move `Models/`, `Data/`, `Migrations/` into it; rewrite namespaces; fix up the API's usings until `dotnet build` is green. Nothing else moves yet.
2. **Move the five shared services** into Core, adding the explicit `Microsoft.Extensions.*` usings the plain SDK requires. Still green.
3. **Refactor `MediaUploadService`** (§3): `Stream`+`contentType`, `ValidateImage` out to the API, two controller call sites updated. Green, and `dotnet test --filter "Category!=Integration"` still passes.
4. **Extract `EnrichmentMessage`** to Core.
5. **Create `Acordanza.Worker`**, move the three enrichment services into it, delete the `FormFile` hack, write `AddEnrichmentWorker` and `Program.cs`, and strip the `process-enrichment` branch, the enrichment registrations and the `Anthropic.Foundry` reference out of the API.
6. **Fix the tests** (§Testing) and get `dotnet test Acordanza.slnx` green with Docker up.
7. **Docker** (§7): both Dockerfiles, `.dockerignore`, `Dockerfile.dev`, `docker-compose.override.yml`. Verify `docker compose up -d --build` still hot-reloads the API, and that both images build locally.
8. **Terraform** (§8) — `terraform validate` and `terraform plan` under `infra/terraform/`; the plan should show the job's secrets/env/args changing and nothing else moving.
9. **CI** (§9) and `.trivyignore`.
10. **Docs** — the four files above.
11. **Verify the contract didn't move**: `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` then `bun run generate-api` in `frontend/` must produce **zero diff**. Any diff means step 1–5 changed a DTO by accident.

## Testing

The refactor's real test is that the existing suite passes unchanged in substance. Concretely:

**Unchanged and must stay so** — every test in `Acordanza.Api.Tests` other than the two noted below, modulo `using` lines. `AuthorizationMatrixTests`, `ValidationVisibilityTests`, `PrivacyInvariantsTests` and the rest exercise the API through real HTTP and know nothing about project boundaries; if any of them needs a behavioural edit, something in §2–§5 went wrong.

**`RouteEnrichmentTests`** is the one file with genuine work. Today it constructs `RouteEnrichmentService` by hand in `BuildService` (pulling `MediaUploadService`/`BlobStorageService`/`IHttpClientFactory` off `Factory.Services`) and resolves `EnrichmentQueueProcessor` from the API host at line 336. After the split the API host registers neither. Replace both with a worker-side provider built from the same env vars `ApiFixture` already sets:

```
new ServiceCollection() → AddEnrichmentWorker(config) → replace IRouteEnrichmentProvider/ITopoVerificationProvider with the fakes
```

`AddEnrichmentWorker` existing for exactly this reason is why §4 puts it in its own file. Care is needed on one point `ApiFixture` already documents: process env vars are global, so this second provider must read the *same* configuration the fixture set, never its own — it is a second service provider over one config, not a second configuration.

The assertions themselves do not change: the 202/404/409/503 endpoint tests still run against the API host (`RouteEnrichmentQueue` and `QueueClient` stay there), and the processing/poison tests still run against real Postgres + Azurite with a faked provider.

**One new test**, in `Acordanza.Api.Tests/Infrastructure/` — a plain unit test asserting that a `HostApplicationBuilder` configured with **only** the worker's Terraform env set (`ConnectionStrings__Default`, `Storage__{AccountName,ContainerName,EnrichmentQueueName,ManagedIdentityClientId}`, `Anthropic__FoundryResource`) can call `AddEnrichmentWorker` and resolve `EnrichmentQueueProcessor` without throwing. That is §Problem's first defect, turned into something CI can fail on: it fails today (`Missing Jwt:Key configuration`), and it would fail again the moment someone reintroduces a required key the job isn't given. Notably it needs no Docker — resolving the graph doesn't open a connection — so it belongs in the fast tier.

**Manual, before merge**, since neither Terraform nor the image is CI-testable end to end:

- `docker build` both images locally and check the worker's size against the API's (the §7 claim), and that `docker run` on it with no env fails on `ConnectionStrings:Default` rather than on `Jwt:Key`.
- The full plan-18 smoke, unchanged, now against the worker project: `docker compose up -d`, `az login` under an identity holding Cognitive Services User on the Foundry resource, `dotnet run` from `backend/Acordanza.Worker/`, with `dotnet run -- enqueue-enrichment <id>` from `backend/Acordanza.Api/` on the other side.
- After deploy: trigger one enrichment from the button and confirm the job execution **succeeds** (it currently cannot) and that its logs arrive in Grafana Cloud under `service.name = acordanza-enrichment-worker`. Those two observations are the whole point of the change; record both in the PR.

`dotnet test Acordanza.slnx` from the repo root with Docker up, `terraform validate`/`plan` under `infra/terraform/`, and the zero-diff OpenAPI check from step 11.
