# Show orientation as a compass rose, and filter by several at once

Status: done
Issue: [#185](https://github.com/acheve-org/acordanza/issues/185)
Depends on: [17](17-grade-scale-and-pills.md) and [20](20-route-display-revamp.md) only as precedent (`GradePill`/`gradeBand` is the shape this copies: a display component in the design system plus a pure geometry/label module beside it; plan 20 is what put orientation on `RouteCard`'s meta line in the first place). One backend change, in `GET /routes` only — no migration, no data-model change.

## Problem

Orientation is one of the few facts that decides whether a route is climbable *today* — north faces stay in the shade, south faces bake — and today it is the least legible thing on screen:

- It renders as a Spanish word buried in a `·`-joined text line (`RouteCard.tsx:14`, `TickedRouteCard.tsx:17`) or as one `label/value` pair among four in `DetailModal`'s stats row (`DetailModal.tsx:149-153`). "Sureste" reads at the same weight as "28 m" and "Equipada", so scanning a list for shade means reading every card.
- The filter (`Filters.tsx:57-64`) is a `<select>` that picks **exactly one** direction. The real question a climber asks in August is "what's in the shade this afternoon?" — that's *north, northeast and northwest*, three separate searches today, whose results can't be seen together.

Both are the same underlying gap: orientation is angular data being presented, and queried, as an unordered list of words. A compass rose (*rosa de los vientos*) is the notation that already exists for this, is understood without a legend, and — being a ring of eight sectors — is also a multi-select control at no extra conceptual cost. The same picture answers "where does this face?" and "give me these three".

## Design

Three pieces, in dependency order: a geometry/label module and two components in the design system (§1), the display sites (§2), the multi-select filter and the backend change it needs (§3), the create/edit form (§4).

Two calls in §1 — how the 18px card glyph is drawn, and what the picker's interaction layer is made of — were settled against a live prototype of all the candidates rather than argued on paper. The reasoning is recorded where each lands, so neither gets relitigated during implementation.

### 1. `design-system/src/components/Compass/`

Mirrors the `GradePill` + `gradeBand` split exactly — a pure module holding the domain knowledge, a display component, and (new here) an interactive one:

```
compass.ts          Orientation type, ordered list, Spanish labels/abbreviations, wedgePath(), needlePath()
CompassRose.tsx     display only — role="img", no interaction
CompassPicker.tsx   interactive — native checkbox/radio inputs, single or multiple
Compass.css
```

Two components rather than one with a mode prop: the props genuinely don't overlap (`value` vs `value + onChange + multiple`), and the display one must stay free of any interaction surface since it renders inside a clickable `RouteCard`.

**`compass.ts` owns the labels, and `api/types.ts` re-exports them.** The eight values (`north` … `northwest`) are backend enum strings, but the ordering *is* geometry and the component can't work without it, so the ordered array and the label maps move here — `frontend/src/api/types.ts` keeps `orientationLabels` as a re-export so every existing call site and the root `CLAUDE.md` rule ("reuse `orientationLabels` rather than hardcoding translations") keep working unchanged. Spanish text in the design system is already established (`Pagination` renders "Anterior"/"Siguiente"), so this doesn't cross a new line.

```ts
export type Orientation = 'north' | 'northeast' | 'east' | 'southeast' | 'south' | 'southwest' | 'west' | 'northwest'

// Clockwise from north — this order is the component's geometry, not a preference.
export const orientations: Orientation[] = ['north', 'northeast', 'east', 'southeast', 'south', 'southwest', 'west', 'northwest']

export const orientationLabels: Record<Orientation, string> = { north: 'Norte', /* … */ northwest: 'Noroeste' }

// Note the Spanish O for Oeste — not the English W.
export const orientationAbbr: Record<Orientation, string> = { north: 'N', northeast: 'NE', east: 'E', southeast: 'SE', south: 'S', southwest: 'SO', west: 'O', northwest: 'NO' }
```

**Geometry: pure functions over one shared polar helper, unit-tested.** Sector *i* is centred on `-90 + 45i` degrees (north up, clockwise) and spans 45°, so it runs from `centre - 22.5` to `centre + 22.5`. A wedge is an annulus segment — outer arc, line in, inner arc back, close:

```ts
// Shared by wedgePath and needlePath below; the only trigonometry in the module.
const point = (cx: number, cy: number, r: number, deg: number) => {
  const rad = (deg * Math.PI) / 180
  return `${(cx + r * Math.cos(rad)).toFixed(2)} ${(cy + r * Math.sin(rad)).toFixed(2)}`
}

// Annulus segment for sector `index`, as an SVG path `d`. Kept pure and parameterised by
// radius so the 56px display rose and the 132px picker share one implementation.
export function wedgePath(index: number, cx: number, cy: number, outer: number, inner: number, gap = 1.5): string {
  const mid = -90 + 45 * index
  const a0 = mid - 22.5 + gap, a1 = mid + 22.5 - gap   // `gap` degrees of air between wedges
  const p = (r: number, deg: number) => point(cx, cy, r, deg)
  return `M ${p(outer, a0)} A ${outer} ${outer} 0 0 1 ${p(outer, a1)} L ${p(inner, a1)} A ${inner} ${inner} 0 0 0 ${p(inner, a0)} Z`
}
```

(large-arc flag is always `0` — 45° never exceeds a semicircle; sweep `1` outbound, `0` back along the inner arc.)

**`CompassRose` — display.** `<CompassRose value={o} size="sm" | "md" />`, `viewBox="0 0 100 100"`, `role="img"`, `aria-label={`Orientación: ${orientationLabels[value]}`}`:

- **`sm` (18px, card meta line) — a needle in a ring, not a miniature rose.** Deliberately *not* eight wedges: at 18px each wedge is ~3px and the whole thing turns to mud. It draws a hairline ring (`r=43`, `stroke-width: 5`) and a single arrowhead pointing along the sector's centre angle. Four candidates were built at true size and compared on real cards — a filled slice on a disc, the same plus a north tick, a shrunken eight-wedge rose, and this needle — and the needle is the only one whose direction is unambiguous at 18px, because the shape itself is asymmetric: a slice and its neighbour differ by a 45° rotation of a fat blunt form, while a needle's tip lands somewhere visibly different. It costs the visual rhyme with the `md` rose, which is the price paid here, and `orientationAbbr[value]` sits beside it (see §2) carrying the exact value.

  The needle is a second path helper next to `wedgePath`, not a wedge with different radii — a kite from tip to two shoulders to a tail notch:

  ```ts
  export function needlePath(index: number, cx: number, cy: number, r: number): string {
    const mid = -90 + 45 * index
    const p = (radius: number, deg: number) => point(cx, cy, radius, deg)
    return `M ${p(r, mid)} L ${p(r * 0.75, mid + 138)} L ${p(r * 0.25, mid + 180)} L ${p(r * 0.75, mid - 138)} Z`
  }
  ```

- **`md` (56px, detail modal).** All eight wedges, the selected one filled with `--accent`, the rest `--chip-bg`, plus the four cardinal letters (N / E / S / O) at radius 44 in `--dim`. Intercardinals get no letter — the wedge plus the `aria-label` covers them and four letters is the density the size affords.

**`CompassPicker` — interactive.** `<CompassPicker value={Orientation[]} onChange={…} />`, or `<CompassPicker value={Orientation | null} onChange={…} multiple={false} />`.

The interaction layer is **eight native form controls in a 3×3 CSS grid** (centre cell empty) — not `tabindex` on SVG paths, and not `<button>`s carrying `aria-pressed`/`role="radio"`. Each cell is a `<label>` holding a transparent full-bleed `<input>` plus the SVG that paints its wedge at 300% size, offset by its grid position (`left: calc(-100% * var(--c)); top: calc(-100% * var(--r))`, `pointer-events: none`), so the eight cells compose into one continuous rose:

```
<label class="compass-cell" style={{ '--r': row, '--c': col }}>
  <input type={multiple ? 'checkbox' : 'radio'} name="orientation" value={o} aria-label={orientationLabels[o]} />
  <svg aria-hidden="true">…one wedge path…</svg>
</label>
```

`.compass-cell input { position: absolute; inset: 0; width: 100%; height: 100%; opacity: 0 }` — transparent rather than `display: none`, so it stays focusable and stays the hit target.

- **`<input>`, not `<button aria-pressed>`, because of the single-select case.** `role="radio"` on a button buys the *role* and nothing else: arrow-key traversal, roving focus and wrap-around are all UA behaviours a real radio group already has and a button group would need hand-written and hand-tested. A prototype of the button version made this concrete. Checkbox semantics are the same win on the multi side, smaller: Space, checked state and the accessible name all come free.
- **All state is CSS on the input's checked/focus state** — `input:checked ~ svg .compass-wedge { fill: var(--accent) }`, `input:focus-visible ~ svg .compass-wedge { stroke: var(--ink) }`, and hover as `:hover input:not(:checked) ~ svg .compass-wedge` so hover never overpaints a selection. No JS class toggling; the component's only job is `onChange`.
- **Focus is drawn as a `stroke` on the wedge, not an `outline`** — a square outline around a wedge-shaped paint area reads as a bug, and the stroke lands exactly on the shape about to be toggled.
- **Hit targets are the square cells**, slightly larger than the paint (hover lights a wedge from ~8px outside it). Confirmed as the right trade for touch; no two cells overlap, so nothing is ambiguous.
- **The cardinal letters are one overlay SVG** spanning the grid (`position: absolute; inset: 0; pointer-events: none`, `aria-hidden`), not eight per-cell texts.
- **DOM order is `orientations` order**, so Tab walks the compass clockwise from north.

Sizing is fixed (132px, one interactive size) — the picker appears in exactly two places and a scalable interactive rose buys nothing.

**No new tokens.** `--accent`/`--on-accent` (selected), `--chip-bg` (unselected), `--border`, `--dim`, `--radius-*` already exist; the whole component is one CSS file over the current palette.

**Considered and deferred: tinting by sun exposure** (warm south, cool north — *solana/umbría*, the thing the orientation actually means to a climber). It's the strongest version of this idea, but it needs a new token pair in `DESIGN.md` and it collides visually with the grade-band colours already carried by `RouteCard`'s left accent and `GradePill`. Land the monochrome rose first, look at a real list, then decide.

### 2. Display sites

- **`RouteCard`/`TickedRouteCard`.** The meta line stops being one `join(' · ')` string and becomes an interleaved node list: `<CompassRose size="sm" value={o} />` (the needle) then `SE · 28 m · Equipada` as before. Small helper in both files (they already duplicate this line) — or better, one shared `<RouteMeta route={…} />`, since plan 20 created the duplication and this is the second edit to both copies.
- **`DetailModal`.** The `Orientación` stat's `<span className="detail-stat-value">` becomes `<CompassRose size="md" value={…} />` above the full Spanish word. The stats row's other three cells are untouched — the rose is the one field that earns extra height.
- **Not `AreaPage`.** Aggregating "this sector faces N/NE" from its routes is a real idea and a different plan; nothing here computes an aggregate.

### 3. Multi-select filter — and the backend change

**Backend, `RoutesController.GetRoutes` only.** The parameter becomes an array; ASP.NET binds repeated `?orientation=north&orientation=northwest` natively:

```csharp
[FromQuery] string[]? orientation
…
if (orientation is { Length: > 0 })
{
    if (orientation.FirstOrDefault(o => !AllowedOrientations.Contains(o)) is not null)
        return BadRequest(new { error = "la orientación no es válida" });   // same string ValidateOrientation returns
    query = query.Where(r => r.Orientation != null && orientation.Contains(r.Orientation));
}
```

EF Core/Npgsql translates the `Contains` to `= ANY(@p)`, one round trip. Rejecting unknown values rather than silently dropping them keeps the read path consistent with the write path (`ValidateOrientation`), which is the only other place these eight strings are enforced. `AllowedOrientations` stays the single source of truth. **No back-compat shim for a single-valued `orientation`:** the frontend is the only client, and a one-element array serialises to exactly the URL a single value produced today.

**Frontend.** `RouteFilters.orientation: string` → `Orientation[]`, `emptyRouteFilters.orientation: []`. In `api/routes.ts`, `params.set` becomes `params.append` per value.

**Normalise on toggle, not on serialise.** Every toggle rebuilds the array as `orientations.filter((o) => next.includes(o))` — canonical compass order, always. That one line is what keeps `queryKeys.routes(filters)` (`['routes', filters]`, hashed structurally by react-query) stable: without it, picking N then S and picking S then N are two different cache entries and two identical requests.

**`Filters.tsx`.** The `<select>` is replaced by `<CompassPicker value={filters.orientation} onChange={…} />` plus a "Cualquiera" text button, shown only when something is selected, that clears back to `[]` — an empty rose already *means* "any", so the clear affordance doesn't need to occupy the centre cell permanently. The 250ms debounce in `SearchPage` already covers the burst of clicks from selecting three sectors; nothing there changes.

`AreaPage` (`{ ...emptyRouteFilters, areaId }`) needs no edit — it picks up `[]` for free.

### 4. Create/edit form

`RouteFormPage`'s orientation `<select>` (line 196) becomes `<CompassPicker multiple={false} value={form.orientation || null} onChange={…} />`. `Orientation` is nullable on `ClimbingRoute`, so the picker keeps a "Sin especificar" clear button next to it; clicking the already-selected sector does **not** deselect (that's a `radiogroup` and clearing should be explicit). The submit payload (`orientation: form.orientation || null`) is unchanged.

### Non-goals

- **A route still has exactly one orientation.** The multi-select is a *filter*, not a data-model change — no migration, no `Orientation` → `Orientations` on `ClimbingRoute`. Multi-facing routes, if they're ever a real need, are their own issue.
- **No degrees, no free rotation.** Eight sectors, the values the backend already validates.
- **No sun/shade tinting** (see §1), no time-of-day or seasonal logic — the fantasy version of this feature ("shady right now") needs latitude, date and wall angle, and is not what's being planned here.
- **No orientation on the map** (`AreaMap` polygons stay as they are).
- **No shade preset.** A one-tap "Sombra de tarde" (N · NE · NO) next to the filter was prototyped and is genuinely the question that motivated the whole feature, but it's undecided and orthogonal — it's a preselected array, and it can be added later without touching anything here. If it's wanted, it's a separate issue.

## Steps

1. **Design system.** `compass.ts` (+ tests), `CompassRose.tsx`, `CompassPicker.tsx`, `Compass.css`; export all four public names from `design-system/src/index.ts`; document the component in `/DESIGN.md` alongside the existing set.
2. **`frontend/src/api/types.ts`** re-exports `orientationLabels`/`orientations`/`Orientation` from `acordanza-design-system` and drops the local map; `RouteFilters.orientation` becomes `Orientation[]`, `emptyRouteFilters.orientation` becomes `[]`.
3. **Backend.** `GET /routes`'s `orientation` parameter → `string[]?` with the validation and `Contains` filter above.
4. **Regenerate the OpenAPI contract** (`ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` with Postgres+Azurite up and `Jwt:Key` set), then `bun run generate-api` in `frontend/`; commit both diffs — CI's `openapi-contract-check` fails otherwise.
5. **`api/routes.ts`** — `params.append` per selected value.
6. **`Filters.tsx`** — `CompassPicker` + clear button, canonical-order toggle.
7. **`RouteCard`/`TickedRouteCard`** — `sm` needle + abbreviation in the meta line (extract the shared `RouteMeta` while both files are open).
8. **`DetailModal`** — `md` rose in the stats row.
9. **`RouteFormPage`** — single-select picker.
10. **Tests** (below), then `dotnet test Acordanza.slnx`, `bun run test` + `bun run build` in both `design-system/` and `frontend/`, `bun run lint`.
11. **Docs.** `frontend/CLAUDE.md`: the `Filters` bullet (orientation is now multi-valued), the `RouteCard`/`DetailModal` bullets, and a line noting `orientationLabels` now originates in the design system. `backend/CLAUDE.md`: `GET /routes` takes repeated `orientation`. `/DESIGN.md`: the new components.
12. **Verify** with the `run-acordanza` skill: the `sm` needle at real card density, the `md` rose in the modal, and a three-sector filter selection returning the union. The two judgement calls that can't be made from code — which `sm` treatment, and whether square hit targets feel right — were settled against a live prototype before this plan was finalised, so this pass is confirmation against real data, not a second exploration.

## Testing

**Backend** (`RoutesTests`, extend the existing `GET /routes` filter tests): one orientation filters as before; two orientations return the union and nothing else; an unknown value returns 400 with `la orientación no es válida`; no `orientation` parameter filters nothing.

**Design system** (`design-system/`, its own Vitest — this is exactly the keyboard/focus layer that suite exists for):
- `compass.test.ts` — `wedgePath` and `needlePath` are pure geometry, so pin them: sector 0 points north (its outer endpoints straddle `cx`, both above `cy`; the needle's tip is at `cx`, above `cy`), sector 2 east, and the eight paths are distinct in each. Cheap, and it's the one part where an off-by-45° is invisible in review and obvious on screen.
- `CompassRose.test.tsx` — renders `role="img"` with the Spanish label; `sm` draws one needle, `md` draws eight wedges.
- `CompassPicker.test.tsx` — eight **checkboxes** named in Spanish (`getByRole('checkbox', { name: 'Norte' })`); clicking one calls `onChange` with it added and a selected one with it removed; `toBeChecked()` reflects `value`; `multiple={false}` renders **radios** and replaces rather than accumulates; Tab reaches every sector in compass order.

  **Don't assert the radio group's arrow-key traversal.** It's the UA behaviour this design exists to inherit, and jsdom doesn't implement it — a test there would be asserting jsdom, not the component. Tab order and checked state are the parts that live in our code; the arrow keys get checked once by hand in step 12.

**Frontend:**
- `routes.test.ts` — `orientation: ['north', 'south']` serialises to `?orientation=north&orientation=south`; `[]` omits the parameter entirely.
- `queryKeys.test.ts` — updated for the array-valued field, plus one assertion that two differently-ordered selections normalise to the same key (guards §3's normalisation rule).
- `Filters.test.tsx` — selecting two sectors calls `onChange` with both in compass order; the clear button appears only with a selection and resets to `[]`.
- `RouteCard.test.tsx` / `TickedRouteCard.test.tsx` — the needle renders with its accessible name when `orientation` is set and nothing renders when it's null (that second half is what stops an empty rose leaking onto ungraded/unoriented routes).
- `DetailModal.test.tsx` / `RouteFormPage.test.tsx` — updated for the picker replacing the `<select>`; existing permission-matrix assertions untouched.
