# Explore list: depth color+icon instead of "Con sectores"/"Sin sectores"

Status: done
Depends on: [37](37-explore-map-revamp.md) — this revises just plan 37 §7 (List content); the map/list/geolocation/clustering machinery it built stays untouched. No backend change, no migration, no OpenAPI regeneration. Frontend + design-system only.

## Problem

`ExplorePage`'s list row subtitle currently means two different things depending on the row. For a top-level row it's `hasChildren` — "Con sectores"/"Sin sectores", computed client-side (`areas.some(a => a.parentAreaId === area.id)`). For a subarea row it's the parent's name, a disambiguating breadcrumb (sibling subareas across different top-level areas can share a name, e.g. "Sector Norte"). Nothing on the row says, at a glance, *which kind of row this is* — you have to read and parse the subtitle text to work out whether you're looking at a zone or one of its sectors, which is exactly the confusion this plan removes.

Meanwhile `AreaMap` already encodes this same root/subarea distinction visually, just not for the list: the active top-level area's own polygon draws in `#3182ce` (blue), its subareas' polygons in `#38a169` (green) — two raw hex literals, unnamed, not shared with anything.

## Design

### 1. Two new design tokens: `--depth-root` / `--depth-subarea`

`design-system/src/tokens.css`, following the existing `--grade-6`/`--grade-7`/`--grade-8-9` pattern of named category colors — but without the `-bg`/`-border` siblings the grade triplet has, since nothing here renders a tinted background or border, only a small icon glyph:

```css
--depth-root: #3182ce;
--depth-subarea: #38a169;
```

Values are literally `AreaMap`'s existing blue/green — this is the map's tier-coloring becoming a named, reusable token, not a new color choice. Document both in `DESIGN.md`'s `colors:` header block and its narrative colors section, next to `grade-orange`/`grade-purple`'s entries:

> **Depth Root** (`{colors.depth-root}` — `#3182ce`): root-area indicator — `ExplorePage`'s list icon and `AreaMap`'s active top-level polygon.
> **Depth Subarea** (`{colors.depth-subarea}` — `#38a169`): the same, one level down.

### 2. `ListRow` gains a generic `leading` slot — no depth logic inside it

`design-system/src/components/List/List.tsx`'s `ListRowProps` gains one new optional prop, symmetric with the existing `trailing`:

```ts
leading?: ReactNode // left of title/subtitle — an icon, typically
```

Rendered in a new wrapper before `.list-row-main`:

```tsx
{leading && <div className="list-row-leading">{leading}</div>}
<div className="list-row-main">…</div>
```

