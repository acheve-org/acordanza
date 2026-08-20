# Icon component + a curated icon library, replacing text/emoji stand-ins

Status: proposed
Issue: [#242](https://github.com/acheve-org/acordanza/issues/242)
Depends on: [28](28-orientation-compass-rose.md) as precedent — `CompassRose` is the existing example of a hand-authored, geometry-in-a-pure-module inline SVG component with a `size` prop and no external asset pipeline, and this plan follows the same shape. No backend or data-model changes.

## Problem

There is no `Icon` component or icon library anywhere in the codebase today — confirmed by inventory. Every icon-like thing is one of:

- **A hand-rolled inline `<svg>`, duplicated.** `MainNav.tsx` and `SideNav.tsx` each carry a byte-identical hamburger `<svg><path d="M3 6h18v2H3zm0 5h18v2H3zm0 5h18v2H3z" /></svg>` — two copies of the same icon that will silently drift the next time one gets tweaked and the other doesn't.
- **A literal text/Unicode glyph doing an icon's job.** `RemoveButton.tsx:18` renders `&times;` as its only content; `Chip` inherits that via `RemoveButton`. `AreaTree.tsx`'s expand toggle renders `{expanded ? '▾' : '▸'}` as plain text. `AreaPage.tsx:104,109` prints `✓ Validado por …` / `✗ Rechazado por …` with the checkmark/cross as literal characters. `TreeSelect.tsx`'s row toggle renders `{expanded ? '−' : '+'}` as text — the same plus/minus glyph its own "add a child here" button (`TreeSelect.tsx:406`, also literal `+`) uses for a *different* action, which is the one real semantic clash the inventory turned up.
- **A plain-text button with no glyph at all**, for actions that are iconified everywhere else: "Editar"/"Eliminar" across `AreaTree`, `RouteDetailModal`, `FirstAscensionistsPage`, `RouteFormPage`, `MyImportsPage`; "Aprobar"/"Rechazar" in `PendingRequestsList`, `RouteDetailModal`, `PendingTasksPage`, `RejectReasonModal`; "Ordenar por" column buttons in `UsersPage`.

Two visual conventions are already in unacknowledged competition: `App.tsx`'s bell/gear/logout/avatar icons are solid (`fill="currentColor"`, no stroke), while `HomePage.tsx`'s four card icons are outline (`fill="none" stroke="currentColor" strokeWidth={1.75}`). Building the library without picking one now just bakes the inconsistency in permanently.

## Design

Three pieces, in dependency order: the `Icon` component itself (§1), the curated v1 registry (§2), and the specific call sites it replaces (§3).

### 1. `design-system/src/components/Icon/`

Mirrors the `Compass`/`GradePill` split — a pure data module plus a thin display component:

```
Icon.tsx        the component: name-keyed lookup, size, decorative/labelled switch
icons.ts        IconName union + Record<IconName, ReactNode> of raw <path>/<circle> children
Icon.css        one rule, the size-token → width/height mapping
```

**Visual convention: outline, not filled.** `HomePage`'s existing icons (`fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round"`) are adopted as the standard — this is also the convention widely-used open icon sets (Lucide, Feather) already draw in, so new icons can be adapted from there directly rather than hand-plotted from scratch, and existing outline icons (`HomePage`'s four) drop straight into the registry unchanged. `App.tsx`'s solid bell/gear/logout and `RouteCard`'s solid tick need **redrawing**, not copy-pasting, when they migrate (§3) — flagged here so it isn't discovered mid-implementation.

```tsx
// icons.ts
export type IconName =
  | 'edit' | 'delete' | 'add' | 'search' | 'check' | 'close' | 'reject'
  | 'chevron' | 'menu' | 'bell' | 'settings' | 'logout' | 'sort'

export const icons: Record<IconName, ReactNode> = {
  edit: <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />, // + pencil path, per icon
  // …
}
```

