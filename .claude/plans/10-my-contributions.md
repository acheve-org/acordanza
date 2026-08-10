# "Mis contribuciones" page + a reject-with-reason validation lifecycle

Status: done (landed in 08db89a, refined in 3c70d22)
Issue: [#125](https://github.com/acheve-org/acordanza/issues/125)
Depends on: nothing. Independent of [07](07-test-harness.md)/[08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) — but if the harness lands first, the new `Validation` helper transitions and the three-way permission gating below are exactly the kind of thing plan 07/08 should cover.

## Problem

Issue #125 asks for a "Contribuciones" page under "Mi acordanza" listing the areas, routes and first ascensionists the current user created, each with its validation status and — when rejected — the rejection reason. Building it surfaces three gaps in the current model, none of which is just a UI concern:

1. **Two of the three entities have no creator.** `ClimbingRoute` has `AddedByUserId`/`AddedBy`/`CreatedAt`; `Area` and `FirstAscensionist` have neither. There is currently no way to answer "which areas did I create?" at all.
2. **`FirstAscensionist` has no validation lifecycle.** Only `ClimbingRoute` and `Area` implement `IValidatable`. `Permissions.FirstAscensionistValidate` (`first-ascensionist.validate`) already exists and is already granted to `moderator`/`admin` in `RolePermissions` — but a grep shows it is referenced *nowhere* outside those two declaration lists. It has always been a permission with no endpoint behind it. The issue asks for it to become real.
3. **Validation is binary — there is no rejection at all.** `IValidatable` is `ValidatedAt`/`ValidatedByUserId`, both nullable, and `POST /{id}/validate` only ever sets them. `RejectionReason` exists only on `IReviewableRequest` (the `AreaExpert`/`FirstAscensionistLinkRequest` self-service flows). So the "in case of rejection, the rejection reason" half of the issue has no data behind it, and no way for a moderator to produce one.

The consequence of (3) today: a route a moderator considers wrong just sits in `/manage/pending-tasks` forever (or gets deleted, losing the contributor's work with no explanation). The consequence of (1)+(2): the contributor never finds out either way.

## Design

### 1. `IValidatable` becomes a three-state lifecycle

The vocabulary already exists in this codebase, on the request-review side. Reuse it rather than inventing a parallel one:

```csharp
public interface IValidatable
{
    ReviewStatus ValidationStatus { get; set; }   // Pending | Approved | Rejected
    DateTimeOffset? DecidedAt { get; set; }
    string? DecidedByUserId { get; set; }
    string? RejectionReason { get; set; }
}
```

Three decisions packed into that, each worth stating:

**Reuse `ReviewStatus`, don't add a `ValidationStatus` enum.** The member set is identical (`Pending`/`Approved`/`Rejected`) and the string-column convention (`HasConversion<string>().HasMaxLength(20)`, `AppDbContext.cs:179`) carries over unchanged. The property is still named `ValidationStatus` — an entity never implements both interfaces, but the *name* keeps a grep for "validation status" distinct from the request flows. `ReviewStatus.cs`'s header comment ("Shared by every self-service request/approve/reject flow ... see `IReviewableRequest`") becomes wrong and must be widened.

**Rename `ValidatedAt`/`ValidatedByUserId` → `DecidedAt`/`DecidedByUserId`/`DecidedBy`.** These fields now get set on rejection too, so their old names would lie. More importantly, the rename is what makes the migration *safe*: every existing `ValidatedAt != null` check means "approved", and after this change that is only true by accident. Renaming turns each of those into a compile error instead of a silently-wrong truthiness test — on both sides, since the frontend's `generated.ts` regenerates and `validatedAt` disappears from `RouteDto`/`AreaDto`. It also lines the entity up with `IReviewableRequest`'s existing `DecidedAt`/`DecidedByUserId`/`RejectionReason` trio, so the two halves of the app finally spell approval the same way. The full sweep is small — 5 backend call sites (both controllers' DTO projections), 4 frontend ones (listed in §6).

**Keep one decided-at/by pair rather than adding a separate `RejectedAt`/`RejectedByUserId`.** A record is in exactly one state; two timestamp pairs would need an invariant nothing enforces.

`Models/Validation.cs` grows to cover every transition, staying the single source it was made to be:

```csharp
public static void MarkValidated(IValidatable entity, string userId);            // → Approved, stamps decision, clears RejectionReason
public static void MarkRejected(IValidatable entity, string userId, string reason); // → Rejected, stamps decision + reason
public static void InvalidateIfChanged(IValidatable entity, bool changed);       // → Pending, clears decision + reason (only when changed)
```

`InvalidateIfChanged` clearing `RejectionReason` is the load-bearing bit of the whole feature: it is what lets a contributor read the reason on the contributions page, fix the record, and have it land back in the moderator's queue automatically. Its existing "no-op re-save doesn't un-validate" guarantee is unchanged.

Note `MarkValidated` clears `RejectionReason` — validating a previously-rejected record must not leave a stale reason behind on an approved row.

### 2. Creator attribution on `Area` and `FirstAscensionist`

Mirror `ClimbingRoute`'s existing shape exactly rather than introducing an `IAuthored`-style interface (the three controllers project three different DTOs; an interface would buy nothing):

```csharp
public string? AddedByUserId { get; set; }
public ApplicationUser? AddedBy { get; set; }
public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
```

Nullable, unlike `ClimbingRoute.AddedByUserId` — existing rows have no recoverable creator, and there is nothing to backfill them from. Those rows simply never appear in anyone's contributions list; that is the honest answer and needs no fallback. `ClimbingRoute` is left alone (its column stays non-nullable and already carries real data, so route history is complete from day one).

FK config follows the existing rule for user references: `DeleteBehavior.Restrict`, same as `ClimbingRoute.AddedBy`/`ValidatedBy` and every `DecidedBy`. EF creates the FK index automatically, which is also the index the contributions query wants — nothing extra to configure.

Both controllers stamp the creator on create: `AreasController.CreateArea` and `FirstAscensionistsController.CreateFirstAscensionist` already call `userManager.GetUserId(User)!`.

### 3. `FirstAscensionist` joins the lifecycle

`FirstAscensionist : IValidatable`, plus:

- **Create** — auto-approve when the creator holds `first-ascensionist.validate`, otherwise leave `Pending`. This is a copy of the rule `AreasController.CreateArea` already applies for areas (`User.HasClaim("permission", Permissions.AreaValidate)` → `Validation.MarkValidated`), so a moderator creating an opener doesn't file a task for themselves while a regular user's creation does.
- **Update** — `Validation.InvalidateIfChanged(fa, changed: trimmedName != fa.Name)`, same rule routes and areas follow. Consequence worth naming: a linked user renaming their *own* record sends it back to the queue. That is consistent with how every other edit behaves here, and re-approval is one click.
- **`POST /{id}/validate`** and **`POST /{id}/reject`**, both `[Authorize(Policy = Permissions.FirstAscensionistValidate)]` — the first actual use of that permission. No `AreaExpertAuthorizer` equivalent: openers aren't scoped to an area.
- **`GET /pending`**, `[Authorize(Policy = Permissions.FirstAscensionistValidate)]`, returning the pending records for the review queue. A dedicated endpoint rather than a `validationStatus` field on `FirstAscensionistDto`: that DTO is embedded in *every* route payload, and the review queue is the only consumer of the status. This mirrors `GET /area-experts/pending`, which the frontend api layer already wraps as `listPendingAreaExperts`.

Status fields go on `FirstAscensionistProfileDto` (the "Sigue" detail view), the new pending DTO, and the contributions DTO — not on the slim `FirstAscensionistDto`.

**Explicit non-goal: validation does not gate visibility.** A pending or rejected first ascensionist stays listed in `/first-ascensionists`, in `FirstAscensionistPicker`, and on route payloads — exactly as unvalidated routes and areas are fully visible today. Validation is an editorial signal, not a publication gate, and turning it into one is a much larger product change than this issue asks for.

### 4. Reject endpoints for routes and areas

Three symmetric endpoints, one body record `RejectValidationRequest(string Reason)` in a shared DTO file:

| Endpoint | Guard |
|---|---|
| `POST /api/routes/{id}/reject` | `areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, route.AreaId)` — identical to `ValidateRoute`, so an area expert can reject within their area too |
| `POST /api/areas/{id}/reject` | `[Authorize(Policy = Permissions.AreaValidate)]` |
| `POST /api/first-ascensionists/{id}/reject` | `[Authorize(Policy = Permissions.FirstAscensionistValidate)]` |

Empty/whitespace reason → `400 { error = "el motivo es obligatorio" }`, reusing verbatim the string `AreaExpertsController.Reject` already returns. Reason is `.Trim()`ed before storing, same as there.

No `ReviewRequestService`-style conditional `ExecuteUpdateAsync` here, deliberately. That primitive exists because a *request* must be decided exactly once — a second decision would be a bug. Validation is not once-only: re-validating an approved route, or rejecting one that was previously approved, are both legitimate moderator actions, and the existing `validate` endpoint is already a plain load/mutate/`SaveChangesAsync`. Keeping reject in the same shape means the two stay symmetric. (Generalising `ReviewRequestService` over `IValidatable` was considered and rejected: the interfaces share no members — no `UserId`, no `RequestedAt` — so it would be a second generic overload with a different constraint and no shared body.)

No decision emails. `AreaExpert`/`FirstAscensionistLink` decisions notify because the requester is blocked waiting on an answer; a rejected route is a passive editorial note the contributor reads on their contributions page. Adding `EmailTemplates` entries for it is a reasonable follow-up, not part of this.

### 5. Read model: `GET /api/contributions/mine`

New `ContributionsController` (`api/contributions`) — a read model over three tables, so it belongs to none of the existing per-resource controllers:

```csharp
public record ContributionDto(
    int Id,
    string Name,
    string? Detail,              // routes: "Peña Rúa › Sector 1 · 6a"; areas: their name path; openers: null
    DateTimeOffset CreatedAt,
    string ValidationStatus,     // "pending" | "approved" | "rejected"
    DateTimeOffset? DecidedAt,
    string? DecidedByName,
    string? RejectionReason);

public record MyContributionsResponse(
    List<ContributionDto> Routes,
    List<ContributionDto> Areas,
    List<ContributionDto> FirstAscensionists);
```

- One endpoint, not three `?mine=true` filters on the existing controllers: the page is one screen and wants one round trip, and the three rows are the same shape once `Detail` absorbs the per-kind difference.
- Three arrays rather than a flat list with a `kind` discriminator, because the page is tabs-with-counts (same shape as `PendingTasksPage`) and this hands it the counts for free.
- `ValidationStatus` is lowercased on the way out (`e.ValidationStatus.ToString().ToLowerInvariant()`), matching `AreaExpertsController.ToDto` — which is what lets the frontend reuse its existing `RequestStatus` union.
- `Detail` is computed server-side via the already-injected `AreaHierarchyService.NamePath`, rather than shipping the whole area list so the client can build breadcrumbs (which is what `PendingTasksPage` has to do today).
- Ordered `CreatedAt` descending per array.
- **Unpaginated.** At this app's scale (a private group) a contributor has tens of rows, and paging three independent lists in one response is disproportionate. Accepted risk, noted here so it's a deliberate choice: if a prolific user makes it unpleasant, add per-tab paging reusing the existing `Pagination` component and a `page`/`pageSize` per array.

### 6. Frontend

**New page.** `pages/MyContributionsPage.tsx` at `/my-acordanza/contributions` (slug in English, label in Spanish — root `CLAUDE.md`), reached from a new "Contribuciones" `NavLink` in `MyAcordanzaLayout`, placed after "Vincular aperturista" and before the conditional "Mis aperturas" item. `api/contributions.ts` + a `queryKeys.myContributions()` entry. It's a new page, so it uses `useQuery` from the start rather than the legacy `useState`/`useEffect`/`refresh*` pattern.

Layout: the `.tab-bar`/`.tab-item` + `Badge` count markup from `PendingTasksPage` (three tabs: Vías / Sectores / Aperturistas), `List`/`ListRow` rows with `Chip status={...}` as `trailing` — `Chip` already supports exactly `'pending' | 'approved' | 'rejected'` — and the subtitle carrying date, decider and reason in the shape `MyAreasPage` already uses:

```
{statusLabel}{decidedByName && ` · ${decidedByName}`}{status === 'rejected' && rejectionReason && ` · Motivo: ${rejectionReason}`}
```

**Consolidate the duplicated status labels.** `MyAreasPage.tsx:8` and `ClaimFirstAscensionistPage.tsx:8` each define a private `statusLabels` map with the same three Spanish strings; the new page would be a third copy. Move one to `api/types.ts` as `reviewStatusLabels: Record<RequestStatus, string>`, next to `orientationLabels`/`gearLabels` (root `CLAUDE.md`: reuse those maps rather than hardcoding translations per component), and point all three at it. Note the existing strings are request-flow wording ("Pendiente de aprobación", "Aprobado", "Rechazado"); validation reads better as "Pendiente de validación", "Validado", "Rechazado" — so this is two exported maps sharing one `RequestStatus` key type, not one map forced to cover both.

**The rename sweep** (all four sites, each a type error once `generated.ts` regenerates):

| Site | Change |
|---|---|
| `context/PendingTasksContext.tsx:67` | `!r.validatedAt && r.canValidate` → `r.validationStatus === 'pending' && r.canValidate` |
| `context/PendingTasksContext.tsx:80` | `!a.validatedAt` → `a.validationStatus === 'pending'` |
| `components/DetailModal.tsx:70,78` | approved banner keys off `validationStatus === 'approved'` (`validatedBy`→`decidedByName`, `validatedAt`→`decidedAt`); new rejected banner showing `rejectionReason`; the "Validar" button now also renders a "Rechazar" button opening the shared `RejectReasonModal` |
| `pages/AreaPage.tsx:162` | same banner treatment |

The `pending` (rather than `!approved`) filter in `PendingTasksContext` is not cosmetic: without it a rejected route would sit in the bell badge forever, since rejection never sets the old `validatedAt`.

**`PendingTasksPage`.** Routes are already reviewed through `DetailModal`, so they get reject for free from the sweep above. The areas tab gains a "Rechazar" button next to "Validar" (→ `RejectReasonModal`). A fourth "Aperturistas" tab is added, fed by a new `pendingFirstAscensionists` slice in `PendingTasksContext` — a fifth `Promise.allSettled` leg calling the new `GET /first-ascensionists/pending`, gated on `hasPermission(Permissions.firstAscensionistValidate)`, contributing to `total` and to `visible` exactly like the other four.

## Steps

1. **Model + migration.** `IValidatable` rework, `ReviewStatus` comment widened, `Validation` helper's three transitions, `FirstAscensionist : IValidatable`, `AddedByUserId`/`AddedBy`/`CreatedAt` on `Area` + `FirstAscensionist`, `AppDbContext` config (string conversion + max length for the three status columns, `Restrict` on the two new user FKs). Then `dotnet ef migrations add ValidationRejectionAndContributors`.
   **Inspect the generated migration before running it**: the `ValidatedAt`→`DecidedAt` rename must come out as `RenameColumn`, not a `DropColumn`+`AddColumn` pair (which would silently discard every existing validation). If the scaffolder emits drop/add, hand-edit it into a rename with a comment saying why — the same documented-exception treatment `AddUniqueEmailAndPasskeys` got. Add a `migrationBuilder.Sql` backfill setting `ValidationStatus = 'Approved'` where `DecidedAt IS NOT NULL`, `'Pending'` otherwise, for `Routes` and `Areas` (new `FirstAscensionists` rows default to `Pending`, which is the correct starting state for records nobody has reviewed).
2. **Controllers.** `RoutesController`/`AreasController`: reject endpoint + DTO projections updated. `FirstAscensionistsController`: creator stamping, auto-approve on create, `InvalidateIfChanged` on update, validate/reject/pending endpoints, status on the profile DTO. `AreasController.CreateArea`: creator stamping.
3. **`ContributionsController` + DTOs.**
4. **Regenerate the contract.** `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` (needs Postgres + Azurite up and `Jwt:Key` set), then `bun run generate-api` in `frontend/`, and commit both diffs — CI's `openapi-contract-check` fails the build otherwise.
5. **Frontend sweep.** `api/types.ts` (new type aliases + the two label maps), the four rename sites, `DetailModal`/`AreaPage` reject flows, `PendingTasksContext` + `PendingTasksPage`'s fourth tab and areas reject button.
6. **New page.** `api/contributions.ts`, `queryKeys`, `MyContributionsPage`, the `<Route>` in `App.tsx`, the `NavLink` in `MyAcordanzaLayout`.
7. **Docs.** Root `CLAUDE.md`: add `/my-acordanza/contributions` to the route-slug list. `backend/CLAUDE.md`: the three-state `IValidatable` and the widened `Validation` helper (the current text describes it as clearing/setting two fields), `FirstAscensionist` now validatable and carrying a creator, the new endpoints, the new controller, and the note that `first-ascensionist.validate` is no longer an unused permission. `frontend/CLAUDE.md`: the new page + api file, the `reviewStatusLabels`/`validationStatusLabels` consolidation, the fourth pending-tasks category.
8. **Verify** with the `run-acordanza` skill: as a `user`, create an area/route/opener and see all three land as "Pendiente de validación"; as a `moderator`, reject each with a reason; back as the user, read the reasons on `/my-acordanza/contributions`, edit one, and confirm it returns to pending and reappears in the moderator's bell.

No automated tests — there is still no suite on either side (plans 07–09).

## Decisions to confirm before starting

- **The `ValidatedAt` → `DecidedAt` rename.** Recommended above for the compile-error safety net, but it is the single widest part of the diff and touches the checked-in OpenAPI document. Keeping the old names and only adding `ValidationStatus`/`RejectionReason` is viable, at the cost of every existing `ValidatedAt != null` check quietly meaning "decided" instead of "approved" until someone notices.
- **Pre-existing areas and openers are invisible on the contributions page** (no creator to backfill). Routes are unaffected.
- **Pending/rejected records stay fully visible** across the app (§3) — no publication gating.
- **The contributions endpoint is unpaginated** (§5).
