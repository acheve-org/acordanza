# Make the compass legible: one needle rose, a labelled picker

Status: done
Issue: [#227](https://github.com/acheve-org/acordanza/issues/227)
Depends on: [28](28-orientation-compass-rose.md), which this revises — §1's `md` rose and `CompassPicker` are rewritten here, §2–§4's call sites survive untouched. No backend change, no migration, no data-model change, no new tokens, no change to any component's public props.

## Problem

Plan 28 shipped orientation as a compass rose. Two of its decisions didn't survive contact with the real app, and they fail for the same reason: **the wedge is a bad unit of meaning at every size this app draws it.**

**The `md` rose is a different picture from the `sm` rose.** `CompassRose` branches on `size`: `sm` (18px, `RouteMeta.tsx:18`) draws a hairline ring with a single needle, `md` (56px, `RouteDetailModal.tsx:132`) draws eight wedges plus the four cardinal letters. Plan 28 knew it was paying for this — "It costs the visual rhyme with the `md` rose, which is the price paid here" (§1) — on the assumption that the wedge view was worth having at 56px. It isn't. The detail modal already prints the full Spanish word directly beneath the rose (`RouteDetailModal.tsx:133`), so the eight wedges and the N/E/S/O letters are answering a question the adjacent text already answered, in a notation the route cards never taught anyone to read. One concept, two pictures, and the bigger one carries less information per pixel.

**The picker is unusable, in four specific ways.** `CompassPicker` renders eight `wedgePath` shapes in a 3×3 grid at 132px:

- **Nothing is labelled.** You cannot tell `northeast` from `east` without counting wedges clockwise from the top. Plan 28 §1 specified the fix — "The cardinal letters are one overlay SVG spanning the grid" — and it was never built: `Compass.css:91` still carries the dead `.compass-picker-letters` rule, a CSS hook for an element that has never existed. This is an unshipped plan item, not a design choice anyone made.
- **It doesn't look interactive.** An unselected wedge is `--chip-bg` (`rgba(0,0,0,.04)`) with a 1px `--border` (`rgba(0,0,0,.07)`) — against `--bg-card` that is very nearly nothing. Eight barely-there grey shapes read as an illustration, not as eight controls.
- **Multi-select is invisible.** `Filters` uses `multiple` (the default) and nothing on screen suggests you may pick three. The one interaction that motivated the entire feature — "what's in the shade this afternoon?" is *north, northeast and northwest* (plan 28's Problem) — is the one the control never advertises.
- **The selection has no words.** After picking, the only readout is the picture you just made. `Filters` shows no summary; `RouteFormPage` shows none either.

And a fifth, found while auditing the tokens: **the keyboard focus indicator is broken.** `Compass.css:82` sets `stroke: var(--ink)` on the focused wedge, and `--ink` **is not defined anywhere in the repo** — `tokens.css:24` defines `--text`. It is the only `var(--ink)` in the codebase. The declaration is invalid at computed-value time, so the focus stroke is whatever `stroke` falls back to rather than the deliberate high-contrast outline plan 28 §1 specified ("the stroke lands exactly on the shape about to be toggled").

Notably **not** a problem: hit-target size. The 3×3 grid at 132px gives 44px cells, which is the touch minimum and tested fine. Any growth below is bought for legibility, never for reach.

## Design

Two components, in dependency order: the rose collapses to one drawing (§1), the picker keeps its wedges but gains labels, contrast and words (§2), and `compass.ts` absorbs the fallout (§3).

### 1. `CompassRose` — one drawing, two sizes

The `size` branch goes away entirely. Both sizes render the same three elements; `size` selects nothing but CSS `width`/`height`.

```tsx
export function CompassRose({ value, size = 'md' }: CompassRoseProps) {
  const index = orientations.indexOf(value)
  const label = `Orientación: ${orientationLabels[value]}`

  return (
    <svg className={`compass-rose compass-rose-${size}`} viewBox="0 0 100 100" role="img" aria-label={label}>
      <title>{label}</title>
      <circle className="compass-rose-ring" cx={50} cy={50} r={40} />
      <line className="compass-rose-north" x1={50} y1={0} x2={50} y2={6} />
      <path className="compass-rose-needle" d={needlePath(index, 50, 50, 35)} />
    </svg>
  )
}
```

**The north tick is geometry, not a letter.** A needle alone says "that way" but not "that way relative to what", and with the cardinal letters gone the 56px rose in the detail modal would be a bare arrow. The tick is a radial mark at 12 o'clock — and because it sits at exactly −90°, it needs no trigonometry at all: it is a vertical line at `x=50` from `y=0` to `y=6`, which is why it stays inline in the component rather than becoming a third export from `compass.ts`.

Sizing it is the one thing that must be checked at true size rather than reasoned about. At `sm` the viewBox scales by 0.18, so the tick's 6×7 units land at roughly 1.1×1.3 physical pixels — visible as a deliberate mark, but only just. If it disappears on a real card, the fallback is a heavier `stroke-width`, not a bigger radius: the tick must stay inside the viewBox (`r ≤ 50`), which is why the ring shrinks from `r=43` to `r=40` and the needle from `r=38` to `r=35` to clear room above the ring.

When the value *is* `north`, the needle's tip (`y=15`) sits below the ring band (`y=7.5–12.5`), which sits below the tick (`y=0–6`) — three separated marks on one axis, reading as "the needle agrees with north", which is correct and not a collision.

**Consequences.** `CARDINAL_LETTERS` is deleted. `.compass-rose-letter` is deleted. `wedgePath` loses its only display consumer and `needlePath` gains one — it now draws every rose in the app.

### 2. `CompassPicker` — labelled wedges

Everything structural about the picker is **kept**, because none of it is what's broken: the 3×3 CSS grid with the empty centre, the eight native `<input type="checkbox|radio">` controls inside `<label>`s, the transparent full-bleed input as hit target, the per-cell SVG scaled to 300% and offset by grid position, all state expressed as CSS on `:checked`/`:focus-visible`, and DOM order following `orientations` so Tab walks clockwise from north. Plan 28 §1's argument for native inputs over `<button aria-pressed>` — free arrow-key traversal, free roving focus, free checked state and accessible names — is untouched by anything here.

Four changes.

**Labels inside the wedges, upright.** Each cell's SVG gains a `<text>` alongside its wedge path, at the wedge's mid-radius:

```tsx
<svg viewBox="0 0 100 100" aria-hidden="true">
  <path className="compass-wedge" d={wedgePath(i, 50, 50, 46, 22)} />
  <text className="compass-wedge-label" {...labelPoint(i, 50, 50, 34)} textAnchor="middle" dominantBaseline="central">
    {orientationAbbr[o]}
  </text>
</svg>
```

The geometry already accommodates this without changing: the annulus runs `r=22` to `r=46`, so mid-radius is `r=34`, where 45° of arc gives ~26.7 units — about 47px at the new 176px size, against ~22px for a two-character label at `font-size: 10`. No widening of the band, no shrinking of the centre hole.

*Upright, never rotated.* Rotating each label to follow its wedge is what a decorative rose does, and it makes the four intercardinals — precisely the ones this fixes — the hardest to read.

*And the per-cell SVG is why plan 28's overlay never needed building.* Each cell's SVG is already the full 100×100 coordinate space, merely clipped to that cell, so a `<text>` placed at the wedge's own mid-angle lands in the right place with no extra element spanning the grid. `.compass-picker-letters` is deleted rather than implemented.

*The labels are `aria-hidden` by inheritance* — they live inside the `aria-hidden="true"` SVG, and the accessible name stays the input's `aria-label={orientationLabels[o]}` (the full Spanish word). Nothing must make `NE` the accessible name in place of `Noreste`.

**Contrast, on every state.** Unselected wedges keep `--chip-bg` but the stroke goes from `--border` to `--border-strong`, and the label is `--dim`. Selected keeps `--accent` and the label flips to `--on-accent`. Hover on an unselected wedge needs a new treatment because its old one (`fill: var(--chip-bg); stroke: var(--border-strong)`) is now the *resting* state — it becomes a fill step up, still gated behind `:not(:checked)` so hover never overpaints a selection. **And the focus rule's `var(--ink)` becomes `var(--text)`**, which is the fix for the undefined-token bug above.

**Two lines of text beneath the rose**, both owned by the component, which means its root becomes a wrapper around the existing `.compass-picker` grid rather than being that grid:

- *A hint, in `multiple` mode only*: «Puedes elegir varias.» in `--dim`. This is conditional on the mode and must stay so — `RouteFormPage` needs to communicate the exact opposite, that precisely one applies. A checkmark glyph on selected wedges was considered as a text-free alternative and rejected twice over: it crowds a wedge that now carries `NE`, and it only appears *after* a selection, which is too late to tell you the selection was allowed to be plural.
- *A summary, in both modes*: the selected orientations' full Spanish labels joined with ` · ` (`Norte · Noreste · Noroeste`), in `--dim`. In single mode it renders the one full word, which is genuinely more than the two-character wedge label gives. **With nothing selected it renders nothing** — not "Cualquiera", not "Sin especificar".

  That last point is the API boundary. Both call sites already render their own clear button outside the component with *different words for it* — `Filters.tsx:77` says «Cualquiera», `RouteFormPage.tsx:249` says «Sin especificar» — because a filter's "any" and a form field's "unspecified" are different ideas. That is domain language, not component language. The component cannot pick between them, so it says nothing and both buttons stay exactly where they are.

**176px, fixed.** Up from 132px, purely to give two-character labels room; `NO` at `font-size: 10` inside a 44px cell is cramped, and cramped labels would leave the first failure half-fixed. It stays a fixed size rather than becoming fluid, for plan 28 §1's reason — two call sites, and a scalable interactive rose buys nothing — and the numbers say it costs no layout work anywhere:

| Call site | Container | 176px fits? |
|---|---|---|
| `Filters` | `.layout`'s fixed **260px** sidebar (`styles.css:328`) | yes, 84px spare |
| `RouteFormPage` | `.grid-3`'s `auto` column (`styles.css:419`), inside `.picker-stack` | yes, the two `1fr` columns absorb 44px |
| both, <720px | `.grid-3` collapses to one column (`styles.css:990`); `.filters` goes static (`styles.css:989`) | yes, fits any phone width |

**The centre cell stays empty.** A live needle mirroring the selection is a real temptation — it is the one place the display rose and the picker would meet — but it is only meaningful when exactly one wedge is selected, and drawing a target-shaped thing in the middle of a control whose every other region is clickable invites clicks on a non-target. Using it as the clear affordance was already argued down in plan 28 §3 ("an empty rose already *means* 'any'"), and that reasoning survives this revamp intact.

### 3. `compass.ts`

**`wedgePath` stays.** It keeps exactly one consumer — the picker — but it is still pure geometry whose off-by-45° failure mode is invisible in review and obvious on screen. Inlining it into `CompassPicker.tsx` would trade a tested pure function for an untested inline one.

**`labelPoint` is added**, because placing text at a wedge's mid-angle is trigonometry and `compass.ts` is where the trigonometry lives (it already declares `point` "the only trigonometry in the module"). It returns `{ x, y }` rather than a path string, since it feeds SVG attributes, not a `d`:

```ts
// Where sector `index`'s label sits: on its centre angle, at radius `r`. Returns SVG
// coordinates rather than a path string — this feeds <text x= y=, not a `d`.
export function labelPoint(index: number, cx: number, cy: number, r: number): { x: number; y: number }
```

`orientationAbbr` — already exported, already used by `RouteMeta` — becomes the picker's label source too, so the Spanish `O`-for-Oeste convention has one definition.

### Non-goals

- **No sun/shade tinting.** Plan 28 §1 deferred *solana/umbría* colouring with "land the monochrome rose first, look at a real list, then decide". Decided: no, and not later either in this control. It fights the contrast fix head-on — tinting makes *unselected* wedges colourful, so "selected" can no longer mean "the coloured one" and would need a second visual channel to survive — and it still collides with the grade-band colours on `RouteCard`. If the idea has a home, it is the route card or the area page, and it is a separate issue.
- **No backend change, no data-model change.** A route still has exactly one orientation; `GET /routes` still takes repeated `orientation`. Nothing here touches the API, so no OpenAPI regeneration and no `bun run generate-api`.
- **No new tokens.** `--accent`, `--on-accent`, `--chip-bg`, `--border-strong`, `--dim`, `--text` all exist. The only token work is *fixing* a reference to one that never did.
- **No public prop changes.** `CompassRoseProps` and `CompassPickerProps` are byte-for-byte what they are today; every call site compiles unchanged.
- **The clear buttons stay at the call sites.** See §2.
- **No touch-target growth.** The picker grew for label room; 44px cells were already correct and the new 58px cells are a side effect, not a goal.
- **No third size, no fluid picker, no `lg` rose.** If a `lg` rose is ever wanted, §1's collapse makes it one CSS rule — that is the payoff, not an invitation.

## Steps

1. **`compass.ts`** — add `labelPoint`; export it from `design-system/src/index.ts`. `wedgePath`/`needlePath`/`orientationAbbr` unchanged.
2. **`CompassRose.tsx`** — delete the `size` branch and `CARDINAL_LETTERS`; render ring (`r=40`) + north tick + needle (`r=35`) unconditionally.
3. **`CompassPicker.tsx`** — add the `<text>` label per cell; wrap the grid in a field container; render the `multiple`-only hint and the both-modes summary line beneath.
4. **`Compass.css`** — `.compass-picker` 132px → 176px; new `.compass-wedge-label` (`font-size: 10`, weight 600, `--dim`, `pointer-events: none`) and its `:checked` flip to `--on-accent`; unselected stroke `--border` → `--border-strong`; new hover fill step; **`var(--ink)` → `var(--text)`** on the focus rule; new `.compass-picker-hint`/`.compass-picker-summary`; delete `.compass-rose-letter` and `.compass-picker-letters`.
5. **Tests** (below), then `bun run test` + `bun run build` in `design-system/`, then the same in `frontend/`, then `bun run lint`. No backend work and no `dotnet test` — nothing here crosses the API.
6. **Docs.** `/DESIGN.md`'s `CompassRose`/`CompassPicker` paragraph is rewritten: the `sm`/`md` split is now pixel size only, the `md` wedge view and cardinal letters are gone, and the picker gains labels, a hint and a summary. `frontend/CLAUDE.md` needs no edit — no call site's behaviour changes — but confirm the `Filters` bullet's description of the orientation control still reads true.
7. **Verify** with the `run-acordanza` skill, on three things that cannot be settled from code, in this order of doubt:
   1. **The north tick at 18px on a real route card.** ~1.1×1.3 physical pixels is the whole question; if it vanishes, raise `stroke-width` before touching radii (§1).
   2. **Two-character labels at 176px**, specifically `NO`/`SO` in the corner wedges, where the arc is the same but the eye has the least surrounding context.
   3. **Keyboard focus is now visible** — Tab into the picker and confirm the `--text` stroke actually paints, which is the thing the undefined token was hiding.

## Testing

All of it in `design-system/`'s own Vitest — nothing here reaches the frontend app or the API, so no test in `frontend/` changes.

**`compass.test.ts`** — keep every existing `wedgePath`/`needlePath` assertion unchanged; add `labelPoint`: sector 0 is directly above the centre (`x === cx`, `y < cy`), sector 2 directly right (`y === cy`, `x > cx`), and the eight points are pairwise distinct. Same rationale as the existing path tests — an off-by-45° here puts `NE` on the east wedge, which is invisible in review and glaring on screen.

**`CompassRose.test.tsx`** — **two existing cases are deleted**, not adapted, since they pin behaviour that ceases to exist:

- ~~`md draws eight wedges and no needle`~~
- ~~`md marks the selected sector`~~

Replaced by: both `sm` and `md` draw exactly one needle and zero wedges; both draw the north tick; the `role="img"` Spanish-label case is kept as is.

**`CompassPicker.test.tsx`** — every existing case is kept verbatim (eight checkboxes named in Spanish, add/remove through `onChange` in compass order, `toBeChecked()` reflecting `value`, `multiple={false}` rendering radios and replacing rather than accumulating, Tab reaching every sector in compass order). Added:

- The eight abbreviations render as text (`NE`, `SO`, …) **and the accessible names are still the full Spanish words** — `getByRole('checkbox', { name: 'Noreste' })` must keep passing, which is the assertion that stops the visible label leaking into the accessible name.
- The hint renders in `multiple` mode and does **not** render with `multiple={false}`.
- The summary lists the selected labels in compass order in both modes, and renders nothing when the selection is empty.

Plan 28 §1's standing exclusion holds: **don't assert the radio group's arrow-key traversal.** It's UA behaviour the design exists to inherit, jsdom doesn't implement it, and a test there asserts jsdom rather than the component. It gets checked by hand in step 7.
