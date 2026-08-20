# Revamp `/explore`: viewport-driven two-column layout, geolocation, clustering

Status: done
Depends on: nothing structurally new. Builds on plan 23 (`AreaMap`/`AreaPolygonModal` stay `React.lazy`) and reuses the existing bbox-filtered `GET /areas` endpoint as-is — **no backend change, no migration, no OpenAPI regeneration.** Frontend + design-system only.

## Problem

`pages/ExplorePage.tsx` today stacks a fixed-height `AreaMap` above a *separately* paginated `GET /areas/roots` list (10/page) that has nothing to do with what the map is currently showing — picking through pages to find a zone the map already has a pin for. The page scrolls as a whole; the map is a fixed `65vh` block, not the primary surface. There's no geolocation (the map always opens on a hardcoded Spain-wide view, `SPAIN_CENTER`/`SPAIN_ZOOM`), no clustering (`AreaMap` renders one `Marker` per top-level area unconditionally, which is fine at today's area count but has no mitigation if it grows), and selecting nothing in the list ever moves the map — the two surfaces don't talk to each other at all.

`AreaMap` itself already does real work worth preserving: a debounced (`300ms`), request-guarded viewport fetch (`GET /areas?minLat=...`) and a deliberate two-tier reveal — top-level areas always show as markers; a top-level area's subareas (as `Polygon`s, not markers — no marker exists for a subarea today) only appear once the map is zoomed past `SUBAREA_ZOOM = 12` and centered near that area. This plan keeps both mechanisms, just shares them with the new list instead of leaving them locked inside `AreaMap`.

## Design

### 1. Lift viewport fetching and area-grouping out of `AreaMap`, so the list can share both

Today `AreaMap` owns `areas` state, fetches it itself, and privately computes `topLevelAreas`/`activeTopLevelArea`/`subareas` via `useMemo`. For the list to show **exactly what's pinned on the map** (agreed: the list mirrors the map's own zoom-gated reveal, not a separate "always roots" view), both surfaces need to run off the same data and the same grouping logic — not two independent copies that could drift.

- **`hooks/useViewportAreas.ts`** — the fetch, moved verbatim (same debounced-`fetchHandle`-ref + `requestId`-guard shape `AreaMap` uses today, not react-query — `frontend/CLAUDE.md` already documents why this one stays off `useDebouncedValue`/react-query: it debounces map *events*, not a state value). Returns `{ areas: AreaSummary[], setBounds: (bounds: AreaBounds) => void }`. `ExplorePage` owns the hook and passes `areas` down to both the list and `AreaMap`.
- **`lib/exploreAreas.ts`** — the grouping, extracted as pure functions (same split as `compass.ts`/`treeFilter.ts` — domain-free logic cheap to pin with unit tests, colocated rather than buried in a component):
  ```ts
  export function deriveVisibleAreas(areas: AreaSummary[], center: L.LatLngLiteral | null, zoom: number) {
    // exact logic AreaMap has today: topLevelAreas, activeTopLevelArea (closest
    // top-level area to `center`, only once zoom >= SUBAREA_ZOOM), subareas
    return { topLevelAreas, activeTopLevelArea, subareas };
  }
  export function sortByDistance<T extends { centroidLat: number | null; centroidLng: number | null }>(
    areas: T[], center: L.LatLngLiteral | null,
  ): T[] { /* same Math.hypot distance AreaMap already uses to pick the closest area */ }
  ```
  `ExplorePage` and `AreaMap` each call `deriveVisibleAreas` with the same `areas` prop and the same `center`/`zoom` (both derived identically from the bounds `AreaMap` reports up on `moveend`/`zoomend` — see §2) — so "list mirrors map" holds by construction, not by convention. `ExplorePage` additionally calls `sortByDistance` on `[...topLevelAreas, ...subareas]` for the list's ordering (distance from the map's current center — stays meaningful while panning, unlike distance-from-user or alphabetical).

### 2. `AreaMap` becomes a controlled component

New props (replacing its current zero-prop, fully-internal shape):

