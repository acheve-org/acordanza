# Code-split Leaflet out of the initial bundle

Status: not started
Depends on: nothing. Frontend only — no backend change, no DTO change, no OpenAPI regeneration, no visual change on any page.

## Problem

`bun run build` emits **one** JavaScript chunk:

```
dist/assets/index-*.css    45.45 kB │ gzip:  11.83 kB
dist/assets/index-*.js    608.87 kB │ gzip: 177.69 kB
(!) Some chunks are larger than 500 kB after minification.
```

Vite has been printing that warning for a while. Splitting the same build by vendor group (a throwaway probe config, not committed) shows where the weight actually is:

| group | raw | gzip |
|---|---|---|
| `leaflet` + `leaflet-draw` + `react-leaflet` | 237.91 kB JS + 19.98 kB CSS | **67.93 + 7.21 kB** |
| `react` + `react-dom` + `react-router` | 219.60 kB | 70.68 kB |
| `@tanstack/react-query` | 37.33 kB | 11.04 kB |
| the app's own code | 113.15 kB | 27.48 kB |

Leaflet is **~38% of the JS** and ~75 kB gzip once its stylesheet is counted — more than every page, component and API module this app has ever written, combined (27 kB). And it is imported by exactly two files:

- `components/AreaMap.tsx` — rendered only by `pages/ExplorePage.tsx` (`/explore`).
- `components/AreaPolygonModal.tsx` — rendered only by `components/AreaTree.tsx`, itself only reachable under `/manage/areas` and `/my-acordanza/my-areas`, and only after clicking "Dibujar polígono" on a row.

Both are static imports, so every visitor downloads and parses all of it before the app renders — including the ones who land on `/`, search routes at `/routes`, browse aperturistas at `/fa`, or log a tick under `/my-acordanza`, and never open a map at all. On the phone-on-a-crag connection this app is actually used from, that is the single most expensive thing in the build.

## Design

### 1. Lazy the two Leaflet components, and nothing else

```tsx
// pages/ExplorePage.tsx
const AreaMap = lazy(() => import('../components/AreaMap').then((m) => ({ default: m.AreaMap })));
```

```tsx
// components/AreaTree.tsx
const AreaPolygonModal = lazy(() => import('./AreaPolygonModal').then((m) => ({ default: m.AreaPolygonModal })));
```

The `.then(m => ({ default: m.X }))` wrapper is needed because both components are named exports and `React.lazy` wants a module with a `default`. Converting them to default exports instead would be the smaller diff, but the codebase uses named exports uniformly (every component, every page) — one file breaking that rule to save a line reads as an accident later.

**Only these two.** Route-level `lazy()` on the other 20 pages is the obvious next thought and it should be resisted: after Leaflet leaves, *all* the app's own code is 27 kB gzip, so per-route splitting would trade a measurable one-time cost for ~1–2 kB per route plus a request waterfall and a spinner on every navigation. The whole win here is one library that 90% of sessions never need; take it and stop.

### 2. Suspense boundaries

**`ExplorePage`** renders `<AreaMap />` inline, above the zones list, so it needs a placeholder that does not move the list when the chunk lands:

```tsx
<Suspense fallback={<div className="area-map-placeholder" />}>
  <AreaMap />
</Suspense>
```

`.area-map-placeholder` in `styles.css` gets the same `height` and `border-radius` the map container already has (see `.area-map` there), plus `background: var(--surface-2)` — reserving the box rather than collapsing it. No spinner: the global `LoadingBar` (`main.tsx`) already signals in-flight work, and a second spinner inside a reserved box is noise.

**`AreaPolygonModal`** is already rendered conditionally (`{polygonModal && <AreaPolygonModal … />}`), triggered by a click, so its boundary can be minimal:

```tsx
{polygonModal && (
  <Suspense fallback={null}>
    <AreaPolygonModal … />
  </Suspense>
)}
```

`fallback={null}` is right here and wrong above: there is no layout to preserve (the modal is an overlay that does not exist yet), and the click that opened it already fired the `LoadingBar`. A brief nothing is better than a flash of empty modal chrome.

### 3. What comes along for free

Vite splits CSS per async chunk, and both components import their stylesheets themselves (`leaflet/dist/leaflet.css`, `leaflet-draw/dist/leaflet.draw.css`) — so the ~20 kB of Leaflet CSS moves into the lazy chunks with the JS, without touching `styles.css`. The handful of Leaflet *overrides* that live in `src/styles.css` (`.leaflet-popup-content-wrapper`, `.leaflet-container a.leaflet-popup-close-button`, around line 807) stay in the main stylesheet: they are ~4 lines, they are app theming rather than library CSS, and moving them would mean a component-scoped stylesheet, which this app deliberately does not have (`frontend/CLAUDE.md`, Styling).

`AreaMap`'s three marker-image imports (`marker-icon-2x.png`, `marker-icon.png`, `marker-shadow.png`) and its `L.Icon.Default.mergeOptions` fix-up are module-level side effects inside the lazily-loaded module, so they run when the chunk loads — which is exactly when the map is about to mount. No change needed there, but it is the one thing to actually look at in the browser (§Verify): a regression here shows up as missing marker icons, not as an error.

### 4. Rejected: vendor chunking via `build.rolldownOptions`

Splitting `react`/`leaflet` into named vendor chunks — what the Vite warning's second bullet suggests — improves nothing that matters. The imports stay static, so the browser still downloads every chunk before first render; all it buys is better cache granularity across deploys. The probe config in §Problem was built for measurement only and is deliberately not part of this plan.

## Steps

1. `pages/ExplorePage.tsx`: `lazy` + `Suspense` around `<AreaMap />`.
2. `src/styles.css`: add `.area-map-placeholder`, matching `.area-map`'s height/radius. It is a base rule, so it goes **above** the `@media (max-width: 720px)` block at the bottom of the file — and if `.area-map` has a mobile height override in that block, the placeholder needs the same one (`frontend/CLAUDE.md`, Styling: the breakpoint block must stay last).
3. `components/AreaTree.tsx`: `lazy` + `Suspense` around `<AreaPolygonModal />`.
4. `bun run build` and record the new chunk table (§Verify).
5. `frontend/CLAUDE.md`: the `AreaMap`/`AreaPolygonModal` sentences in the `components/` bullet gain a note that both are lazily imported and why — otherwise the next person "simplifies" a `lazy()` back to a static import, and the 75 kB quietly returns with nothing failing.

## Verify

**Build output** is the assertion here, since this plan has no behavioural surface:

- `bun run build` produces at least three JS chunks, the entry chunk drops by roughly 238 kB raw / 68 kB gzip, the Leaflet CSS (~20 kB) is no longer in `index-*.css`, and the >500 kB warning is gone.
- Record before/after in the PR description; that number is the whole point of the change.

**Behaviour**, via the `run-acordanza` skill (jsdom cannot test Leaflet — `frontend/CLAUDE.md` puts map rendering explicitly out of scope for the Vitest layer):

- `/explore` renders the map, markers show their icons (the §3 side-effect check), and the zones list below does not jump when the chunk lands.
- `/manage/areas` → "Dibujar polígono" on a row opens the modal with the map and the draw toolbar, saving a polygon still works.
- With the browser network tab open on `/routes`: no `leaflet` chunk is requested; navigating to `/explore` requests it once.

**Existing tests** must stay green unchanged: `bun run test` (32 files, 126 tests). `AreaTree.test.tsx` renders `AreaTree` and never opens the polygon modal, so the lazy import simply means that module is no longer even loaded during that test — if it starts failing, the `Suspense` boundary is in the wrong place.