```tsx
// Icon.tsx
export interface IconProps {
  name: IconName
  size?: 'sm' | 'md' | 'lg'   // 16 / 20 / 24px — matches the sizes already in use (18–24px) across the audit
  /** Accessible name. Omit for a decorative icon sitting beside visible text (the default —
   *  most call sites already carry a Spanish label); pass it when the icon is the button's
   *  only content. */
  label?: string
  className?: string
}

export function Icon({ name, size = 'md', label, className }: IconProps) {
  const classNames = ['icon', `icon-${size}`, className].filter(Boolean).join(' ')
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={classNames}
      aria-hidden={label ? undefined : true}
      role={label ? 'img' : undefined}
      aria-label={label}
    >
      {icons[name]}
    </svg>
  )
}
```

**Reject/check reuse the same `d` the audit already found working**: `check` is `RouteCard.tsx`'s existing tick path (`M5 13l4 4L19 7`, already stroke-based — a genuine as-is graft, not a redraw), and `chevron` is a single path rotated by CSS (`transform: rotate(90deg)` when open) rather than two separate glyphs — the same technique `styles.css:663-665`'s `.route-form-details summary::before` already uses for the "Más detalles" disclosure, so `Icon` unifies with that convention too instead of adding a third.

**No new size tokens beyond the three above** — `--space-*` already covers gaps around an icon; only `icon-sm`/`icon-md`/`icon-lg` width/height rules are new, added to `Icon.css`.

**Not built now (explicitly deferred, not silently dropped):**
- `external-link`, `upload`, `download`, `lock`, `calendar`, `filter` — no current call site needs them (Q13); add on demand when a page actually wants one, same as every other icon here started from a real replacement, not a guess.
- A `Modal` close button (`×`) — `Modal` has no close affordance today (Escape/overlay-click/caller `ModalActions` button only) and adding one is new UX behaviour affecting every modal's layout, not an icon swap (Q12). Worth its own follow-up plan once this one lands, since `Icon`'s `close` name will already exist for it to use.

### 2. v1 registry — 13 icons

| name | replaces |
|---|---|
| `edit` | plain-text "Editar" buttons: `AreaTree`, `RouteDetailModal`, `FirstAscensionistsPage`, `RouteFormPage` |
| `delete` | plain-text "Eliminar" buttons: `AreaTree`, `RouteDetailModal`, `FirstAscensionistsPage`, `MyImportsPage` |
| `add` | plain-text/`+`-prefixed add actions: `AreaPicker`, `FirstAscensionistPicker`, `AreaTree`'s "+ Subsector", `TreeSelect`'s per-row `+` add-child button |
| `search` | canonicalizes `HomePage`'s existing "Busca" card icon (already the target visual style — dropped in as-is) |
| `check` | `RouteDetailModal`/`PendingTasksPage` "Validar", `AreaPage`'s literal `✓`, `RouteCard`'s existing tick `<svg>` (graft, not redraw — see §1) |
| `reject` | `RouteDetailModal`/`PendingTasksPage`/`PendingRequestsList`/`RejectReasonModal` "Rechazar"/"Aprobar" pairing, `AreaPage`'s literal `✗` |
| `close` | `RemoveButton`'s literal `×` (and by extension `Chip`, which composes `RemoveButton`) |
| `chevron` | `AreaTree`'s literal `▾`/`▸`, `TreeSelect`'s row-expand `−`/`+` (normalized to the same rotate-on-open pattern — resolves the `+`/add-child clash noted above) |
| `menu` | dedupes `MainNav.tsx` + `SideNav.tsx`'s identical inline hamburger `<svg>` |
| `bell` | `App.tsx`'s notification bell (redraw to outline, see §1) |
| `settings` | `App.tsx`'s gear (redraw to outline) |
| `logout` | `App.tsx`'s logout icon (redraw to outline) |
| `sort` | `UsersPage`'s "Ordenar por" column buttons (currently no glyph at all — new addition, not a redraw) |

