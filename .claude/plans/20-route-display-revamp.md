# Revamp route display: denser cards and a sectioned detail modal

Status: done
Issue: not filed yet — file one and link it here before the `plan:` commit
Depends on: [16](16-route-new-fields.md) (`Length`/`ObligatoryGrade`/`RecommendedGear`/`OptionalGear`/`Links` already exist on `RouteDto`/`RouteDetailDto` and the create/edit form, but were deliberately never rendered anywhere — "route information will be revamped in a following issue". This is that issue) and [17](17-grade-scale-and-pills.md) (`gradeBand`, the French-scale-to-color-band function this reuses for the card's left accent). Purely visual/presentational — one small backend DTO addition (`MyTickDto.Length`, §2), no migration, no routing change. Independent of [21](21-route-slug-and-url.md) (the shareable route URL) — either can ship first, in either order.

## Problem

Two independent complaints about how a route is shown today, both real:

1. **`RouteCard` (`frontend/src/components/RouteCard.tsx`) is sparse and low-signal.** It shows grade, name, area path, a gear label, a topo/tick presence icon, and (when non-approved) a status chip — nothing else. Orientation, length, and who opened it are all on the DTO already (plan 16) and never surface until the modal opens. `padding: var(--space-lg)` (16px) plus a `gap: var(--space-xs)` between four sparse rows reads as too much white space for how little information is there.
2. **`RouteDetailModal` (`../../frontend/src/components/RouteDetailModal.tsx`) is one undifferentiated scroll.** Header → description → first-ascensionist line → divider → topo grid → divider → tick button, with no visual grouping. Worse, it's missing content that already exists on `RouteDetail`: `length`, `obligatoryGrade`, `recommendedGear`, `optionalGear`, and `links` are fetched but never rendered anywhere in the app — the exact gap plan 16 named and deferred.

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
- **Orientation + length join the existing gear line.** `[orientation && orientationLabels[...], length && `${length} m`, gear && gearLabels[...]].filter(Boolean).join(' · ')` on one `.rc-meta` line — same join-and-filter idiom `RouteDetailModal`'s `.detail-fa` orientation/gear line already uses, so no new pattern.
- **Tighten spacing.** Drop `.route-card`'s `padding` from `--space-lg` (16px) to `--space-md` (14px) and its `gap` from `--space-xs`... actually the fix here is less about the token values (which are already the smallest in the scale below `--space-lg`) and more about **row count**: today's 4 stacked flex children (`rc-top`, `rc-main`, chip, `rc-type`) become 3 (`rc-top` grade+icons, `rc-main` name+area, one meta line folding gear into it) — one fewer internal gap. `--space-xs` gap stays; measure against `run-acordanza` screenshots before tuning further, since "too much white space" is a visual judgment call best confirmed by looking at the rendered card, not by picking token values blind.

### 2. `TickedRouteCard`: brought fully in line

`TickedRouteCard` (`frontend/src/components/TickedRouteCard.tsx`) shares `.route-card`/`.rc-top`/`.rc-main`/`.rc-type` with `RouteCard` already, so the grade-band left accent and tightened spacing apply automatically once §1's CSS lands — no separate visual work needed there.

**Field parity requires one small backend addition.** `MyTickDto` (`backend/Acordanza.Api/Dtos/TickDtos.cs:17-31`) already carries `Orientation` but has no `Length` field — confirmed by checking the actual record, not assumed. Since `Length` already exists on `ClimbingRoute` and this is a plain read, add it to `MyTickDto`'s projection in `TicksController.GetMine` — one field, no migration. With that, `TickedRouteCard` gets the exact same orientation+length meta line `RouteCard` does, true parity rather than a partial one. The delete button (`RemoveButton`) and the tick-specific `.trc-tick` date/rating/notes row are untouched — those aren't part of what's "too sparse" here.

### 3. `RouteDetailModal`: sectioned single column, no tabs

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
- **Divider language: full replacement, decided.** Every section (existing and new alike) becomes a bordered `.detail-section` block; the old `hr.divider` element is removed entirely rather than mixed with the new section styling — one consistent visual language for the whole modal, not two.

### Non-goals

- **No tick history or social features in the modal** — explicitly ruled out; ticks stay private-per-user, the modal stays write-only.
- **No topo thumbnails on cards** — explicitly ruled out; `hasTopos` stays a boolean icon.
- **No tabs in the modal** — sectioned single column only.
- **No shareable URL / routing change** — that's [plan 21](21-route-slug-and-url.md), split out specifically so this visual work isn't blocked on it.

## Steps

1. **Backend, small.** `Dtos/TickDtos.cs`: add `Length` to `MyTickDto`; `TicksController.GetMine`'s projection populates it from `route.Length`.
2. **Regenerate the OpenAPI contract** (`ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true`, Postgres+Azurite up, `Jwt:Key` set) then `bun run generate-api` in `frontend/`; commit both diffs.
3. **`RouteCard.tsx`** — grade-band left accent (`gradeBand` import), orientation+length in the meta line, layout/spacing per §1.
4. **`TickedRouteCard.tsx`** — inherits shared CSS automatically; add the orientation+length meta line now that `MyTickDto` carries both.
5. **`RouteDetailModal.tsx`** — stats row, links section, recommended/optional gear section, per §3; `styles.css`'s `.detail-*` rules reworked into the `.detail-section` block language, `hr.divider` removed.
6. **Tests** (below).
7. **Docs.** `backend/CLAUDE.md`: one-line mention of `MyTickDto.Length`. `frontend/CLAUDE.md`: `RouteCard`/`TickedRouteCard`/`RouteDetailModal` bullets updated for the new fields/sections.
8. **Verify** with the `run-acordanza` skill: screenshot cards across a few grade bands to confirm the left-accent color and spacing read cleanly, and one modal with all-new fields populated (length, obligatory grade, gear notes, links) plus one with them empty (confirming sections cleanly omit).

## Testing

**Backend** (`TicksTests.cs`, extend existing): `GET /ticks/mine` response includes `length`.

**Frontend:**
- `RouteCard.test.tsx` (extend) — grade-band class applied per grade string (reuse `gradeBand`'s existing test cases as the source of truth), orientation/length rendered when present and omitted when absent.
- `TickedRouteCard.test.tsx` (extend) — orientation/length rendered from `MyTickDto`.
- `DetailModal.test.tsx` (extend) — stats row, links, gear-notes sections render when populated and are absent when not; existing validate/reject/edit/delete/topo/tick permission-matrix assertions untouched.

`dotnet test Acordanza.slnx` from the repo root (Docker up) and `bun run test` + `bun run build` in `frontend/`.
