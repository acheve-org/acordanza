# Revamp route display: denser cards, a sectioned detail modal, a shareable route URL

Status: planned (not started)
Issue: not filed yet — file one and link it here before the `plan:` commit
Depends on: [16](16-route-new-fields.md) (`Length`/`ObligatoryGrade`/`RecommendedGear`/`OptionalGear`/`Links` already exist on `RouteDto`/`RouteDetailDto` and the create/edit form, but were deliberately never rendered anywhere — "route information will be revamped in a following issue". This is that issue) and [17](17-grade-scale-and-pills.md) (`gradeBand`, the French-scale-to-color-band function this reuses for the card's left accent). Backend: one new `ClimbingRoute.Slug` column + migration + a `by-slug` endpoint, mirroring [13](13-first-ascensionist-slug.md)'s `FirstAscensionist.Slug`. Frontend: `RouteCard`/`TickedRouteCard`/`DetailModal` visual rework, plus a routing change that consolidates three pages' separately-wired detail-modal logic into one shared, URL-addressable overlay.

## Problem

Two independent complaints about how a route is shown today, both real:

1. **`RouteCard` (`frontend/src/components/RouteCard.tsx`) is sparse and low-signal.** It shows grade, name, area path, a gear label, a topo/tick presence icon, and (when non-approved) a status chip — nothing else. Orientation, length, and who opened it are all on the DTO already (plan 16) and never surface until the modal opens. `padding: var(--space-lg)` (16px) plus a `gap: var(--space-xs)` between four sparse rows reads as too much white space for how little information is there.
2. **`DetailModal` (`frontend/src/components/DetailModal.tsx`) is one undifferentiated scroll.** Header → description → first-ascensionist line → divider → topo grid → divider → tick button, with no visual grouping. Worse, it's missing content that already exists on `RouteDetail`: `length`, `obligatoryGrade`, `recommendedGear`, `optionalGear`, and `links` are fetched but never rendered anywhere in the app — the exact gap plan 16 named and deferred.

A third piece, added at the requester's direction rather than found in the code: routes have no shareable URL today. `DetailModal` opens as pure client-side state (`detailRouteId`) on whichever list page is behind it (`SearchPage`, `AreaPage`, `FirstAscensionistPage`), so a specific route can't be linked, bookmarked, or reached with the browser back button — unlike `Area` (`/areas/*`) and `FirstAscensionist` (`/fa/:slug`), both of which already have slugs and their own URLs.

## Design

### 1. `RouteCard`: hierarchy, not just more fields

Confirmed field priority (highest to lowest): **name + area path** → **grade + gear** → **orientation + length** (new) → topo/tick icons (de-emphasized, not removed — `hasTopos`/`hasTick` stay booleans on `RouteSummary`, no image fetch added to the list view per the "keep it icon/text-only" call). Status chip stays, unconditionally on non-approved routes as today.

```
┌─ 6b+ ───────────────────────────  ⛰ ✓ ┐   ← grade pill (colored by band) · icons, de-emphasized
│  Fisura del Diablo                     │   ← name, primary
│  Riglos · Peña Amarillo                │   ← area path, primary
│  Sur · 28 m · Equipada                 │   ← orientation · length · gear, one secondary line
└─────────────────────────────────────────┘
```

- **Left accent band becomes grade-colored, not static teal.** Today `.route-card { border-left: 3px solid var(--accent-2) }` — a fixed teal regardless of grade. `RouteCard` imports `gradeBand` from `acordanza-design-system` (already exported, `design-system/src/index.ts:21`, the same function `GradePill` uses internally) and applies a `grade-band-{band}` class the way `GradePill.css` already does, reusing the existing `--grade-6`/`--grade-7`/`--grade-8-9` tokens for the border color. Band `'1-5'`/`null` (ungraded or below-6a) keeps today's `--accent-2` teal as the default — so easy routes and non-French-scale grades look exactly as they do now, and only 6th/7th/8th-9th grade routes gain a new color, at zero new token cost.
- **Orientation + length join the existing gear line.** `[orientation && orientationLabels[...], length && `${length} m`, gear && gearLabels[...]].filter(Boolean).join(' · ')` on one `.rc-meta` line — same join-and-filter idiom `DetailModal`'s `.detail-fa` orientation/gear line already uses, so no new pattern.
- **Tighten spacing.** Drop `.route-card`'s `padding` from `--space-lg` (16px) to `--space-md` (14px) and its `gap` from `--space-xs`... actually the fix here is less about the token values (which are already the smallest in the scale below `--space-lg`) and more about **row count**: today's 4 stacked flex children (`rc-top`, `rc-main`, chip, `rc-type`) become 3 (`rc-top` grade+icons, `rc-main` name+area, one meta line folding gear into it) — one fewer internal gap. `--space-xs` gap stays; measure against `run-acordanza` screenshots before tuning further, since "too much white space" is a visual judgment call best confirmed by looking at the rendered card, not by picking token values blind.

### 2. `TickedRouteCard`: brought in line

`TickedRouteCard` (`frontend/src/components/TickedRouteCard.tsx`) shares `.route-card`/`.rc-top`/`.rc-main`/`.rc-type` with `RouteCard` already, so the grade-band left accent and tightened spacing apply automatically once §1's CSS lands — no separate visual work needed there. It gains the same orientation+length meta line (`TickedRoute`/`MyTickDto` already carries `orientation`? — **check**: if `MyTickDto` doesn't carry `orientation`/`length` today, that's a DTO gap this plan doesn't create scope to fix; in that case `TickedRouteCard`'s meta line stays gear-only and the visual consistency (band color, spacing) is what "bring it in line" delivers, not full field parity). The delete button (`RemoveButton`) and the tick-specific `.trc-tick` date/rating/notes row are untouched — those aren't part of what's "too sparse" here.

### 3. `DetailModal`: sectioned single column, no tabs

Same `Modal` wrapper, same overall scroll — restructured into clearly separated blocks instead of one undifferentiated flow, each visually grouped (heading + hairline-bordered block, the same `{radius.md}`/hairline-border language `.route-card` already uses, not a new visual primitive):

```
┌────────────────────────────────────────────┐
│ Fisura del Diablo                [actions] │  ← detail-head, unchanged
│ 6b+  ·  Riglos › Peña Amarillo              │
│ Sur · Equipada                              │
│ ✓ Validada por … / ✗ Rechazada por …        │
├────────────────────────────────────────────┤
│  Grado 6b+   Long. 28 m   Orient. Sur   Eq. │  ← NEW stats row — length/obligatoryGrade
│  Grado obligatorio 6a (if set)              │    folded in here instead of prose
├────────────────────────────────────────────┤
│  Descripción text…                          │  ← unchanged, own block
├────────────────────────────────────────────┤
│  Material recomendado: …                    │  ← NEW — recommendedGear/optionalGear,
│  Material opcional: …                       │    only rendered if present
├────────────────────────────────────────────┤
│  Enlaces: [link] [link]                     │  ← NEW — route.links, was fetched, never shown
├────────────────────────────────────────────┤
│  Apertura: Fulano, Mengano                  │  ← unchanged content, own block now
├────────────────────────────────────────────┤
│  Topos                          [+ Añadir]  │  ← unchanged
│  [img] [img] [img]                          │
├────────────────────────────────────────────┤
│  [+ Registrar ascenso]                      │  ← unchanged, explicitly kept write-only
└────────────────────────────────────────────┘
```

- **No tick history, no aggregate ratings** — per direction received, ticks stay private-per-user and the modal stays a write-only "log your ascent" action, exactly as today. Nothing about `AddTickModal` or the tick button's wiring changes.
- **A new "stats row"** is the one genuinely new layout element: a small horizontal `label/value` group for grade, length, orientation, gear, and (if present) obligatory grade — replacing today's plain-text `.detail-fa` orientation/gear join for these fields, but keeping the same join-and-filter approach for whichever of the five are actually present (all but `length`/`grade` are optional on the DTO).
- **`links`** (`List<string>`, plan 16) renders as a small list of `<a target="_blank" rel="noreferrer">` — same external-link pattern the topo thumbnails already use — under its own heading, omitted entirely when empty.
- **`recommendedGear`/`optionalGear`** are free-text strings (not enums — see backend `CLAUDE.md`'s note that `Orientation`/`Gear` are "free-form-ish"), rendered as plain labeled text, each line omitted when empty.
- CSS: extend `styles.css`'s existing `.detail-*` rules with a `.detail-stats`/`.detail-section` pair rather than inventing new class families — the existing `hr.divider` between "Topos" and the tick button either stays or is replaced by `.detail-section` borders consistently; pick one divider language and use it for every section, not both.

### 4. A shareable, slug-based route URL

**`ClimbingRoute` gains a persisted `Slug` column, scoped per-area — matching how `Area.Slug` is scoped per-parent, not `FirstAscensionist.Slug`'s flat global namespace.** A single global slug namespace across every route in the app was the initial idea and is worth rejecting explicitly: `ClimbingRoute.Name` has no uniqueness constraint at all today (not even per-area), so a global slug would hit the `-2`/`-3` suffix loop constantly on any common route name ("Directa", "Fisura", "Vía Nueva" — the kind of name that recurs across dozens of unrelated crags) and produce noisy, meaningless suffixes. Scoping to the route's area fixes both problems at once: collisions only have to be disambiguated among an area's own routes (typically a handful to a few dozen), and the resulting URL is a real path rather than an opaque slug-plus-suffix.

- **URL shape**: `/routes/{area-slug-path}/{route-slug}`, e.g. `/routes/riglos/pena-amarillo/fisura-del-diablo` — the same `{area-slug-path}` `AreaHierarchyService` already computes (`AreaHierarchy.SlugPath(areaId)`, the exact method `RoutesController.UploadTopo` already calls for the topo blob prefix, `backend/Acordanza.Api/Controllers/RoutesController.cs:452`) plus the route's own leaf `Slug`. Nothing about the area-path segment is stored on `ClimbingRoute` — it's resolved live from `AreaId` via the existing hierarchy snapshot, the same way `Area`'s own multi-segment breadcrumbs are never stored, only each node's own `Slug` is. This also means **moving a route to a different area "just works"**: the stored `Slug` doesn't change, but the next time its URL is built (client-side, from `AreaSlugPath` + `Slug`) it reflects the new area automatically — no migration, no redirect table, mirroring how `Area`'s own path already updates automatically when a subtree is reparented.
- **Uniqueness**: `(AreaId, Slug)` unique index, and `GenerateUniqueRouteSlugAsync(areaId, name, excludeId?)` scopes its `-2`/`-3` collision loop to `AreaId` — copied from `AreasController.GenerateUniqueAreaSlugAsync(parentAreaId, name)`, not `FirstAscensionistsController`'s flat version. On `UpdateRoute`, if `AreaId` changes, the slug is regenerated against the *new* area's sibling set too (a route moved into an area that happens to already have a same-named route still needs the suffix).
- **Regenerated on rename, not frozen at creation** — this still follows `FirstAscensionist`, not `Area`, and needed checking carefully: `Area.Slug` freezes forever specifically because it doubles as the topo blob-storage path prefix, and changing it would orphan uploaded images. Route topo blob names *look* slug-shaped (`{area-slug-path}/{route-slug}-{NN}.{ext}`) but are actually computed fresh at upload time from `Slugify.ToSlug(route.Name)` directly — there is no persisted `Route.Slug` today, and this plan's new column is not wired into that blob-naming code at all (leave `UploadTopo`'s `blobPrefix` line untouched). So nothing is keyed on the new column, and it can safely regenerate whenever `Name` (or `AreaId`) changes.
- Migration: add the column + a raw-SQL backfill (lowercase, accent-strip via `translate()`, `regexp_replace` to collapse non-alphanumerics, `ROW_NUMBER() OVER (PARTITION BY "AreaId" ORDER BY "Id")` dedupe suffixing — partitioned, unlike `FirstAscensionist`'s ungrouped version) + the composite unique index — same shape as `20260808140244_AddFirstAscensionistSlug.cs` and `20260804110308_AddAreaSiblingSlugUniqueness.cs` combined.
- New `GET /routes/by-slug-path/{*path}` (named to match `GET /areas/by-slug-path/{*path}`, not `FirstAscensionist`'s flat `by-slug/{slug}`) returning `RouteDetailDto`: split the trailing segment off `path` as the route slug candidate, resolve the remaining segments to an area via `AreaHierarchy.ResolveSlugPath` (the same method `AreasController`'s equivalent action already uses), then look up the route by `(AreaId, Slug)`. Alongside, not replacing, the existing numeric `GET /routes/{id}` — `RouteFormPage`'s edit route (`/routes/:id/edit`) keeps using the numeric id, only the *viewing* path moves to the slug path.
- Both `Slug` and a new `AreaSlugPath` (the pre-computed `AreaHierarchy.SlugPath(route.AreaId)` string) are added to `RouteDto` and `RouteDetailDto` — every list card needs both to build the full detail-link path client-side, so they travel on the summary shape, not just the detail one (unlike `FirstAscensionist.Slug`, which only lives on the profile DTO since that page doesn't need to construct links from a list card).

**Frontend routing: one shared, URL-backed overlay, replacing three separate copies of the same wiring.** Today `SearchPage`, `AreaPage`, and `FirstAscensionistPage` each independently hold `detailRouteId` state, fetch the route, and wire up all six mutations (delete/validate/reject/tick/topo-upload/topo-delete) to their own `DetailModal` instance — one via react-query (`SearchPage`), two by hand-rolled `useState`/`useEffect` (`AreaPage`, `FirstAscensionistPage`). This plan collapses all three into one:

- `App.tsx` adopts the standard react-router "background location" pattern: render the normal route tree against `location.state?.backgroundLocation ?? location`, and — only when a `backgroundLocation` is present in state — render a second, small `<Routes>` on top matching `routes/*` (a splat, since the path now carries a variable number of area segments plus the route slug) to a new `RouteDetailOverlay` component. `RouteDetailOverlay` reads the full match via `useParams()['*']`, is the *only* place `DetailModal` is ever rendered from now on, owns the by-slug-path fetch and all six mutations, and closes via `navigate(backgroundLocation)`.
- Every card click, in all three list pages, becomes `navigate(`/routes/${route.areaSlugPath}/${route.slug}`, { state: { backgroundLocation: location } })` instead of `setDetailRouteId(id)` — `RoutesList`'s `onSelect` prop changes from `(id: number) => void` to `(route: RouteSummary) => void` so `areaSlugPath`/`slug` are available at the call site.
- **`routes/*` vs. the existing static routes.** React Router ranks static/dynamic segments above splats, so `routes` (exact, `SearchPage`'s list), `routes/new`, and `routes/:id/edit` all keep matching before `routes/*` is ever tried — no route needs reordering, `routes/*` just catches everything else under the prefix.
- **Fresh-visit fallback** (a pasted link, a reload, no `backgroundLocation` in history state): the normal route tree needs its own match too, so `<Route path="routes/*" element={<SearchPage />} />` joins the existing `<Route path="routes" element={<SearchPage />} />`. `SearchPage` reads `useParams()['*']` and, if non-empty on mount with no `backgroundLocation` already in state, immediately `navigate('/routes/' + splat, { replace: true, state: { backgroundLocation: { pathname: '/routes' } } })` — synthesizing the overlay state once so `RouteDetailOverlay` is still the only component that ever opens the modal.
- Net effect on `SearchPage`/`AreaPage`/`FirstAscensionistPage`: each **loses** its `detailRouteId`/`detailRoute` state, its six mutations, and its `<DetailModal .../>` block — they keep only their list-fetch/filter logic and an `onSelect` that navigates. This is a simplification, not just plumbing to support the URL — three copies of the same wiring become one.

### Non-goals

- **No tick history or social features in the modal** — explicitly ruled out; ticks stay private-per-user, the modal stays write-only.
- **No topo thumbnails on cards** — explicitly ruled out; `hasTopos` stays a boolean icon.
- **No change to `RouteFormPage`/`/routes/:id/edit`** — still id-based, untouched.
- **No change to topo blob naming** — the new `Route.Slug` column is not read by `UploadTopo`.
- **No tabs in the modal** — sectioned single column only.

## Steps

1. **Backend.** `Models/ClimbingRoute.cs` (`Slug` property, doc comment referencing the per-area-scoped, regenerate-on-rename-or-move rationale), `Data/AppDbContext.cs` (`HasIndex(r => new { r.AreaId, r.Slug }).IsUnique()`, mirroring `Area`'s `(ParentAreaId, Slug)` index), migration (column + partitioned backfill + composite index, modeled on `20260808140244_AddFirstAscensionistSlug.cs` + `20260804110308_AddAreaSiblingSlugUniqueness.cs`), `RoutesController` (`GenerateUniqueRouteSlugAsync(areaId, name, excludeId?)`, wired into `CreateRoute` and, on name *or* area change, `UpdateRoute`; new `GET /routes/by-slug-path/{*path}` action using `AreaHierarchy.ResolveSlugPath`), `Dtos/RouteDtos.cs` (`Slug` + `AreaSlugPath` on `RouteDto`/`RouteDetailDto`).
2. **Regenerate the OpenAPI contract** (`ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true`, Postgres+Azurite up, `Jwt:Key` set) then `bun run generate-api` in `frontend/`; commit both diffs.
3. **`RouteCard.tsx`** — grade-band left accent (`gradeBand` import), orientation+length in the meta line, layout/spacing per §1.
4. **`TickedRouteCard.tsx`** — inherits shared CSS automatically; confirm no visual regression in the tick/date/rating row.
5. **`DetailModal.tsx`** — stats row, links section, recommended/optional gear section, per §3; `styles.css`'s `.detail-*` rules extended accordingly.
6. **Routing.** `App.tsx` (background-location split, `routes/*` splat route), new `components/RouteDetailOverlay.tsx` (the consolidated fetch-by-slug-path + six mutations + `DetailModal`), `RoutesList.tsx` (`onSelect` signature change), `SearchPage.tsx`/`AreaPage.tsx`/`FirstAscensionistPage.tsx` (strip local detail-modal state/mutations, wire `onSelect` to `navigate`).
7. **Tests** (below).
8. **Docs.** Root `CLAUDE.md`: no change (route slugs aren't part of the four-section table). `backend/CLAUDE.md`: `ClimbingRoute` bullet gains its per-area-scoped `Slug` sentence (contrasted with `Area`'s and `FirstAscensionist`'s), `RoutesController` bullet gains `GET /routes/by-slug-path/{*path}`. `frontend/CLAUDE.md`: `RouteCard`/`DetailModal` bullet updated for the new fields and the routing consolidation; the "Modal/dropdown data is fetched by the thing that needs it, when it opens" rule gets a note that `RouteDetailOverlay` is the sole owner of route-detail fetching now.
9. **Verify** with the `run-acordanza` skill: open a route from each of the three contexts (Busca, an area page, an aperturista page) and confirm the URL updates to `/routes/{area-slug-path}/{route-slug}`, the underlying list/filters/scroll stay intact, browser back closes the modal, and a hard reload on that URL renders Busca underneath with the modal open. Screenshot cards across a few grade bands to confirm the left-accent color and spacing read cleanly, and one modal with all-new fields populated (length, obligatory grade, gear notes, links) plus one with them empty (confirming sections cleanly omit).

## Testing

**Backend** (`RoutesTests.cs`, extend existing):
- Slug generated on create, collision within the same area suffixed (`-2`); the same name in a *different* area does **not** get suffixed (proves the scoping is per-area, not global).
- Regenerated on rename; regenerated (against the new area's sibling set) when a route is moved to a different area; unchanged on a no-op re-save (`excludeId` guard).
- `GET /routes/by-slug-path/{*path}` resolves a full `{area-slug-path}/{route-slug}` correctly, including a multi-level area path; unknown area path, unknown route slug within a valid area, and a route-slug-only path with no area segments all → 404.
- `RouteDto`/`RouteDetailDto` responses include `slug` and `areaSlugPath`.

**Frontend:**
- `RouteCard.test.tsx` (extend) — grade-band class applied per grade string (reuse `gradeBand`'s existing test cases as the source of truth), orientation/length rendered when present and omitted when absent.
- `DetailModal.test.tsx` (extend) — stats row, links, gear-notes sections render when populated and are absent when not; existing validate/reject/edit/delete/topo/tick permission-matrix assertions untouched.
- New `RouteDetailOverlay.test.tsx` — the six mutations (delete/validate/reject/tick/topo-upload/topo-delete) fire against the right endpoint and close/invalidate correctly; this is the test that used to be split (differently) across `SearchPage.test.tsx`/`AreaPage.test.tsx`/`FirstAscensionistPage.test.tsx` and now has one home.
- `SearchPage.test.tsx`/`AreaPage.test.tsx`/`FirstAscensionistPage.test.tsx` — updated to assert `onSelect` navigates to `/routes/{areaSlugPath}/{slug}` with background-location state, rather than opening a local modal.
- One test exercising the fresh-visit fallback: render at a `/routes/{areaSlugPath}/{slug}` URL directly (no router state), assert `SearchPage` renders and the overlay opens.

`dotnet test Acordanza.slnx` from the repo root (Docker up) and `bun run test` + `bun run build` in `frontend/`.

## Decisions to confirm before starting

- **Route slugs are scoped per-area (`(AreaId, Slug)`), matching `Area`'s own sibling scoping, not `FirstAscensionist`'s flat global namespace** (§4) — chosen specifically to avoid a global slug hitting constant `-2`/`-3`/`-7` suffixes on common route names. One consequence worth confirming: a route's URL changes silently if it's moved to a different area (no redirect from the old path) — this mirrors how `Area` itself already behaves when a subtree is reparented, so it's consistent with the rest of the app, but it does mean an old shared link to a moved route 404s rather than redirecting. If a stable-forever URL matters more than a readable, area-scoped one, the alternative is a flat global slug (back to the original draft of this section) with the collision-noise tradeoff accepted instead.
- **Route slugs regenerate on rename or on area move** (§4) — confirmed safe since topo blob naming doesn't read the persisted slug, it recomputes from `route.Name` at upload time. If that changes later (e.g. blob naming migrates onto the persisted `Slug` for consistency with `Area`), this decision would need revisiting together with a blob-rename migration.
- **The background-location routing consolidation** (§4) is a bigger frontend change than "just" adding a URL — it deletes duplicated modal-wiring code from three pages in favor of one shared component. If a smaller, purely additive change is preferred instead (e.g. each page keeps its own modal wiring and only gains a URL sync effect), say so — it avoids touching `AreaPage`/`FirstAscensionistPage`'s existing hand-rolled fetch pattern, at the cost of keeping three copies of the same six mutations.