**Not touched by this plan** (existing, working, semantically specific — see Q10's reasoning): `RouteCard`'s topo-camera icon, `AreaMap`'s spinner/crosshair, `Brand`'s wordmark mark, `CompassRose`/`CompassPicker`, `ExplorePage`/`PendingTasksPage`'s `--depth-root`/`--depth-subarea`-colored root/subarea icons.

### 3. Call sites

Straight glyph-for-`<Icon>` swaps, each keeping its existing accessible name:

- **`RemoveButton.tsx`** — `&times;` → `<Icon name="close" size="sm" />`. `label` prop stays on the `<button>` itself (unchanged); the icon inside stays decorative (no `Icon` `label`) since the button already carries `aria-label`.
- **`AreaTree.tsx`** — expand toggle: `{expanded ? '▾' : '▸'}` → `<Icon name="chevron" className={expanded ? 'icon-rotated' : undefined} />`, `aria-label` unchanged on the wrapping `<button>`. "Editar"/"Eliminar"/"+ Subsector" `Button`s gain a leading `<Icon name="edit"/"delete"/"add" size="sm" />` alongside their existing text (icon+text, per Q6 — these aren't compact enough to go icon-only).
- **`TreeSelect.tsx`** — row toggle `{expanded ? '−' : '+'}` → `<Icon name="chevron" .../>` (rotated, same as `AreaTree`); the separate `onCreateChild` `+` button → `<Icon name="add" size="sm" label="Añadir subsector aquí" />` (icon-only, `aria-label`/`title` already present on the button — the `Icon` itself can drop `label` since the button supplies it, matching the `RemoveButton` pattern above).
- **`Chip.tsx`** — no direct change; inherits the `RemoveButton` fix.
- **`AreaPage.tsx`** — `✓ Validado por …` / `✗ Rechazado por …` → `<Icon name="check"/"reject" size="sm" />` prefixing the same text.
- **`RouteCard.tsx`** — `hasTick`'s hand-rolled `<svg>` → `<Icon name="check" size="sm" label="Ya la has registrado" />` (same path, now sourced from the shared registry instead of a local copy).
- **`RouteDetailModal.tsx`** — "Validar"/"Rechazar"/"Editar"/"Eliminar" `Button`s each gain a matching leading icon (`check`/`reject`/`edit`/`delete`), text kept.
- **`FirstAscensionistsPage.tsx`**, **`RouteFormPage.tsx`**, **`MyImportsPage.tsx`** — same edit/delete icon+text treatment.
- **`AreaPicker.tsx`**, **`FirstAscensionistPicker.tsx`** — "create new" sub-form triggers gain a leading `add` icon.
- **`PendingRequestsList.tsx`**, **`PendingTasksPage.tsx`**, **`RejectReasonModal.tsx`** — "Aprobar"/"Validar" and "Rechazar" gain `check`/`reject` icons.
- **`UsersPage.tsx`** — "Ordenar por" column buttons gain a leading `sort` icon (new — there's no existing glyph here to replace).
- **`MainNav.tsx`, `SideNav.tsx`** — both inline hamburger `<svg>`s → `<Icon name="menu" />`, dedup complete.
- **`App.tsx`** — bell/gear/logout inline `<svg>`s → `<Icon name="bell"/"settings"/"logout" />` (redrawn outline versions land in `icons.ts`, not copy-pasted). The avatar/profile-trigger `<svg>` (circle + shoulders) is **not** in the v1 list — it's a bespoke person silhouette, not one of the 13, so it stays as-is; note this explicitly so it isn't mistaken for an oversight during review.
- **`HomePage.tsx`** — the "Busca" card's magnifier `<svg>` → `<Icon name="search" size="lg" />` (the other three cards' icons — map pin, flag, profile — aren't in v1 and stay as their own inline `<svg>`s, same reasoning as the `App.tsx` avatar above).

### Non-goals

- **No `Modal` close button** (§1) — new UX behaviour, deferred to its own plan (Q12).
- **No external-link indicator** — new UI on links that have none today, deferred (Q13).
- **No sprite sheet / build step.** Inline SVG per icon, matching `CompassRose`'s precedent — no bundler config, no generated asset.
- **No redraw of `CompassRose`/`Brand`/`AreaMap`/`ExplorePage`'s depth icons.** Semantically specific (geometry-driven, or tied to `--depth-*` tokens) — folding them into a generic name-keyed registry would either lose that specificity or force the registry to grow color/token props no other icon needs. Left alone; a candidate for a later plan if it ever becomes a real duplication problem.
- **No icon-only conversion of text-labeled buttons that already have room for both.** Per Q6, existing text stays; icons are additive except where an icon-only pattern already existed (`RemoveButton`, `TreeSelect`'s add-child button).

## Steps

1. **Design system.** `Icon.tsx`, `icons.ts` (13 entries), `Icon.css`; export `Icon`, `IconName`, `icons` from `design-system/src/index.ts`; document the component in `DESIGN.md`'s Components section alongside the existing 13 (component count line at `DESIGN.md:170-172` needs updating too).
2. **`RemoveButton.tsx`** and, transitively, **`Chip.tsx`**.
3. **`AreaTree.tsx`**, **`TreeSelect.tsx`** — chevron normalization + edit/delete/add icons.
4. **`AreaPage.tsx`** — `✓`/`✗` replacement.
5. **`RouteCard.tsx`**, **`RouteDetailModal.tsx`** — tick graft + validate/reject/edit/delete icons.
6. **`FirstAscensionistsPage.tsx`**, **`RouteFormPage.tsx`**, **`MyImportsPage.tsx`**, **`AreaPicker.tsx`**, **`FirstAscensionistPicker.tsx`** — edit/delete/add icons.
7. **`PendingRequestsList.tsx`**, **`PendingTasksPage.tsx`**, **`RejectReasonModal.tsx`** — check/reject icons.
8. **`UsersPage.tsx`** — sort icon (new).
9. **`MainNav.tsx`, `SideNav.tsx`** — hamburger dedup.
10. **`App.tsx`** — bell/gear/logout redraw + swap.
11. **`HomePage.tsx`** — search icon canonicalization.
12. **Tests** (below), then `bun run test` + `bun run build` in `design-system/` and `frontend/`, `bun run lint`.
13. **Docs.** `frontend/CLAUDE.md`: note `RemoveButton`/`AreaTree`/`TreeSelect`/etc. now render via `Icon` where relevant, in the existing per-component bullets rather than a new one. `DESIGN.md`: the new `Icon` entry.
14. **Verify** with the `run-acordanza` skill — a screenshot pass over the touched pages (nav hamburger, area tree expand/collapse, route detail's action buttons, users sort bar) to catch anything that reads wrong at real size, since stroke-based redraws of previously-filled icons (bell/gear/logout) are a visual judgment call that's easier to confirm on screen than in code review.

## Testing

**Design system** (`design-system/`, its own Vitest):
- `Icon.test.tsx` — every `IconName` renders exactly one `<svg>` with no thrown error (guards against a name added to the union but not to `icons`); default render is `aria-hidden="true"` with no `role`; passing `label` switches to `role="img"` with the matching `aria-label` and drops `aria-hidden`; `size` maps to the expected CSS class.

**Frontend** (per touched component, extending existing test files rather than adding new ones):
- `RemoveButton`/`Chip` — existing tests keep passing unchanged (same accessible name, same click behaviour); no new assertions needed since the visible glyph was never what was tested.
- `AreaTree.test.tsx`/`TreeSelect.test.tsx` — expand/collapse still toggles on click and reports the correct `aria-expanded`/`aria-label`; the chevron's rotation is a CSS class, asserted by class presence, not by rendering.
- `RouteCard.test.tsx` — `hasTick`'s icon still renders only when `hasTick` is true, with its accessible name (`Ya la has registrado`) intact — same assertion the audit's existing test already made, now pointed at `Icon` instead of a local `<svg>`.
- No test asserts exact SVG path data anywhere — that's a visual concern for step 14's manual pass, not something jsdom-rendered tests can meaningfully check (same reasoning `frontend/CLAUDE.md`'s Testing section already gives for excluding Leaflet map rendering from this layer).