`List.css`: `.list-row-leading { display: flex; align-items: center; flex-shrink: 0; }` (mirrors `.list-row-trailing`'s `flex-shrink: 0`). `ListRow` itself renders whatever node it's handed and stays ignorant of "depth" — same division of responsibility `trailing` already has (a `RemoveButton`, `Button`s — `ListRow` doesn't know what those do either). Non-breaking: the prop defaults to unset, every existing call site (`ExplorePage`, still the only one) keeps working unchanged until it opts in. Document in `DESIGN.md`'s List/ListRow entry.

### 3. The actual icon + color choice lives in `frontend/`, not the design system

New in `frontend/src/lib/exploreAreas.ts` (already the framework-free shared lib both `AreaMap` and `ExplorePage` pull from):

```ts
// Kept in sync by hand with tokens.css's --depth-root/--depth-subarea — AreaMap
// needs a literal color string for Leaflet's SVG renderer (see §4), which can't
// resolve a CSS custom property the way a normal DOM style can.
export const DEPTH_COLORS = { root: '#3182ce', subarea: '#38a169' } as const;
```

Two small inline SVGs (colocated in `ExplorePage.tsx`, or a tiny local component if that reads cleaner — implementation's call, not a design-system component, matching the app's existing convention of ad hoc per-use inline SVGs rather than a shared icon set): a mountain/peak glyph for root rows, a single map-pin glyph for subarea rows — distinct shapes, not the same glyph in two colors, so the signal survives without relying on color perception alone. Both `stroke="currentColor"`/`fill="none"`, in the app's existing icon style (matches `AreaMap`'s own locate-button SVGs), wrapped with `style={{ color: 'var(--depth-root)' }}` / `var(--depth-subarea)` — a real DOM element, so the CSS custom property resolves normally here (unlike the Leaflet case in §4).

`ExplorePage.tsx`'s row loop:

```tsx
const isTopLevel = area.parentAreaId === null;
return (
  <ListRow
    key={area.id}
    ref={(row) => { rowRefs.current[area.id] = row; }}
    title={area.name}
    subtitle={isTopLevel ? undefined : activeTopLevelArea?.name}
    leading={isTopLevel ? <RootIcon /> : <SubareaIcon />}
    selected={area.id === selectedAreaId}
    onClick={() => handleSelectArea(area)}
  />
);
```

The `areas.some(a => a.parentAreaId === area.id)` "Con sectores"/"Sin sectores" computation is deleted outright, not repurposed — the fact it reported (does this area have something to zoom into) is answered by tapping the row, which flies the map in and either reveals subarea polygons or doesn't. Keeping it as a second signal on the same row would just add back the clutter this plan removes.

### 4. `AreaMap.tsx`'s polygon colors become token-sourced literals, not new hex

Leaflet's SVG renderer sets a path's stroke via `path.setAttribute('stroke', options.color)` (`leaflet-src.js`'s `SVG.prototype._updateStyle`) — a raw SVG attribute, not a CSS style — so a `'var(--depth-root)'` string handed to `pathOptions.color` would not resolve; the browser doesn't evaluate custom properties inside attribute value strings. `AreaMap.tsx` therefore keeps using a literal color string for `pathOptions.color`, sourced from `DEPTH_COLORS` (§3) instead of its own separately-typed-out inline hex:

```tsx
pathOptions={{ color: DEPTH_COLORS.root }}    // was: '#3182ce'
...
pathOptions={{ color: DEPTH_COLORS.subarea }} // was: '#38a169'
```

No visual change on the map — this section is purely "stop duplicating the hex inline in `AreaMap.tsx`, source it from the one place `frontend/` already keeps this pair" (`exploreAreas.ts`). Marker/pin icons are explicitly untouched — they stay Leaflet's plain default icon; recoloring markers inside a `MarkerClusterGroup` is materially more work (custom `DivIcon` markup per marker) than this list-focused cleanup calls for, and the depth-color connection is already fully expressed via the polygons alone.

## Non-goals

- No change to marker/pin rendering or clustering behavior.
- No support for nesting deeper than the two tiers `deriveVisibleAreas` already renders (root, subarea-of-the-active-root) — the backend's `Area` model is technically unbounded-depth, but nothing in `ExplorePage` today surfaces a third tier, and building for one now would be speculative.
- No `aria-label`/accessible name added specifically for the depth icon — the row's title/subtitle text and position already convey the tier; the icon is a supplementary visual cue, distinct in shape as well as color, not the sole carrier of the information.
- `AreaTree`/`TreeSelect`/`AreaPicker` (the `/manage/areas` and route-form pickers) are untouched — this is `ExplorePage`'s list only; extending the depth-color convention there is a separate decision if it ever comes up.
- No design-system `Icon` component introduced — the two new SVGs stay ad hoc, matching how every other icon in the app is authored today.

## Steps

1. **`design-system/src/tokens.css`** — add `--depth-root`/`--depth-subarea`.
2. **`DESIGN.md`** — add both to the `colors:` header block and the narrative colors section; update the List/ListRow entry to mention `leading`.
3. **`design-system/src/components/List/List.tsx`** — add `leading?: ReactNode` to `ListRowProps`, render it in a new wrapper before `.list-row-main`.
4. **`design-system/src/components/List/List.css`** — `.list-row-leading` rule.
5. **`frontend/src/lib/exploreAreas.ts`** — add `DEPTH_COLORS`.
6. **`frontend/src/pages/ExplorePage.tsx`** — the two SVGs, drop the `hasChildren` subtitle computation, wire `leading`/`subtitle` per §3.
7. **`frontend/src/components/AreaMap.tsx`** — swap the two literal hex strings for `DEPTH_COLORS.root`/`DEPTH_COLORS.subarea`.
8. **`frontend/CLAUDE.md`** — update the `ExplorePage` bullet: the list row now shows a depth icon (root/subarea) instead of "Con sectores"/"Sin sectores"; subarea rows still show the parent breadcrumb.
9. **Verify with the `run-acordanza` skill** — visual check: icon shape/color renders correctly for both tiers at a few zoom levels, list selection/highlight still works alongside the new `leading` slot, map polygons render unchanged.

## Testing

- **`design-system/List.test.tsx`** — add a case asserting `leading` renders inside `.list-row-leading`.
- **`frontend/src/pages/ExplorePage.test.tsx`** — extend the existing suite (plan 37) to assert a root row's `leading` icon differs from a subarea row's, and that neither row renders "Con sectores"/"Sin sectores" text anymore.
- **`frontend/src/lib/exploreAreas.test.ts`** — no new case needed for `DEPTH_COLORS` itself (a static two-key object — nothing behavioral to assert).
- **`AreaMap` stays untested at the rendering level**, per the existing `frontend/CLAUDE.md` carve-out — the polygon-color swap is a one-line change confirmed visually via step 9, not a candidate for jsdom coverage.