```ts
interface AreaMapProps {
  areas: AreaSummary[];
  onBoundsChange: (bounds: AreaBounds, zoom: number) => void; // unchanged shape, now required
  selectedAreaId: number | null;
  onSelectArea: (id: number | null) => void;
  flyTarget: FlyTarget | null; // see §4
  onLocateMe: () => void;
  locating: boolean;
}
```

`AreaMap` keeps its own local `center`/`zoom` state (fed by its own `BoundsTracker`, as today) purely to compute its own render groups via `deriveVisibleAreas` and to report bounds upward — it does not need `center`/`zoom` passed back in as props.

### 3. Layout: two-column grid, scroll contained to the list

Rewrite `.explore-page` in `styles.css` from a flex column to the grid:

```css
.explore-page {
  height: calc(100dvh - var(--topbar-height));
  display: grid;
  grid-template-columns: 340px 1fr;
  gap: var(--space-xl);
  padding: var(--space-xxl);
  overflow: hidden;
}
```

(`* { box-sizing: border-box }` is already global, so the padding doesn't blow the height calc.) Left column (`.explore-list`) is a flex column: heading + hint text (static), then `.explore-list-scroll` (`flex: 1; min-height: 0; overflow-y: auto`) holding the `List`/`EmptyState` — `min-height: 0` is required on both the grid item and the flex child, since without it a flex/grid child won't shrink below its content size and the scroll never kicks in. Right column (`.explore-map`) is `height: 100%`.

`AreaMap`'s own `.area-map`/`.area-map-wrap` currently size themselves to `65vh` — written for the old stacked layout. Change the base `.area-map`/`.area-map-wrap` rule to `height: 100%; width: 100%` so it fills `.explore-map` exactly. This doesn't touch `.modal-wide .area-map` (`AreaPolygonModal`'s own `60vh` override, `styles.css:430`), which stays as-is — it's a separate selector, unaffected. The below-map hint text (*"Solo se muestran en el mapa los sectores que tienen información geográfica cargada"*) moves into the list column's header, since the map no longer has room below it.

**Mobile (`≤720px`, must stay the last block in the file per the existing convention):** map goes full-bleed, list becomes a bottom sheet:

```css
@media (max-width: 720px) {
  .explore-page { display: block; height: calc(100dvh - var(--topbar-height)); padding: 0; position: relative; overflow: hidden; }
  .explore-map { height: 100%; border-radius: 0; }
  .explore-list {
    position: absolute; inset: auto 0 0 0; max-height: 65%;
    background: var(--bg); border-radius: var(--radius-lg) var(--radius-lg) 0 0;
    box-shadow: 0 -4px 16px rgba(0,0,0,.15);
    transform: translateY(calc(100% - 48px)); transition: transform .2s ease;
    padding: var(--space-md) var(--space-lg);
  }
  .explore-list.is-open { transform: translateY(0); }
}
```

A `.explore-list-handle` button (full-width, the always-visible 48px strip) toggles `mobileListOpen` (plain `useState`, not `useDisclosure` — no outside-click/Escape semantics wanted here, just an explicit tap to expand/collapse). Selecting an area from the list also collapses the sheet (`setMobileListOpen(false)`), since the reason to pick something was to see it on the map.

### 4. Fly-to commands: selecting a list row, or a pin, moves the map

`MapContainer` forwards its `ref` straight to the underlying `L.Map` (confirmed in `react-leaflet@5`'s types — `ForwardRefExoticComponent<MapContainerProps & RefAttributes<LeafletMap>>`), so `AreaMap` holds `mapRef = useRef<L.Map>(null)` and reacts imperatively to a `flyTarget` prop:

```ts
export interface FlyTarget {
  bounds?: [[number, number], [number, number]];
  center?: [number, number];
  zoom?: number;
}
```

```ts
useEffect(() => {
  if (!flyTarget || !mapRef.current) return;
  if (flyTarget.bounds) mapRef.current.fitBounds(flyTarget.bounds, { padding: [40, 40] });
  else if (flyTarget.center) mapRef.current.flyTo(flyTarget.center, flyTarget.zoom ?? SUBAREA_ZOOM);
}, [flyTarget]);
```

No nonce/counter needed — `ExplorePage` always constructs a fresh object on each selection, so referential inequality alone re-triggers the effect, including re-selecting the same area twice.

`ExplorePage.handleSelectArea(area)`:

```ts
function handleSelectArea(area: AreaSummary) {
  setSelectedAreaId(area.id);
  setFlyTarget(
    area.polygon.length > 0
      ? { bounds: boundsOfPolygon(area.polygon) }
      : area.centroidLat != null
        ? { center: [area.centroidLat, area.centroidLng!], zoom: SUBAREA_ZOOM }
        : null,
  );
  setMobileListOpen(false);
}
```

Fitting tight to a top-level area's own polygon zooms in well past `SUBAREA_ZOOM` in practice, which — because `AreaMap`'s own `zoom`/`center` state updates from the resulting `moveend`, exactly as any other pan/zoom does — naturally triggers its existing subarea-reveal gate with no extra code. Selecting a *subarea* from the list works the same way against its own `polygon`.

**Two-way sync (§Q10):** clicking a pin should also update `selectedAreaId` so the list highlights/scrolls to the matching row. Each `Marker`/`Polygon` gets `eventHandlers={{ click: () => onSelectArea(area.id) }}` in addition to its existing nested `Popup` (Leaflet's native click-to-open-popup keeps working unchanged). Selecting a row in the list, conversely, should open that area's popup on the map — `AreaMap` keeps `markerRefs`/`polygonRefs` (`Map<number, L.Marker | L.Polygon>`, populated via each element's `ref` callback) and an effect:

```ts
useEffect(() => {
  if (selectedAreaId == null) return;
  markerRefs.current.get(selectedAreaId)?.openPopup();
  polygonRefs.current.get(selectedAreaId)?.openPopup();
}, [selectedAreaId, topLevelAreas, subareas]);
```

On the list side, each `ListRow` gets a `ref` (a small `rowRefs.current[area.id]` map) and an effect on `selectedAreaId` calling `.scrollIntoView({ block: 'nearest' })` — harmless when the row was the click's own origin, necessary when the origin was a map pin.

`ListRow` (design-system) needs one small additive change to render the highlight: an optional `selected?: boolean` prop appending a `list-row-selected` class (`border-color: var(--accent); background: var(--bg-card-hover)` in `List.css`) — non-breaking, every existing call site (`ExplorePage` was the only one; still is) keeps working unchanged since the prop defaults to falsy.

### 5. Geolocation

**`hooks/useGeolocation.ts`** — a small hook, not react-query (this isn't server data):

```ts
type Status = 'idle' | 'pending' | 'granted' | 'denied' | 'unsupported';
export function useGeolocation() {
  const [status, setStatus] = useState<Status>('idle');
  const [position, setPosition] = useState<GeolocationPosition | null>(null);

  const request = useCallback(() => {
    if (!('geolocation' in navigator)) return setStatus('unsupported');
    setStatus('pending');
    navigator.geolocation.getCurrentPosition(
      (p) => { setPosition(p); setStatus('granted'); },
      () => setStatus('denied'),
      // no watchPosition — a one-time "where am I browsing from" snapshot, not live tracking
    );
  }, []);

  useEffect(() => { request(); }, [request]); // auto-prompt on mount

  return { status, position, request };
}
```

`request` is exposed again for the on-map "locate me" button (a small persistent affordance so a user who denied the initial prompt, or just panned away, isn't stuck — `.area-map-locate`, absolutely positioned inside `.area-map-wrap`, a round icon button matching the app's existing inline-SVG icon-button style, `disabled` + a spinner glyph while `status === 'pending'`).

`ExplorePage` reacts to a resolved position:

```ts
useEffect(() => {
  if (geo.position) {
    setFlyTarget({
      center: [geo.position.coords.latitude, geo.position.coords.longitude],
      zoom: DEFAULT_LOCATION_ZOOM,
    });
  }
}, [geo.position]);
```

**Fallback / interim view — reusing what's already there, not computing one:** `AreaMap`'s `MapContainer` already opens on `center={SPAIN_CENTER} zoom={SPAIN_ZOOM}` (a deliberate existing default, not a placeholder). Denied/unsupported/still-pending geolocation simply means that Spain-wide view stays put until/unless a position resolves and the effect above flies away from it — no separate "fetch every area and compute a bounding box" step is needed (that would mean pulling the *entire*, geometry-laden `AreaMap`/`GET /areas` response with no bbox on every single page load just to render a fallback, which costs more than the feature is worth). This is a deliberate change from the original "fit-to-all-areas fallback" framing floated during design — once `SPAIN_CENTER`/`SPAIN_ZOOM` turned up as an existing, already-tuned default, reusing it is strictly simpler and cheaper for the same practical result.

**`DEFAULT_LOCATION_ZOOM`** — a new constant beside the existing `SPAIN_ZOOM`/`SUBAREA_ZOOM`, `9`: roughly the "~100km around" zoom level Web Mercator tiles give at that scale (each zoom step roughly halves ground distance shown; `6` is country-scale, `12` — `SUBAREA_ZOOM` — is already tuned as "close enough to see one crag's sectors", so `9` sits between the two as the regional scale asked for). Approximate by design (Q2) — not a computed radius.

### 6. Clustering

Add `react-leaflet-cluster@^4.1.3` (wraps `leaflet.markercluster@^1.5.3`, itself added as a direct dependency; `@types/leaflet.markercluster@^1.5.6` as a dev dependency) — confirmed compatible with the app's existing `react-leaflet@^5.0.0`/`react@^19` (its own peer deps: `react-leaflet ^5.0.0`, `react ^19.0.0`). Both new packages load inside `AreaMap`'s existing `React.lazy` chunk (plan 23), so they don't touch the main bundle `frontend/CLAUDE.md` already tracks the size of.

```tsx
import MarkerClusterGroup from 'react-leaflet-cluster';
import 'leaflet.markercluster/dist/MarkerCluster.css';
import 'leaflet.markercluster/dist/MarkerCluster.Default.css';
...
<MarkerClusterGroup>
  {topLevelAreas.map((area) => <Marker key={area.id} ... />)}
</MarkerClusterGroup>
```

Only the top-level `Marker`s are wrapped — **subareas render as `Polygon` outlines, not markers, today and after this plan**; a marker-clustering library clusters point markers, so there's nothing for it to do to a polygon layer. (This resolves, rather than contradicts, the earlier design discussion about "clustering subareas too if still dense" — that scenario doesn't arise given the codebase's actual marker/polygon split; if subareas ever gain their own markers, wrapping them in a second `MarkerClusterGroup` is the same one-line pattern.) Clicking a cluster zooms in to expand it — `leaflet.markercluster`'s default (`zoomToBoundsOnClick: true`), no configuration needed.

### 7. List content

- **Sort:** `sortByDistance` (§1), by distance from the map's current center.
- **Subtitle:** the old list's "Con sectores"/"Sin sectores" came from `AreaNode.hasChildren` (`GET /areas/roots`'s DTO), which `AreaSummary`/`AreaDto` (the bbox endpoint) doesn't carry. Since the list already holds the full currently-loaded `areas` array, compute the same fact client-side instead — `areas.some(a => a.parentAreaId === area.id)` — for a top-level row; a subarea row shows its parent's name as a breadcrumb instead (`activeTopLevelArea!.name`), since sibling subareas across different top-level areas can share a name (e.g. "Sector Norte") and the list has no other way to disambiguate them.
- **Empty state:** `visibleAreas.length === 0` → the existing `EmptyState` component, same as today's zero-zones case, just re-triggered per viewport instead of per whole dataset — *"No hay zonas en esta vista — aleja el mapa o desplázate."*
- **No search/filter box** — the bbox already narrows results, and `/routes` already covers name search; out of scope for this pass.

## Non-goals

- No backend endpoint change. `GET /areas` is reused exactly as `AreaMap` already calls it.
- No pagination or search on the viewport-bounded list.
- No URL/shareable map state — selection and view are transient, reset on reload.
- No continuous (`watchPosition`) location tracking.
- `AreaPolygonModal`'s own `.modal-wide .area-map` usage is untouched.

## Steps

1. **`design-system/src/components/List/`** — add `selected?: boolean` to `ListRowProps`, appending `list-row-selected`; `.list-row-selected` rule in `List.css`. Note it in `/DESIGN.md`'s List/ListRow entry.
2. **`frontend/package.json`** — add `react-leaflet-cluster`, `leaflet.markercluster`; add `@types/leaflet.markercluster` as a dev dependency. `bun install`.
3. **`frontend/src/lib/exploreAreas.ts`** — `deriveVisibleAreas`, `sortByDistance`, `boundsOfPolygon`, pulled out of `AreaMap`'s current `useMemo`s.
4. **`frontend/src/hooks/useViewportAreas.ts`** — the debounced/guarded fetch, moved out of `AreaMap` unchanged in behavior.
5. **`frontend/src/hooks/useGeolocation.ts`** — new, per §5.
6. **`frontend/src/components/AreaMap.tsx`** — rewrite to the controlled-component shape (§2): props instead of internal fetch/state, `mapRef` + `flyTarget` effect, marker/polygon ref maps + `selectedAreaId` effect, `eventHandlers.click` wiring `onSelectArea`, `MarkerClusterGroup` around top-level markers, `.area-map-locate` button wired to `onLocateMe`/`locating`.
7. **`frontend/src/pages/ExplorePage.tsx`** — rewrite: `useViewportAreas`, `useGeolocation`, `selectedAreaId`/`flyTarget`/`mobileListOpen` state, `deriveVisibleAreas`/`sortByDistance` for the list, `handleSelectArea`, the two-column markup.
8. **`frontend/src/styles.css`** — `.explore-page` grid rewrite, `.explore-list`/`.explore-list-scroll`/`.explore-list-handle`/`.area-map-locate`, base `.area-map`/`.area-map-wrap` → `height: 100%`, mobile bottom-sheet block (must land above the `720px` block's closing brace, per the file's existing "mobile block stays last" rule).
9. **`frontend/CLAUDE.md`** — update the `ExplorePage`/`AreaMap` bullets (controlled-component shape, new hooks, `react-leaflet-cluster`/`leaflet.markercluster` alongside the existing Leaflet/`leaflet-draw` mention), add `useGeolocation`/`useViewportAreas` to the shared-hooks list.
10. **Verify with the `run-acordanza` skill** — this plan's real risk is entirely visual/interactive and can't be judged from code: the geolocation permission prompt and its fallback/interim view, cluster formation and expand-on-click at a few zoom levels, list↔map sync in both directions, the no-page-scroll two-column layout and its scroll containment, and the mobile bottom sheet.

## Testing

- **`design-system/`**: if convenient, a `List.test.tsx` case that `selected` renders `list-row-selected` — `List` has no existing test file, and this isn't business logic, so this is a nice-to-have, not a blocker (repo convention: "not a coverage target").
- **`frontend/src/lib/exploreAreas.test.ts`** (pure, no rendering): `deriveVisibleAreas` — below `SUBAREA_ZOOM` only top-level areas are grouped, `activeTopLevelArea` is null; at/above it, the closest top-level area to `center` is picked and its children become `subareas`. `sortByDistance` orders by distance to `center`, areas with no centroid sort last (or are excluded — decide during implementation, whichever `AreaMap`'s existing `centroidOf` null-handling already implies). `boundsOfPolygon` returns the correct `[[minLat,minLng],[maxLat,maxLng]]` for a known vertex set.
- **`frontend/src/hooks/useGeolocation.test.ts`** (mock `navigator.geolocation.getCurrentPosition`): auto-requests on mount; `granted`/`denied`/`unsupported` transitions; `request()` re-invokes.
- **`frontend/src/pages/ExplorePage.test.tsx`** (new — none exists today), MSW-backed: renders the list from a mocked `GET /areas` response bounded to the initial viewport; the empty state when the mock returns `[]`; selecting a row calls through to a fly target (assert on props passed to a mocked `AreaMap`, since real Leaflet rendering is out of scope for this layer per `frontend/CLAUDE.md`).
- **`AreaMap` itself stays untested at the rendering level**, per the existing, explicit `frontend/CLAUDE.md` carve-out (jsdom has no real layout for Leaflet) — its new ref/effect wiring is exercised through `ExplorePage`'s props-in, and confirmed for real in step 10's manual pass.
