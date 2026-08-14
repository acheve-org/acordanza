# Create a route from a topo: upload a photo, read it with vision, create or enrich the route

Status: Done — §1–§7 implemented in [#209](https://github.com/acheve-org/acordanza/pull/209), **built from this plan as originally written, i.e. before [31](31-route-from-url.md) amended it.** That amendment landed on `main` while this was being built, and it is not retrofitted here: plan 31 generalises this plan's `TopoImport` into a two-kind `RouteImport` (Image | Url), renames the `topo-import` queue and `TopoImportsController` to `route-import`/`RouteImportsController`, renames `UploadedBy` to `SubmittedBy`, and puts §3's gate-and-extract behind an `IImportReader` seam shared with the URL kind. **Plan 31's implementation therefore also carries that rename** — a second migration over a table with no production rows yet, plus the queue, controller, DTO, API-client and page renames — rather than finding the generalised shape already in place. §4 (match-or-create), §5 (few-shot, golden set) and §8 (not doing) were built as written and stand unchanged under the amendment.

§3's open question is answered and the answer is no: `ValidateLength`'s floor is `> 5`, so the column default of 0 does *not* pass it, and a reviewer editing a machine-created route whose topo stated no length has to supply one before saving. Left as the plan specified (the topo didn't say, so a human must) rather than widening `Length` to nullable, which would ripple through the DTOs and the frontend for a case a reviewer already has to handle.

Four things this plan asks for that only a human can do: verify the `claude-sonnet-5` Foundry deployment's Anthropic-hosted version id via the `az rest` catalog query before the first `terraform apply` (pinned to `"1"` following opus's same-generation value, but the id is per-model — see the comment in `ai_foundry.tf`), curate the `_fewshot/gate/{topo,not-topo}/` blobs (§5), run the manual smoke against real Foundry that validates §3's resolution argument and the $0.05 estimate, and fill `backend/Acordanza.Worker.Tests/Services/TopoImportGoldenSet.json` and run it (§5). `terraform validate` passes; `terraform plan` needs Azure credentials.
Issue: [#205](https://github.com/acheve-org/acordanza/issues/205)
Depends on: [18](18-route-enrichment-worker.md) (the queue, the Container Apps Job, the Foundry resource and its managed-identity auth — this plan adds a second queue and a third deployment to all of it); [22](22-worker-project-split.md) for `Acordanza.Worker`, which every backend change lands in; [29](29-enrichment-quality.md) for the evidence/confidence schema pattern and the golden-set harness, both reused wholesale. One migration (`TopoImport`, §1). One new Foundry deployment (`claude-sonnet-5`, §3). Two new `BlobStorageService` methods (§2).

## Problem

Adding a route means typing a name, a grade, a length, a sector, and the gear notes into a form. But a climber standing under the route usually already has the answer in their hand: a photo of the guidebook page, or of the topo panel at the parking. The information is *there* — it's just in a JPEG instead of a form.

So: upload the topo, and let a vision model fill the form. If it can read enough, the route gets created with the topo already attached. If it can't, nothing is created and the uploader is told why.

This is the enrichment worker's sibling — same queue-and-job shape, same Foundry resource, same "don't write anything the model didn't demonstrably see" discipline — but pointed at an image the user supplied rather than at the open web. Three things make it a materially easier problem than plan 18's, and they're worth stating up front because they're what makes the design smaller:

**The evidence is in the request.** Enrichment's hardest problem was grounding: proving a URL the model returned corresponds to a page it actually read (plan 29 §1). Here the model is handed exactly one image and asked what it says. There is no retrieval step to ground against, and no way to hallucinate a source. The analogue of grounding is a quoted `evidence` string — what the model claims it read off the image — which a human can check against the same image in one glance.

**Routes have a review gate already.** Enrichment appends links, and links have no review concept, which is why plans 18 and 29 kept deferring a suggestion/approval flow and settled for a provenance column. Routes have `ValidationStatus`. A machine-created route landing as `Pending` is visible only to its author and to reviewers (issue #72, plan 11), and shows up in `/manage/pending-tasks` with no new machinery at all. The review flow this feature needs already exists and is already staffed.

**It is roughly 20× cheaper.** No web search, no fetch loop, no thinking-heavy judgment call — two vision calls against one image. About **$0.05 per import** against enrichment's $0.60–1.50 per run (§3 prices it out). That changes who gets to press the button: `route.enrich` is admin-only because plan 18's spend was unbounded per run, and that argument does not carry here.

Two decisions shape everything below.

**Nothing is silently discarded.** A run that can't read the topo still writes a row the uploader can see, with a reason in Spanish and the option to try again. Enrichment gets away with a null result because the route still exists and an admin pressed the button while looking at it; here the upload *is* the entire artifact, and a user who waits two minutes for silence has been given a broken feature. This is what makes `TopoImport` (§1) the keystone of the design rather than an accounting nicety: one table buys user-visible state, a retry, a stated reason, the orphan-blob cleanup story, and — §5 — a labelled dataset that accumulates for free.

**The sector comes from the form, not from the model.** `ClimbingRoute.AreaId` is a required foreign key into a tree, and inferring it from text on a photo means fuzzy-matching "Sector Los Bloques" against the area table — the most error-prone inference in the flow and the one whose failure is most expensive, because a route filed under the wrong sector is worse than no route. The uploader picks the sector in the upload form, using the picker they already know from `/routes/new`. That converts an impossible inference into given context, which also makes every *other* answer better: the same lesson plan 29 §2 learned when it started passing first ascensionists into the enrichment prompt.

## Design

### 1. `TopoImport`: the row that makes the feature legible

One new entity in `Acordanza.Core/Models/`, one migration:

```csharp
public class TopoImport
{
    public int Id { get; set; }

    // The uploaded image, under the _imports/ prefix until it's promoted (§2).
    public required string BlobName { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }

    // Given, never inferred — see the decision above.
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    public string UploadedByUserId { get; set; } = null!;
    public ApplicationUser UploadedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TopoImportStatus Status { get; set; }        // Pending | Completed | Discarded | Failed
    public string? Outcome { get; set; }                // Spanish, shown to the uploader
    public int? RouteId { get; set; }                   // the route created or matched
    public ClimbingRoute? Route { get; set; }
    public bool RouteWasCreated { get; set; }           // false ⇒ matched an existing one
    public DateTimeOffset? ProcessedAt { get; set; }
}
```

`TopoImportStatus` is string-converted in `OnModelCreating`, same convention as `ReviewStatus` and `LinkSource`. The four states are deliberately distinct: **`Discarded`** is the model saying "this isn't a usable topo" (a real answer, no retry needed unless the user disagrees), while **`Failed`** is the pipeline breaking — Foundry timeout, poison message, blob gone — which *is* worth retrying. Collapsing them would tell the uploader "we couldn't read it" when the truth is "we never looked."

`Outcome` is user-facing and therefore **Spanish**, like every `{ error }` payload the frontend can surface. It carries the discard reason: `No parece un topo de escalada`, `El topo no indica el nombre de la vía`, `El topo muestra varias vías y ninguna es claramente la principal`, `No se pudo leer un grado válido (escala francesa)`.

Delete behaviour: `Area` cascades (an import is scoped to its sector and means nothing without it), `UploadedBy` is `Restrict` (matching `Topo.UploadedBy` — deleting a user never cascades), `Route` is `SetNull` so deleting a bad route leaves the import's history intact rather than erasing the evidence of how it got there.

**The endpoint** is `POST /api/topo-imports` (multipart: `file` + `areaId`) on a new `TopoImportsController`, plus `GET /api/topo-imports/mine` for the uploader's own list. Both are `[Authorize(Policy = Permissions.RouteCreate)]` — *not* a new permission. `route.enrich` exists as its own admin-only permission because plan 18's spend was unbounded and the button was an optional extra; here the call *is* the feature, it costs ~$0.05, and the write lands as `Pending` for review. Someone who is trusted to create a route by typing it is trusted to create one by photographing it. The bound is a per-IP rate-limit policy (`topo-import`, 10/hour) registered alongside the existing `forgot-password` policy in `Program.cs`, which caps abuse without inventing a role.

`POST` validates the image through the existing `ImageValidation.ValidateImage` (same content-type allowlist and 15 MB cap as `UploadTopo`), checks the area exists, writes the blob and the `Pending` row, enqueues, and returns `202` with the row. A failed enqueue is a `503` and the row is not written — nothing else happened in the request, same call plan 18 §1 made.

### 2. Blob layout: upload to a staging prefix, promote on success

Azure Blob Storage has no rename, so "temp folder then move" is really upload → download → re-upload → delete. That's fine, and it keeps the permanent blobs' naming convention (`{area-slug-path}/{route-slug}-{NN}.{ext}`) exactly as `MediaUploadService.UploadNumberedAsync` produces it today, which is what every SAS-URL read and cascade-delete path already assumes.

- **On upload**, the API writes `_imports/{importId-ish}.{ext}` in the existing `topos` container. Same container, distinct prefix — a second container would need its own Terraform resource, its own role assignment, and its own ensure-exists call, for no isolation this app needs.
- **On a successful import**, the worker downloads the staged blob and re-uploads it through `MediaUploadService.UploadNumberedAsync` under the route's proper prefix, exactly like a human `UploadTopo`, then deletes the staged blob. `TopoImport.BlobName` is updated to the promoted name so the row still points at a live image.
- **On a discard**, the staged blob is deleted immediately but the row survives with its reason. The image is the thing that costs money to keep; the explanation is what the user needs.
- **Orphans** (a crashed run between upload and processing) are swept by an Azure Storage lifecycle-management rule on the `_imports/` prefix, delete-after-7-days. That's one Terraform block and it means no cleanup code to get wrong.

`BlobStorageService` gains the two methods this needs and currently lacks: **`DownloadAsync(blobName)`** (needed both to promote and to feed the vision calls) and **`ListAsync(prefix)`** (needed for the few-shot set in §5). Both are thin wrappers over the `BlobContainerClient` the service already holds, and both belong in Core since the worker is the only caller today but the API is the obvious second one.

### 3. Two calls: a cheap gate, then a high-resolution read

**A second queue, not a second message type.** `topo-import` alongside `route-enrichment`, on the same storage account. The existing `EnrichmentMessage` contract and `EnrichmentQueueProcessor` stay untouched — a discriminated union on one queue would muddy both, and `EnrichmentQueueProcessor` currently treats an undeserializable message as poison, which a second shape on the same queue would trip. Two queues also let each carry its own visibility timeout: enrichment needs 5 minutes for a web-search loop, a topo import needs about one.

**Still one job and one image.** `azurerm_container_app_job.enrichment` gains a second KEDA `azure-queue` scale rule, and `Acordanza.Worker/Program.cs` drains both queues in sequence and exits. This is the fork worth naming: a second job would mean a second Terraform resource and a second deploy step, and a CLI arg to select which queue to drain would re-introduce exactly the shared-configuration-between-two-processes failure plan 22 exists to prevent. One binary, no branches, two drains.

**The gate — `claude-haiku-4-5`, the existing deployment.** One vision call, structured output `{ isTopo: bool, reason: string }`: is this a climbing topo at all — a route-line photo, a drawn overlay, a guidebook page — as opposed to a selfie, a landscape, a map, a screenshot? A `false` short-circuits the run into `Discarded` before the expensive call. This is a *different question* from the one `ITopoVerificationProvider` answers today ("is this a topo on which route X is identifiably marked"), which needs a route that doesn't exist yet — so it gets a sibling interface, `ITopoImportGateProvider`, not a widened one.

**The extraction — `claude-sonnet-5`, a new deployment.** And the reason it isn't Haiku is not "smarter model", it's **resolution**:

| Model | Max image | Image tokens | $/MTok in / out |
|---|---|---|---|
| `claude-haiku-4-5` | 1568 px long edge | ~1600 | $1 / $5 |
| `claude-sonnet-5` | 2576 px long edge | up to ~4784 | $3 / $15 |

Anything larger is downsampled server-side, so a 4000 px phone photo of a guidebook page reaches Haiku at 1568 px no matter what is sent. For a binary "is there a line drawn on this rock" that is plenty. For reading `La Loca 6b+ 25m` off small print it is very possibly the difference between a correct extraction and a garbled one — which is the whole feature. The gate is a low-resolution binary question; the extraction is a high-resolution reading task, and the split falls out of the tiers rather than out of taste. (No client-side resizing is needed, and therefore no new image-processing dependency: the API downsamples above the cap itself. Plan 29 §4 rejected an HTML parser on supply-chain grounds; this stays on the right side of that line.)

The extraction schema borrows plan 29 §2's shape, because the same trick applies — make a fabricated answer more expensive to produce than a blank one:

```json
{
  "route": {
    "name":       "the route name exactly as printed",
    "grade":      "the grade exactly as printed, not converted",
    "length":     25,
    "evidence":   "the text you read this from, copied verbatim",
    "confidence": "high | medium | low"
  },
  "multipleRoutes": false,
  "reason": "why route is null, if it is"
}
```

`route` is nullable (`anyOf` with `null` — supported; note `pattern`, `minLength`, and numeric bounds are **not** supported by structured outputs, so every constraint below is enforced in C#, not in the schema).

`RouteImportService` accepts the row only when `confidence == "high"`, `evidence` is non-empty, and the grade survives `GradeValidator.IsValid` after `Normalize`. **The model is told to copy the grade, never to convert it** — Spanish guidebooks are usually French-scale, but UIAA (`V+`) and YDS (`5.11a`) appear, and a converted grade is a guess wearing the costume of data. An unconvertible grade is a discard with a reason, not a best effort. `length` is optional and left at its column default when the topo doesn't state it; confirm `ValidateLength`'s floor accepts that at implementation time.

`multipleRoutes: true` **with no clearly dominant route is a discard**, not a coin flip. Trad topos are usually single-route, which is what makes one-route-per-import the right model — but when they aren't, a confidently wrong route is worse than a stated failure the uploader can act on.

`stop_reason` is checked exactly as plan 29 §2 does: `max_tokens` or `refusal` logs a warning and produces `Failed`, not `Discarded`, so a re-press works instead of the user being told their topo was unreadable when the pipeline simply fell over.

**Cost per import**, for the record and to be checked against the logged `usage` rather than believed: gate ≈ $0.002 (one downsampled image plus a short prompt on Haiku), extraction ≈ $0.05 (a ~4784-token image plus prompt and output on Sonnet 5). Call it **$0.05**, against enrichment's $0.60–1.50.

### 4. Match an existing route, or create a pending one

With the sector given, matching is deterministic and needs no fuzzy logic: compare `Slugify.ToSlug(extractedName)` against `ClimbingRoute.Slug` **within the given area exactly** — not its descendants. `Slug` is already unique per `AreaId` and is regenerated whenever the name or area changes, so it is precisely the right key, and the uploader picked that sector explicitly rather than by habit.

**On a match — attach the topo, change nothing else.**

- Import the image through `MediaUploadService.UploadNumberedAsync` as a `Topo` on the matched route, **even if it already has topos**. Enrichment skips in that case because nobody asked for the import; here a human deliberately uploaded, and the numbered-name loop already handles the collision.
- `Topo.UploadedByUserId` is the actual uploader — cleaner than enrichment, which had to attribute to `route.AddedByUserId` because no human was in the loop.
- **A grade disagreement does not block the match and does not rewrite the route.** Record it in `Outcome` (`La vía ya existía; el topo indica 6b+ y la ficha 6a`) and let a human decide. Silently correcting a human-entered grade from a photograph is the wrong default in a catalogue whose whole point is that people entered it.
- **`ValidationStatus` is not touched.** Same invariant as enrichment, and consistent with `UploadTopo`, which doesn't invalidate a route today either. Adding a photo is not an edit of the route's data.

**On no match — create the route as `Pending`.**

- Written through `AppDbContext` directly, never through `RoutesController.CreateRoute`, so `Validation.MarkValidated` is structurally out of the path. This matters: `CreateRoute` auto-approves when the creator can validate the area, and an admin photographing a topo must still get a route somebody has *read* before it goes live. The worker never approves.
- `AddedByUserId` is the uploader, so the route lands in their `/my-acordanza/contributions` and stays visible to them under plan 11's rule while it waits for review.
- `Slug` via the same uniqueness loop `RoutesController.GenerateUniqueRouteSlugAsync` uses — hoist it to a shared helper rather than reimplementing it, for the same reason `LinkRules` was hoisted in plan 18.
- The topo is imported and attached to the new route; then `Status = Completed`, `RouteWasCreated = true`, `RouteId` set.

Every run logs one line — `topo import stage=… gate=… extracted=… matched=… created=…` — the direct analogue of plan 29 §7's stage counts, and the thing that says whether to tune the gate prompt or the extraction prompt.

### 5. Few-shot examples in blob storage, and how this gets better

The gate is a binary classifier, which is exactly the shape few-shot examples help most. `ClaudeTopoImportGateProvider` reads a small labelled set from blob storage at startup and prepends it to every gate call:

```
_fewshot/gate/topo/*.jpg        → labelled "this IS a topo"
_fewshot/gate/not-topo/*.jpg    → labelled "this is NOT a topo"
```

The prefix *is* the label, so there's no manifest file to drift. Curating is `az storage blob upload` into the right prefix — deliberately manual, matching the precedent that plan 29 §7's `GoldenSet.json` is checked in empty for a human to fill.

**Blob storage rather than the repo, because the repo is public.** In-repo embedded resources would be nicer — versioned with the prompt, reviewable in the same PR — but these are third-party topo scans, and checking them into a public repository is a copyright exposure this app does not currently have. Blob keeps them out of git at the cost of being unversioned and invisible to review; the golden set (below) is what compensates by making a bad example set *measurable* instead of merely reviewable.

**Prompt caching is considered and rejected, on arithmetic.** Haiku 4.5's minimum cacheable prefix is **4096 tokens** — the highest of any current model, and non-monotonic across generations (Opus 5 is 512, Sonnet 5 is 1024). Six examples at ~1600 tokens each would clear it. But the default cache TTL is 5 minutes, and this app's uploads are sporadic, so hits would essentially never land; the 1-hour TTL doubles the write cost and needs three reads to break even. Same conclusion plan 29 reached about caching, reached by different arithmetic. Six examples cost about $0.01 per gate call uncached, which is the right thing to simply pay.

**Which makes "does it learn over time?" answerable, honestly.** Claude models are not fine-tunable, so there is no training loop — but the `TopoImport` table is a labelled corpus accumulating for free. Every row records the image, the gate verdict, the extraction, and, crucially, what the human did next: approved the pending route, deleted it, or re-uploaded a topo the gate wrongly rejected. The loop is then plan 29 §7's, unchanged in shape: a human periodically promotes rows into the few-shot prefixes and into `TopoImportGoldenSet.json`, and a `[Trait("Category", "Enrichment")]` harness measures gate precision/recall and extraction accuracy before and after any prompt change. That is real learning — measurable, reversible, and reviewed — with a person in the loop rather than gradient descent. Both CI test jobs already exclude that trait, so nothing needs rewiring.

### 6. Terraform, config, and the third deployment

- `infra/terraform/ai_foundry.tf`: a third `azapi_resource` deployment, `claude-sonnet-5`, GlobalStandard, **Hosted on Anthropic** (`model.version`, not `model.format` — the file's own comment explains why, and the version id is per-model, so look it up via the `az rest` catalog query documented there rather than copying opus's `"1"`), `versionUpgradeOption = "NoAutoUpgrade"`, and `depends_on = [azapi_resource.claude_haiku]` to keep the per-account creation chain serialized. The deployment name must equal the model name — the worker passes it straight through as `MessageCreateParams.Model`, so a mismatch is a 404 at request time.
- `infra/terraform/storage.tf`: `azurerm_storage_queue "topo_import"`, plus the lifecycle-management rule on the `_imports/` prefix (§2). The existing Storage Queue Data Contributor and Blob Data Contributor role assignments cover both without change.
- `infra/terraform/container_app_job.tf`: a second scale rule for the new queue, and `Storage__TopoImportQueueName` in the env block. No new secret — Foundry auth is still the managed identity, and `Anthropic__FoundryResource` already points at the account.
- Config keys: `Storage:TopoImportQueueName` (default `topo-import`) and `Storage:FewShotPrefix` (default `_fewshot/`). Without `Anthropic:FoundryResource` the providers register as no-ops exactly like the enrichment ones, so the whole upload → queue → worker → row loop stays exercisable locally against Azurite with no Foundry access and no `az login`.
- `Acordanza.Worker.Tests/Services/WorkerConfigurationTests` grows to cover the new key, so a missing env var fails a fast test rather than a live job execution — the failure mode plan 22 exists because of.

### 7. Frontend

- **`/my-acordanza/my-imports`** ("Mis importaciones"), a new page under the existing left-nav shell, holding both the upload form (file picker + area picker, reusing the one `RouteFormPage` already uses) and the list of the user's imports with status, reason, and a link to the resulting route. One page rather than two: the list is the only place the upload's outcome can appear, so putting the form anywhere else splits one flow across two screens. Slug stays English, label stays Spanish, per the root `CLAUDE.md` rule.
- **`RouteFormPage`** gains a link to it — "¿Tienes un topo? Créala desde la foto" — since `/routes/new` is where someone with a topo in their hand actually starts.
- On submit: `POST`, then an inline note — `Estamos leyendo el topo. El resultado aparecerá aquí en unos minutos.` No polling, no progress bar; the row is already in the list with `Pendiente`, and a reload updates it. Same deliberate non-interactivity as plan 18 §1's button, for the same reason: the work is asynchronous and there is nothing to stream.
- A `Discarded` row offers "Volver a intentarlo", which re-uploads rather than re-enqueueing — the blob is gone by then (§2), and a user who disagrees with a discard usually wants to try a better photo anyway.

### 8. Not doing

- **Multi-route topos.** One route per import, by decision: trad topos are typically single-route, and a topo showing several with no dominant one is a discard (§3). Sector-wide guidebook pages producing N routes is a different feature with a different review shape — batch creation, per-row dedupe, an `AreaMedia`-vs-`Topo` question — and it would want its own plan.
- **Inferring the sector.** §Problem's second decision. The picker stays.
- **Filling blank fields on a matched route** (length, gear, orientation from the topo). Tempting, and probably the best follow-up here, but it turns a read-only match into an edit of a human's record and drags `Validation.InvalidateIfChanged` into the path. Attach the topo, record the disagreement, stop.
- **Auto-approving anything.** Machine-created routes are always `Pending`, including for admins (§4).
- **A separate review UI.** `/manage/pending-tasks` already is one; that's the point of §Problem's second observation.
- **Enriching the created route automatically.** Chaining a `route-enrichment` message onto a successful import is one line and is exactly the unbounded-spend shape plan 18's trigger decision exists to prevent. The button on the route detail modal still works.
- **Client-side image resizing**, and the dependency it would need. The API downsamples (§3).
- **Prompt caching** (§5), on the arithmetic above.

## Files touched

| File | Change |
| --- | --- |
| `backend/Acordanza.Core/Models/TopoImport.cs` | new — the entity + `TopoImportStatus` (§1) |
| `backend/Acordanza.Core/Data/AppDbContext.cs` | `DbSet<TopoImport>`, status conversion, delete behaviours (§1) |
| `backend/Acordanza.Core/Migrations/*` | one migration: `TopoImport` (§1) |
| `backend/Acordanza.Core/Services/BlobStorageService.cs` | + `DownloadAsync`, + `ListAsync(prefix)` (§2) |
| `backend/Acordanza.Core/Services/TopoImportMessage.cs` | new — the wire contract, sibling of `EnrichmentMessage` (§3) |
| `backend/Acordanza.Api/Controllers/TopoImportsController.cs` | new — `POST /`, `GET /mine` (§1) |
| `backend/Acordanza.Api/Dtos/TopoImportDtos.cs` | new — the read DTO (§1) — forces the OpenAPI regen |
| `backend/Acordanza.Api/Services/TopoImportQueue.cs` | new — enqueue side only (§1) |
| `backend/Acordanza.Api/Program.cs` | queue registration + ensure-exists, `topo-import` rate-limit policy (§1) |
| `backend/Acordanza.Api/Controllers/RoutesController.cs` | hoist `GenerateUniqueRouteSlugAsync` to a shared helper (§4) |
| `backend/Acordanza.Worker/Program.cs` | drain the second queue, then exit (§3) |
| `backend/Acordanza.Worker/Services/TopoImportQueueProcessor.cs` | new — drain loop, mirrors `EnrichmentQueueProcessor` (§3) |
| `backend/Acordanza.Worker/Services/RouteImportService.cs` | new — gate → extract → match-or-create → promote blob (§3–§4) |
| `backend/Acordanza.Worker/Services/ITopoImportGateProvider.cs` (+ Claude/NoOp impls) | new — the Haiku gate, few-shot loading (§3, §5) |
| `backend/Acordanza.Worker/Services/ITopoExtractionProvider.cs` (+ Claude/NoOp impls) | new — the Sonnet 5 extraction (§3) |
| `backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs` | register both providers behind the same `Anthropic:FoundryResource` gate (§6) |
| `backend/Acordanza.Api/appsettings*.json`, `backend/Acordanza.Worker/appsettings*.json` | `Storage:TopoImportQueueName`, `Storage:FewShotPrefix` (§6) |
| `infra/terraform/ai_foundry.tf` | third deployment: `claude-sonnet-5`, Hosted on Anthropic (§6) |
| `infra/terraform/storage.tf` | `topo-import` queue + `_imports/` lifecycle rule (§2, §6) |
| `infra/terraform/container_app_job.tf` | second scale rule + the new env key (§6) |
| `backend/Acordanza.Api/openapi/Acordanza.Api.json`, `frontend/src/api/generated.ts` | regenerated, or `openapi-contract-check` fails |
| `frontend/src/pages/MyImportsPage.tsx` | new — upload form + import list (§7) |
| `frontend/src/pages/MyAcordanzaLayout.tsx`, `frontend/src/App.tsx` | nav item + route (§7) |
| `frontend/src/pages/RouteFormPage.tsx` | link across to the import flow (§7) |
| `frontend/src/api/topoImports.ts`, `frontend/src/api/types.ts` | client calls + types (§7) |
| `backend/Acordanza.Worker.Tests/Services/RouteImportServiceTests.cs` | new (§Testing) |
| `backend/Acordanza.Worker.Tests/Services/TopoImportGoldenSetTests.cs` + `TopoImportGoldenSet.json` | new — `Category=Enrichment`, checked in empty (§5) |
| `backend/Acordanza.Api.Tests/Controllers/TopoImportTests.cs` | new (§Testing) |
| `frontend/src/pages/MyImportsPage.test.tsx` | new (§Testing) |
| `backend/CLAUDE.md`, `CLAUDE.md`, `README.md`, `infra/README.md` | the new controller, queue, providers, config keys, deployment, and the `/my-acordanza/my-imports` route |

## Testing

**Unit** (`Acordanza.Worker.Tests`, no Docker): the acceptance predicate — `confidence != "high"`, empty `evidence`, `multipleRoutes` with no dominant route, and an unconvertible grade each produce `Discarded` with a reason; a valid row produces a candidate. `stop_reason` of `max_tokens`/`refusal` produces `Failed`, never `Discarded` and never an exception. Slug matching: same name different case matches, same name in a *sibling* area does not (§4's exact-area rule).

**Integration** (`Category=Integration`, `WorkerFixture`, fake providers — no real Foundry call ever in CI):

- A gate `false` discards, deletes the staged blob, writes the reason, and creates no route.
- A clean extraction with no existing match creates a route with `ValidationStatus == Pending` **even when the uploader is an admin** — the §4 invariant, asserted explicitly, because `CreateRoute` does the opposite.
- A match attaches a `Topo` to the existing route, leaves its `ValidationStatus` and `Grade` untouched, and records the grade disagreement in `Outcome`.
- A match against a route that already has topos still appends, with the next number.
- The staged blob is promoted to `{area-slug-path}/{route-slug}-NN.ext` and the `_imports/` copy is gone.
- A poison message marks the row `Failed` rather than leaving it `Pending` forever.

**API** (`Acordanza.Api.Tests`): `POST` as a plain `user` returns 202 and enqueues exactly one message carrying the row id; an oversize or wrong-content-type file 400s with the Spanish message and enqueues nothing; a bad `areaId` 400s; `GET /mine` returns only the caller's rows.

**Frontend** (`MyImportsPage.test.tsx`, Vitest + MSW): the four statuses render their Spanish labels; a successful upload shows the inline "unos minutos" note; a `Discarded` row shows its reason and the retry affordance; a `Completed` row links to the route.

**Manual smoke against real Foundry, before merge** — this is the step that validates §3's resolution argument rather than assuming it. Take three real topos (a clean guidebook page, a weathered panel photo, a phone snap at an angle) plus two non-topos, run each through, and record: gate verdict, extracted fields, and whether the grade survived `GradeValidator`. Read the logged `usage` for the actual per-import cost and put the number in the PR — the $0.05 estimate in §3 should be checked, not believed. If the clean guidebook page fails to extract on Sonnet 5, that is the signal to try Opus 5 before tuning the prompt.

Then `TopoImportGoldenSet.json` (§5) as the acceptance gate for prompt changes from here on, reporting gate precision/recall and extraction accuracy. Precision is the target, as in plan 29: a correct discard is a success.

`dotnet test Acordanza.slnx` from the repo root with Docker up; `bun run test` in `frontend/`; `terraform validate`/`plan` under `infra/terraform/` for the queue, lifecycle rule, deployment, and scale rule before the PR.
