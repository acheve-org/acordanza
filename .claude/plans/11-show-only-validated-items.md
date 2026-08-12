# Show only validated items outside "Mi acordanza"

Status: done (landed in 3e5a837)
Issue: [#72](https://github.com/acheve-org/acordanza/issues/72)
Depends on: nothing — but it builds directly on the three-state validation lifecycle and creator attribution landed by [10](10-my-contributions.md) (`IValidatable.ValidationStatus`, `AddedByUserId` on all three entities, the `/manage/pending-tasks` queue, the "Contribuciones" page). Both are already in `main` (e8266e8). The test harness from [07](07-test-harness.md)/[08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is also in, so this one *does* get automated tests — see Steps.

## Problem

Plan 10 built the validation lifecycle and stated an explicit non-goal:

> **Explicit non-goal: validation does not gate visibility.** A pending or rejected first ascensionist stays listed in `/first-ascensionists`, in `FirstAscensionistPicker`, and on route payloads — exactly as unvalidated routes and areas are fully visible today. Validation is an editorial signal, not a publication gate.

Issue #72 reverses that decision: validation *is* a publication gate. Outside "Mi acordanza", only approved routes, areas and first ascensionists may be shown. Pending and rejected ones stay reachable to their author (the "Contribuciones" page, so work isn't lost) and to reviewers (the pending-tasks queue).

Today, nothing filters. Every read endpoint returns every row regardless of `ValidationStatus`:

| Endpoint | Filters on status today |
|---|---|
| `GET /routes`, `GET /routes/grades`, `GET /routes/{id}` | no |
| `GET /areas`, `/areas/roots`, `/areas/{id}/children`, `/areas/by-slug-path/*` | no |
| `GET /first-ascensionists`, `/first-ascensionists/profiles`, `/first-ascensionists/{id}` | no |
| `GET /first-ascensionists/pending` | yes — the only status-aware read in the app |

Four things make this more than a `.Where()` sweep, and each is a section below:

1. **The review queue reads through the browse endpoints.** `PendingTasksContext` fetches *every* route (`listRoutes(emptyRouteFilters)`) and *every* area (`listAreas()`) and filters client-side for `validationStatus === 'pending'`. Filter those endpoints and the bell silently empties. §3.
2. **`RoutesController.CreateRoute` never auto-validates.** `AreasController.CreateArea` and `FirstAscensionistsController.CreateFirstAscensionist` both mark the record approved when the creator holds the matching validate permission; routes don't. Today that's invisible. The moment visibility is gated, a moderator's own new route disappears from "Busca" the instant they save it. §4.
3. **The management screens legitimately need everything.** `/manage/areas` (`AreaTree` → `/areas/roots` + `/areas/{id}/children`, `AreaPolygonModal` → `/areas`) and `/manage/first-ascensionists` exist to *fix* records, including rejected ones. §5.
4. **Existing data is mostly `Pending`.** Plan 10's migration backfilled `Approved` only where `DecidedAt IS NOT NULL`; every route ever created is `Pending` unless someone explicitly validated it, and `SeedData.CreateRouteAsync` doesn't set a status either. Turning the gate on without a backfill empties the app and floods the queue. §7 + Decisions.

## Design

### 1. One rule, three expressions

```
visible(x)  ⟺  x.ValidationStatus == Approved  ||  x.AddedByUserId == callerId
```

The second clause is the issue's closing paragraph ("show only validated items *and contributed ones* independently of validation status"). It is cheap — one more `OR` on an already-indexed FK column — and it is what keeps the app usable for a contributor: they can create a sector, then immediately pick it in the route form, then tick their own route, all before a moderator gets to it. Without it, a regular user's first sector is invisible to them everywhere except the Contribuciones page, which reads as a bug.

Deliberately **not** in the rule: "or the caller can validate it". Moderators browse the same "Busca"/"Explora" everyone else does; review happens in the dedicated queue (§3). One rule for every reader keeps the endpoints honest and means a moderator's screenshot of a page matches what a user sees.

New file `backend/Acordanza.Api/Models/ValidationVisibility.cs` — three one-line predicate expressions, so the rule lives in one greppable place:

```csharp
public static class ValidationVisibility
{
    public static Expression<Func<ClimbingRoute, bool>> Routes(string userId) =>
        r => r.ValidationStatus == ReviewStatus.Approved || r.AddedByUserId == userId;

    public static Expression<Func<Area, bool>> Areas(string userId) =>
        a => a.ValidationStatus == ReviewStatus.Approved || a.AddedByUserId == userId;

    public static Expression<Func<FirstAscensionist, bool>> FirstAscensionists(string userId) =>
        f => f.ValidationStatus == ReviewStatus.Approved || f.AddedByUserId == userId;
}
```

Three near-identical expressions rather than one `IQueryable<T> where T : IValidatable, IAuthored` extension, for two reasons: EF Core's translation of member access through a generic interface constraint is not reliably supported (the workaround is `EF.Property<>` string literals, which trades a compile-time check for a runtime one), and plan 10 already declined to introduce an `IAuthored`-style interface. Also note `ClimbingRoute.AddedByUserId` is non-nullable `string` while the other two are `string?` — a shared interface would need a getter-only nullable member and explicit implementations to stay warning-free. Three lines of duplication is the cheaper honesty here.

**Not a global query filter.** EF's `HasQueryFilter` would apply it to writes, cascades, the hierarchy snapshot and the contributions read model too, and would need `IgnoreQueryFilters()` sprinkled through the exceptions — more surface, less obvious, and it can't see the current user without pushing a scoped `ICurrentUser` into `AppDbContext`. Explicit `.Where(ValidationVisibility.Routes(userId))` at each read site is the version a reader can verify.

### 2. Lists gate; details don't

Applied to **list/browse** endpoints only. `GET /routes/{id}`, `GET /first-ascensionists/{id}` and `GET /areas/by-slug-path/*` keep returning the record whatever its status.

Why: those three are how a contributor opens their own pending record, how the moderator's `RouteDetailModal` reviews a route from the queue, and how `RouteFormPage` loads a route for editing. Gating them means bolting the same three-way "mine OR I can validate OR approved" test onto every detail read, and the payoff is nil — this is a private app where every account can already read everything, and a pending record is only reachable by guessing its id. The issue is about *where things are shown*, i.e. lists. That is the line this plan draws, and it's worth writing into `backend/CLAUDE.md` so the next person doesn't "fix" one half of it.

`GET /areas/by-slug-path/*` is a detail read, so the area itself comes back regardless — but its `children` array is a list, so it gets filtered.

Also deliberately **not** filtered: the `FirstAscensionists` collection embedded in `RouteDto`/`RouteDetailDto`. A route's opener credits are part of the route, not a directory listing; blanking a name out of an approved route's credits because the opener record is pending would lose information, and the natural path there — a moderator validating a route whose opener is still pending — is common. Noted as an explicit non-goal below.

### 3. The review queue gets its own endpoints

Two new endpoints, mirroring the `GET /first-ascensionists/pending` shape plan 10 already established:

**`GET /api/routes/pending`** — `[Authorize]`, no policy attribute. Returns `ValidationStatus == Pending` routes narrowed by `areaExpertAuthorizer.BuildAreaCheckAsync(User, Permissions.RouteValidate)`, which is exactly the `r.canValidate` test `PendingTasksContext` runs client-side today. A caller with neither the claim nor an approved expertise gets `[]` (the check degrades to `_ => false`), same as the current UI's empty bell. A dedicated slim DTO, not `RouteDto`:

```csharp
public record RoutePendingDto(int Id, string Name, string AreaPath, string Grade, DateTimeOffset CreatedAt, string AddedByName);
```

**`GET /api/areas/pending`** — `[Authorize(Policy = Permissions.AreaValidate)]`, same as the other area review endpoints.

```csharp
public record AreaPendingDto(int Id, string Name, string NamePath, int? ParentAreaId, DateTimeOffset CreatedAt, string? AddedByName);
```

`NamePath` is computed server-side via `AreaHierarchyService.NamePath` — the same move `ContributionDto` and `RoutePendingDto` make. It's what lets `PendingTasksContext` drop `allAreas`/`areasById` entirely: that map exists solely so `PendingTasksPage` can call `areaPath(a, areasById)` for a pending area's breadcrumb, and it's the reason the bell currently downloads every area in the database. (`areaPath` itself stays — `AreaPicker` still uses it.)

This is a real performance fix, not just plumbing: today every page load, for every moderator, fetches the complete route table with `AddedBy`/`DecidedBy`/`FirstAscensionists`/`Topos` includes and the complete area table, to render a number in a badge.

### 4. Routes auto-validate on create, like areas and first ascensionists

In `RoutesController.CreateRoute`, after the entity is built:

```csharp
if (await areaExpertAuthorizer.CanActOnAreaAsync(User, Permissions.RouteValidate, request.AreaId))
{
    Validation.MarkValidated(route, userId);
}
```

Same rule areas and first ascensionists already apply, with one difference: routes go through `AreaExpertAuthorizer` rather than a bare `User.HasClaim`, because route validation rights are area-scoped — an approved area expert creating a route inside their own area can validate it, and so should not have to file a task for themselves. This mirrors `ValidateRoute`/`RejectRoute`, which already use exactly that guard.

Without this the gate is unusable: a moderator adding a route watches it vanish from "Busca" on save.

### 5. `includeUnvalidated` for the management screens

Four endpoints gain an opt-in query parameter, `?includeUnvalidated=true`, which skips the §1 filter and is refused (`403`) unless the caller holds the matching validate permission:

| Endpoint | Guarded by | Consumer that needs it |
|---|---|---|
| `GET /areas` | `area.validate` | `AreaPolygonModal` (draws every area's polygon on the management map) |
| `GET /areas/roots` | `area.validate` | `AreaTree` on `/manage/areas` |
| `GET /areas/{id}/children` | `area.validate` | `AreaTree` on `/manage/areas` |
| `GET /first-ascensionists` | `first-ascensionist.validate` | `FirstAscensionistsPage` on `/manage/first-ascensionists` |

Both permissions are moderator/admin-only in `RolePermissions`, which is the same audience `ManageLayout` already gates those pages on (`area.edit` and `first-ascensionist.delete` — all three are moderator+). Gating on `.validate` rather than `.edit` is deliberate: `first-ascensionist.edit` is granted *dynamically* to any user linked to their own opener record (`DynamicPermissions`), so using it here would let any linked user enumerate every unvalidated opener.

Opt-in, not opt-out: a new caller that forgets the parameter gets the safe, filtered list. That's the whole reason to make filtering the default rather than adding a `?status=` selector — a selector invites `?status=all` and quietly reopens the hole.

`AreaPicker` and `FirstAscensionistPicker` do **not** pass it. They feed the route form and the search filters, and "approved or mine" is precisely right there: a contributor can file a route into the sector they just created, and nobody else sees it in their dropdown.

### 6. "Mis aperturas" stops filtering client-side

`MyOpeningsPage` (`/my-acordanza/my-openings`) currently downloads every route and keeps the ones where `firstAscensionists.some(fa => fa.userId === id)`. Under the new filter it would show only the user's *approved-or-authored* openings — but it lives inside "Mi acordanza", where the issue says everything shows.

Add `?openedByMe=true` to `GET /routes`: it filters to `r.FirstAscensionists.Any(fa => fa.UserId == callerId)` **and** skips the §1 visibility filter, since these are by definition the caller's own openings. The client-side `.filter()` and the whole-table download go away with it (`listMyOpenings()` in `api/routes.ts`).

Scope note: `openedByMe` bypasses visibility for routes the caller *opened*, which may have been created by someone else and still be pending. That is the intended reading of "Mi acordanza shows everything of mine" — and the caller is credited on the route, so there's nothing to hide from them.

`MyTicksPage` (`GET /ticks/mine`) needs no change: a tick is the caller's own row and the route name comes along with it, inside "Mi acordanza".

### 7. Existing data and the tree

**The tree is not walked.** Visibility is per-record: a route's own status decides it, not its area's; a child area's status decides it, not its parent's. `AreaHierarchyService`'s snapshot stays complete (it resolves name paths, slug paths and descendant sets — an internal resolution structure, not a display list), so a visible child of a hidden parent still renders a correct breadcrumb. The one odd state this permits — an approved route or subarea sitting under a still-pending parent, whose breadcrumb names a sector nobody can navigate to — is a moderator half-finishing a review, resolved by finishing it. Cascading validity down the tree would be a much larger change (and would make one rejection silently unpublish a whole branch); explicitly out of scope.

**Existing rows need a backfill.** See Decisions — this is the one item that must be settled before step 1, because it determines whether the deploy is a no-op or empties the app.

### 8. A visible reason for "why can't anyone else see this?"

Small UI addition so the gate doesn't read as data loss: where a user's *own* pending or rejected record shows up inline in a browse list (their route in "Busca", their sector in the picker), mark it. `RouteCard` gains a `Chip status={route.validationStatus}` when the status isn't `approved` — the `Chip` component already supports exactly the three states and `validationStatusLabels` already carries the Spanish wording. `AreaPicker` appends `validationStatusLabels[...]` to the option label under the same condition.

Cheap, and it turns "my route is missing for my friend" into a self-explaining state. If it turns out to clutter the card, it can be dropped without touching anything else.

## Steps

1. **Decide the backfill** (see Decisions). Everything else assumes it's settled.
2. **`Models/ValidationVisibility.cs`** — the three predicates, plus the header comment stating the rule and the lists-gate-details-don't line from §2.
3. **`RoutesController`** — `.Where(ValidationVisibility.Routes(userId))` on `GetRoutes` and `GetGrades`; the `openedByMe` branch (§6) short-circuiting that filter; auto-validate in `CreateRoute` (§4); new `GET /routes/pending` + `RoutePendingDto` (§3). `GetRoute` untouched.
4. **`AreasController`** — the filter on `GetAreas`, `GetRootAreas`, `GetAreaChildren` and on `GetAreaBySlugPath`'s `children` projection; `includeUnvalidated` on the first three (§5); new `GET /areas/pending` + `AreaPendingDto` (§3).
5. **`FirstAscensionistsController`** — the filter on `GetFirstAscensionists` and `GetFirstAscensionistProfiles`; `includeUnvalidated` on the former (§5). `GetFirstAscensionist` and the existing `/pending` untouched.
6. **Regenerate the contract.** `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` (Postgres + Azurite up, `Jwt:Key` set), then `bun run generate-api` in `frontend/`; commit both diffs or CI's `openapi-contract-check` fails.
7. **Frontend api layer** — `listPendingRoutes()`, `listMyOpenings()` (`api/routes.ts`); `listPendingAreas()` and an `includeUnvalidated` argument on `listAreas`/`listRootAreas`/`listAreaChildren` (`api/areas.ts`); `includeUnvalidated` on `listFirstAscensionists` (`api/firstAscensionists.ts`).
8. **Frontend callers** — `PendingTasksContext` onto the two new endpoints, dropping `allAreas`/`areasById` from state and from the context value; `PendingTasksPage` using `a.namePath` (drops the `areaPath`/`areasById` usage and the `AreaSummary` import for pending areas); `MyOpeningsPage` onto `listMyOpenings()`; `AreaTree`, `AreaPolygonModal` and `FirstAscensionistsPage` passing `includeUnvalidated`.
9. **§8's status chips** — `RouteCard`, `AreaPicker`.
10. **Backend tests.** New `Controllers/ValidationVisibilityTests.cs` (integration): for each of routes/areas/first ascensionists — approved is visible to a stranger, pending/rejected is not, pending/rejected *is* visible to its author; `includeUnvalidated` returns everything for a moderator and `403`s for a plain user; `GET /routes/pending` returns only pending routes and is area-scoped for an area expert (`TestUsers` already seeds that variant); `GET /areas/pending` `403`s without `area.validate`. Plus a row per new endpoint in `Authorization/AuthorizationMatrixTests.cs`, and a `RouteValidationLifecycleTests` case for §4's auto-validate-on-create.
    **`SeedData` needs a `validated: true` default** on `CreateRouteAsync` and `CreateFirstAscensionistAsync` (matching `CreateAreaAsync`, which already has one) — otherwise every existing test that asserts on `GET /routes` starts failing for the wrong reason. Do this first in the step; the failures it prevents are noise, but the parameter it adds is what the new tests seed pending rows with.
11. **Frontend tests.** `SearchPage.test.tsx` needs no new case (it mocks the endpoint, which is where the filtering now happens) — but add: a `PendingTasksContext` test that the two new endpoints back the badge (MSW's `onUnhandledRequest: 'error'` will already fail the old handlers, which is the point), and a `RouteCard` test that the pending chip renders for a non-approved route and not for an approved one.
12. **Docs.** Root `CLAUDE.md`: a line in Architecture that validation gates list visibility outside "Mi acordanza", with the approved-or-mine rule. `backend/CLAUDE.md`: `ValidationVisibility`, the two new pending endpoints, `includeUnvalidated`, `openedByMe`, routes now auto-validating on create, and the lists-gate/details-don't boundary. `frontend/CLAUDE.md`: `PendingTasksContext` no longer downloading every route and area, the management pages' opt-in, `MyOpeningsPage`'s server-side filter.
13. **Verify** with the `run-acordanza` skill: as a `user`, create a sector + a route in it and confirm both are visible to that user (chipped as pending) and absent for a second plain user; as a `moderator`, see both in the queue, validate the sector, reject the route with a reason; back as the user, read the reason on `/my-acordanza/contributions`, edit the route, watch it return to pending and reappear in the moderator's bell; as the moderator, create a route and confirm it's immediately visible in "Busca" (§4).

## Decisions to confirm before starting

- **The backfill (blocking).** Recommended: a one-off `migrationBuilder.Sql` in an empty migration setting `ValidationStatus = 'Approved'` (leaving `DecidedAt`/`DecidedByUserId` null, so nobody is credited with a decision they didn't make) on every `core."Routes"`, `core."Areas"` and `core."FirstAscensionists"` row that exists at deploy time. Rationale: plan 10's backfill only approved rows with a recorded decision, so essentially the entire pre-existing catalogue is `Pending` today — shipping the gate without this hides almost everything and dumps hundreds of rows into a moderator's queue, which is a data-loss-shaped experience even though nothing is lost. The alternative (ship it, let moderators sweep the queue) is defensible only if the current pending count is genuinely small; worth checking against production before choosing.
- **Moderators see the same browse lists as everyone else** (§1) — no "or I can validate" clause. Review lives in `/manage/pending-tasks`.
- **Detail-by-id endpoints stay open** (§2). A pending record is reachable by direct id/slug URL, by anyone with an account.
- **A route's embedded opener credits are never filtered** (§2), even when the opener record is pending or rejected.
- **Rejected records behave exactly like pending ones** for visibility — hidden from others, visible to their author. The issue lists them together ("Not validated items, accepted and rejected can be shown in the Contribuciones section"), so this needs no separate handling, but it does mean a rejected sector keeps appearing in its author's own pickers until they fix or delete it.
- **Visibility is per-record, not inherited down the area tree** (§7).
