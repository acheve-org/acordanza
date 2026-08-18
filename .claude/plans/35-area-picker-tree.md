# Make `AreaPicker` a tree you can browse *and* filter

Status: done — implemented across three PRs as planned: backend [#223](https://github.com/acheve-org/acordanza/pull/223), design-system `TreeSelect` [#224](https://github.com/acheve-org/acordanza/pull/224), `AreaPicker` rewrite [#225](https://github.com/acheve-org/acordanza/pull/225).
Depends on: [11](11-show-only-validated-items.md) for the approved-or-mine visibility rule the new endpoint must honour; [25](25-finish-react-query-migration.md) for the query-key/invalidation discipline; [28](28-orientation-compass-rose.md) as the shape this copies — a generic, data-agnostic component in the design system plus a thin app-side wrapper that owns fetching, permissions and Spanish domain labels. One new backend endpoint, no migration, no data-model change.

## Problem

`AreaPicker` presents a **tree as a flat list of strings**. `AreaPicker.tsx:35` calls `GET /areas`, gets every area back unordered, rebuilds the hierarchy client-side into `Padre › Hijo › Nieto` breadcrumbs (`areas.ts:86-94`), and renders them as one long `Combobox` list. Four things are wrong with that, and they compound at the real scale of ~300 root zones with 2–5 subsectors each (≈900–1800 nodes):

- **There is no way to browse.** The only affordance is typing. A user who doesn't already know a crag's name cannot discover it here, even though the data is a tree and the tree is exactly the thing that would let them.
- **The payload carries geometry nobody in this component uses.** `AreaDto` (`AreaDtos.cs:5-16`) includes the full `Polygon` vertex array plus `CentroidLat`/`CentroidLng`, `DecidedAt`, `DecidedByName` and `RejectionReason`. The picker reads `id`, `name`, `parentAreaId` and `validationStatus`. At 1800 areas that's a payload dominated by vertices for a dropdown that draws no map.
- **Search is a naive substring over the flattened breadcrumb** (`AreaPicker.tsx:47`) with no diacritics folding, so in a Spanish-only app `pena` misses `Peña` and `rua` misses `Rúa` — the two most likely things anyone types.
- **The create-new form is already broken at this scale.** `AreaPicker.tsx:93-100` renders a `<select>` of *every* area as the parent chooser. That's 1800 `<option>` elements in one native select.

Two smaller things fall out of the same rewrite: the `refreshToken` prop (`AreaPicker.tsx:19`) is passed by none of the four call sites and is dead, and `Combobox` — which `AreaPicker` and `FirstAscensionistPicker` share — has **no keyboard model at all** (no `role="combobox"`, no `role="listbox"`, no `aria-activedescendant`, no arrow keys; options are `<button>`s reached by Tab). Tabbing through a tree of 1800 nodes is not a keyboard model.

There is also a **semantic split the component doesn't express**. Its four call sites mean two different things:

| Call site | Meaning | Backend behaviour |
|---|---|---|
| `Filters` (`SearchPage`), `MyTicksPage` | *filter* by area, clearable | **expands to descendants** — `RoutesController.cs:73`, `TicksController.cs:48` |
| `RouteFormPage`, `MyImportsPage` | *assign* the exact `AreaId` | exact node, no expansion |

A user filtering by «Peña Rúa» today gets every route in its subsectors and is told nothing about it.

## Design

Three pieces in dependency order: one new endpoint (§1), one new design-system primitive (§2), and the rewritten `AreaPicker` on top of it (§3), then the call sites (§4).

The governing decision is that **the client holds the whole tree**. At ~1800 nodes projected to `{id, name, parentAreaId, routeCount, validationStatus}` that's roughly 70–180 KB raw and **20–40 KB gzipped**, fetched once per session and shared by all four pickers. Every subsequent interaction — expand, collapse, filter, breadcrumb, subtree roll-up — is in-memory and costs zero requests. That is what makes "browse root→leaf" and "find a leaf by name" the same widget instead of two, and it's why lazy per-level loading (`/areas/roots` + `/areas/{id}/children`, as `AreaTree` uses) is the wrong trade here: it pays a round trip per expansion and still can't answer a name query without a second endpoint.

### 1. Backend — `GET /areas/tree`

New action on `AreasController`, beside the existing `roots`/`children`/`pending` literals (no route conflict — there is no bare `GET /areas/{id}`):

```csharp
public record AreaTreeNodeDto(int Id, string Name, int? ParentAreaId, int RouteCount, string ValidationStatus);

[HttpGet("tree")]
public async Task<ActionResult<List<AreaTreeNodeDto>>> GetAreaTree()
```

**Flat, not nested.** The client builds its own index once; a nested JSON tree just repeats structure the `parentAreaId` column already encodes and makes the response harder to diff and to type.

**Filtered by `ValidationVisibility.Areas(callerId)`** — approved-or-mine, the same rule every other browse list gets (plan 11). This is what keeps today's `(Pendiente de validación)` suffix behaviour honest: a caller only ever sees their *own* pending/rejected areas here, so the marker means "why is this one flagged?" rather than acting as a moderation cue.

**No `includeUnvalidated` parameter.** The picker never needs it, and every parameter not added is a `403` branch not written and not tested. The management screens (`AreaTree`, `AreaPolygonModal`) keep `roots`/`children`/`GET /areas` and their existing opt-in.

**`RouteCount` is the area's own routes only**, not its subtree. The client has the whole tree, so it rolls subtree totals up itself in one pass — there is no reason to make Postgres compute what a single client-side traversal already gives for free. The count is itself filtered by `ValidationVisibility.Routes(callerId)`, so it never promises routes the caller can't open.

**Two queries, fixed, regardless of tree size** — the areas, then one grouped count, joined in memory:

```csharp
var userId = userManager.GetUserId(User)!;

var areas = await db.Areas.AsNoTracking()
    .Where(ValidationVisibility.Areas(userId))
    .OrderBy(a => a.Name)
    .Select(a => new { a.Id, a.Name, a.ParentAreaId, a.ValidationStatus })
    .ToListAsync();

var counts = await db.Routes.AsNoTracking()
    .Where(ValidationVisibility.Routes(userId))
    .GroupBy(r => r.AreaId)
    .Select(g => new { AreaId = g.Key, Count = g.Count() })
    .ToDictionaryAsync(x => x.AreaId, x => x.Count);
```

A correlated subquery in the `Select` would also be one round trip but a per-row subquery in SQL; the grouped dictionary is provably two statements and reads as such. `Routes.AreaId` is already indexed (`AppDbContext`).

**Update the stale comment at `AreasController.cs:82-86`**, which currently cites `AreaPicker` as a reason `GET /areas` stays unpaginated and polygon-laden. After this it's justified by `AreaMap` and `AreaPolygonModal` alone — the two callers that genuinely need geometry.

Then regenerate the OpenAPI document and `frontend/src/api/generated.ts`; CI's `openapi-contract-check` fails otherwise.

### 2. `design-system/src/components/TreeSelect/`

A generic, data-agnostic tree combobox. It knows nothing about areas, fetching, Spanish, or permissions — it takes flat nodes and renders a filterable tree.

```
TreeSelect.tsx    the component: popup, pruning, keyboard, roles, 720px sheet swap
treeFilter.ts     pure — buildIndex, normalise, matches, prune
TreeSelect.css
```

The `TreeSelect.tsx` + `treeFilter.ts` split mirrors `CompassPicker.tsx` + `compass.ts` exactly: the domain-free logic that can be pinned with cheap unit tests lives beside the component, not inside it.

```ts
export interface TreeSelectNode {
  id: number
  parentId: number | null
  label: string
  /** Rendered right-aligned on the row, e.g. a route count. */
  meta?: ReactNode
  /** Appended to the accessible name and rendered after the label, e.g. a validation status. */
  suffix?: string
  /** Rendered dimmed — e.g. an area whose whole subtree has no routes. */
  dimmed?: boolean
}

export interface TreeSelectProps {
  nodes: TreeSelectNode[]
  value: number | null
  onChange: (id: number) => void
  placeholder: string
  /** Shown in the input while closed — the caller formats it (e.g. a breadcrumb). */
  closedValue?: string
  /** Rows shown when the query is empty, before "Ver todas". */
  headNodes?: TreeSelectNode[]
  headEmptyHint?: ReactNode
  /** Pinned above everything, e.g. "Cualquier sector". */
  leadingOption?: { label: ReactNode; onSelect: () => void }
  /** Renders a "+" affordance per row and one at the foot; parent is implied by position. */
  onCreateChild?: (parentId: number | null) => void
  loading?: boolean
  onOpen?: () => void
}
```

**Interaction model — tree-first, filter-in-place.** The popup always shows a tree, not a list. Typing prunes *that same tree*: matching nodes are kept, their ancestors are kept as context and auto-expanded, everything else is dropped. Clearing the box restores the previous browse state. Discovery and find are literally the same view, which is the whole point — there is no mode switch to get wrong.

**The empty-query state is not "all 300 roots".** A 300-row scroll wall is not browsing. Instead:

- `headNodes` — up to 8 rows the caller considers most likely (see §3: recents, padded by route count), each with its breadcrumb.
- Below them, «Ver todas las zonas», which expands the full root list **grouped alphabetically** with sticky letter headers.

At 300 roots, "navigate root→leaf" stops being how anyone finds a *known* crag and becomes how they discover an *unknown* one — and discovery is served far better by 8 relevant rows than by 300 alphabetical ones.

**Matching rules** (`treeFilter.ts`, all pure):

- **Diacritics folded** via `normalize('NFD')` + stripping combining marks, no dependency. `pena` matches `Peña`. Non-negotiable in a Spanish-only app.
- **Matched against the node's own name**, not its breadcrumb. Breadcrumb matching would make `rúa` return five subsectors that all matched on the same ancestor text.
- **A matching parent shows its children expanded beneath it** — so typing `rúa` still surfaces Peña Rúa's sectors, without the noise.
- **Prefix-per-word, falling back to substring**: `sec nor` matches «Sector Norte». At ≤1800 nodes the cost is nil.
- **Ancestor context rows stay selectable**, just visually de-emphasized. An ancestor is a legitimate answer (any node is), and making it inert would be a trap.

**ARIA and keyboard** — the WAI-ARIA combobox-with-tree-popup pattern, which is why this is a new component and not a flag on `Combobox`:

- input is `role="combobox"` with `aria-expanded`/`aria-controls`/`aria-activedescendant`; popup is `role="tree"`; rows are `role="treeitem"` with `aria-expanded`, `aria-level`, `aria-selected`.
- **Focus never leaves the input.** Arrow keys move a roving `aria-activedescendant`, so the user can keep typing to re-filter while a keyboard selection is live — that is the interaction, and it is exactly what `aria-activedescendant` exists for.
- ArrowDown/ArrowUp walk *visible* rows; ArrowRight expands (or descends if already expanded); ArrowLeft collapses (or ascends to the parent); Enter selects; Home/End jump; Escape closes and returns focus to the input.

**Surface: dropdown on desktop, full-screen sheet under 720px.** Same markup, different presentation — the `nav-popup` technique `styles.css` already uses. A 320px-tall scrolling tree inside a phone viewport the keyboard has already eaten is not usable; the sheet gives the tree width and thumb-reachable rows. `TreeSelect.css` repeats the `720px` literal, as `Modal.css:47` already does, and for the same reason (custom properties can't be used in a media condition) — keep the three in sync by hand.

**Loading** renders a single «Cargando…» row inside the open popup. Not a skeleton tree: we don't know the structure yet, and implying one we don't have is worse than a word.

**`Combobox` is left alone.** `FirstAscensionistPicker` remains its one consumer. It's a flat list with a genuinely different shape, and migrating it here would be scope creep dressed as consolidation.

### 3. `AreaPicker` — the app-side wrapper

Stays in `frontend/src/components/`, keeps its current props minus the dead one:

```ts
interface Props {
  value: number | null
  onChange: (areaId: number | null) => void
  allowCreate?: boolean
  allowClear?: boolean
}
```

`allowClear` still distinguishes the two semantics from §1's table; no `mode` prop. The difference between filter and assign is one boolean and one label, not a new abstraction.

**Data.** One `useQuery` on `queryKeys.areaTree()` → `['areas', 'tree']`, `queryFn: areasApi.listAreaTree()`, **`staleTime: 5 * 60_000`**. This is a deliberate, commented exception to `queryClient.ts`'s global `staleTime: 0` — the area tree changes rarely, four pickers share the key, and mutations still invalidate it because every area mutation in the app already invalidates the `['areas']` prefix (including this component's own create, §3 below). Fetching stays **deferred to first open** (`enabled` on open, plus the existing mount-time fetch when `value !== null` so a preselected label can be resolved), per `frontend/CLAUDE.md`'s "modal/dropdown data is fetched by the thing that needs it" rule — mechanically pinned by MSW's `onUnhandledRequest: 'error'`. With one shared key, whichever picker opens first pays for all four.

`queryKeys.areaList` and the `refreshToken` prop are deleted; nothing else references either.

**Closed-input display** is the full breadcrumb, `Peña Rúa › Sector Norte`. With 300 crags, sector names like «Sector Norte» repeat constantly and the breadcrumb *is* the disambiguator — it belongs in the field, not in dim secondary text.

**The subtree hint** is a separate line below the input, rendered only in filter mode (`allowClear`) and only when the selected area has children: «Incluye 4 subsectores». It can't live inside the value string, and without it a user filtering by a parent has no idea the backend is expanding to descendants — a real comprehension bug, not a nicety.

**Head rows / recents.** An LRU of area ids in `localStorage` under one global key, `acordanza.recentAreas`, cap 8:

- **One key, not one per call site** — a crag used on `RouteFormPage` is exactly the crag wanted first on `MyTicksPage`. Splitting them halves the signal for nothing.
- **Any selection counts**, not only committed ones. "Committed" is marginally better ordering in exchange for threading a callback through four call sites.
- **The exact node is stored**, not its root, and shown with its breadcrumb — re-picking «Peña Rúa › Sector Norte» in one click is the point.
- **Padded with the highest-`routeCount` roots** when recents are thin, so a first-time user gets a useful head list instead of an empty one. Client-side sort over data already in hand.
- **Not cleared on logout.** It's ergonomics, not PII; having `AuthContext.logout` clear it would silently reset the feature for anyone who logs out.

**Dimming.** In filter mode, a node whose *subtree* route count is 0 renders `dimmed` — picking it guarantees an empty result list. Assign mode doesn't dim: an empty sector is precisely where a new route often goes.

**Create is contextual** (`allowCreate` + `Permissions.areaCreate`, as today). The 1800-option `<select>` is deleted outright. Instead `TreeSelect`'s `onCreateChild` puts a «+» on each row («Añadir subsector aquí») and one «Añadir zona nueva» at the popup foot for roots — **the parent is implied by where you clicked, so there is no parent selector to get wrong.** The name is captured in the existing inline sub-form (`.combo-create-form` in `styles.css`), the create calls `areasApi.createArea(name, parentId)`, invalidates the `['areas']` prefix, and selects the new area — same flow as today, minus the impossible chooser.

### 4. Call sites

No prop changes, so all four are near-untouched:

- `Filters.tsx:40` — unchanged (`allowClear`), gains the subtree hint for free.
- `MyTicksPage.tsx:85` — unchanged (`allowClear`), likewise.
- `RouteFormPage.tsx:225` — unchanged (`allowCreate`).
- `MyImportsPage.tsx:163` — unchanged.

### Non-goals

- **`/manage/areas` is not touched.** `AreaTree` pages `GET /areas/roots` at 10/page, which at 300 roots is 30 pages of clicking — the same underlying problem on a different screen, and a separate plan. It keeps its lazy `roots`/`children` fetching and its `includeUnvalidated` opt-in, both of which this endpoint deliberately doesn't offer.
- **`GET /areas` stays.** `AreaMap` (viewport-bounded) and `AreaPolygonModal` (vertices) genuinely need geometry.
- **`FirstAscensionistPicker` and `Combobox` stay as they are.**
- **No multi-select.** Filtering by several areas at once is a real idea and a different plan; every call site here is single-valued today.
- **No server-side area search endpoint.** With the whole tree client-side there is nothing for it to do.

## Steps

Three PRs, one plan. The backend endpoint is independently reviewable and independently useful; the design-system primitive is where the subtle work is and deserves a review not buried under call-site diffs; the swap then reads as almost pure deletion.

**PR 1 — backend.**

1. `AreaTreeNodeDto` + `GET /areas/tree` in `AreasController`, with the two-query projection and `ValidationVisibility` filters above.
2. Update the stale `AreasController.cs:82-86` comment.
3. Backend tests (below).
4. Regenerate the OpenAPI document (`ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true`, Postgres + Azurite up, `Jwt:Key` set) and `bun run generate-api` in `frontend/`; commit both diffs.
5. `backend/CLAUDE.md`: note the endpoint under `AreasController` and under "Validation visibility".

**PR 2 — design system.**

6. `treeFilter.ts` (`buildIndex`, `normalise`, `matches`, `prune`) + its tests.
7. `TreeSelect.tsx`, `TreeSelect.css`, including the roles/keyboard model and the 720px sheet.
8. Export `TreeSelect` and `TreeSelectNode`/`TreeSelectProps` from `design-system/src/index.ts`.
9. Design-system tests (below); document the component in `/DESIGN.md` alongside the existing set.

**PR 3 — frontend swap.**

10. `api/types.ts` — `AreaTreeNode` off `components['schemas']['AreaTreeNodeDto']`; `api/areas.ts` — `listAreaTree()`; `api/queryKeys.ts` — add `areaTree()`, delete `areaList`.
11. Rewrite `AreaPicker` on `TreeSelect`: the query, breadcrumb formatting, subtree roll-up + hint, dimming, validation-status suffix, recents, contextual create. Delete the `refreshToken` prop and the parent `<select>`.
12. `src/test/factories.ts` — `makeAreaTreeNode`.
13. Rewrite `AreaPicker.test.tsx` (all four current tests assert against `GET /areas`) and add the new cases below.
14. `bun run test` + `bun run build` in both `design-system/` and `frontend/`, `bun run lint`; `dotnet test Acordanza.slnx` for PR 1.
15. `frontend/CLAUDE.md`: rewrite the `AreaPicker` mentions in the `components/` bullet and the modal/dropdown-data bullet; note the `staleTime` exception and that `TreeSelect` now lives in the design system. `/DESIGN.md`: the new component.
16. **Verify with the `run-acordanza` skill.** Three things can't be judged from code and must be looked at against real data: whether the head-rows-plus-«Ver todas» split actually feels like browsing at 300 roots, whether the pruned tree stays legible mid-type, and the 720px sheet on a real viewport.

## Testing

**Backend** (`Acordanza.Api.Tests`, `Controllers/AreasTests`):

- returns every approved area flat, with `parentAreaId` intact for a two-level tree;
- a pending area added by the caller **is** returned; a pending area added by someone else **is not** (this is the whole of plan 11's rule for this endpoint, and there is no `includeUnvalidated` escape hatch to test around);
- `routeCount` is the area's **own** routes, not its subtree's — seed a parent with 1 route and a child with 2, assert `1` and `2`, not `3` and `2`;
- `routeCount` excludes a route the caller can't see (another user's pending route);
- **a row-count assertion: the action issues exactly two SQL statements** regardless of how many areas are seeded. At 1800 areas an accidental N+1 in the count projection is the single most likely way this ships slower than what it replaces, and it is invisible in a 40-row fixture unless something asserts it.

**Design system** (`design-system/`, its own Vitest — this is precisely the keyboard/focus layer that suite exists for):

- `treeFilter.test.ts` — pruning keeps a match's ancestors and drops unrelated branches; diacritics fold both ways (`pena`→`Peña`, `peña`→`Pena`); prefix-per-word (`sec nor`→«Sector Norte») and substring fallback; a matching parent yields its children.
- `TreeSelect.test.tsx` — renders `role="tree"` with `role="treeitem"` rows carrying `aria-level`; ArrowDown/ArrowUp move `aria-activedescendant` over visible rows only; ArrowRight expands and ArrowLeft collapses (`aria-expanded` flips); Enter selects the active row; Escape closes and refocuses the input; typing while a row is active re-filters without losing focus; `headNodes` render when the query is empty and «Ver todas» reveals the full root list; `onCreateChild` fires with the row's id, and with `null` from the foot button.

**Frontend** (`AreaPicker.test.tsx`):

- the deferred-fetch pin, rewritten against `/areas/tree`: no fetch on mount with `value === null`; exactly one fetch on open; one fetch on mount when `value !== null`, to resolve the label;
- the subtree hint appears with `allowClear` and a parent selected, and does **not** appear in assign mode;
- a non-approved area still renders its `validationStatusLabels` suffix;
- contextual create posts the clicked row's id as `parentAreaId`, and the foot button posts `null`;
- recents persist across a remount and are padded by route count when `localStorage` is empty.
