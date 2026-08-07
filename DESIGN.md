---
version: alpha
name: acordanza
direction: "Cool Minimal"

colors:
  primary: "#3f3fe0"
  primary-deep: "#3634c4"
  on-primary: "#ffffff"
  canvas: "#eef1f5"
  surface: "#f8f9fb"
  surface-hover: "#eef0f4"
  input-bg: "#ffffff"
  ink: "#181b22"
  ink-dim: "#5b6270"
  ink-faint: "#7d8492"
  hairline: "rgba(0,0,0,0.07)"
  hairline-strong: "rgba(0,0,0,0.15)"
  chip-bg: "rgba(0,0,0,0.04)"
  accent-teal: "#007a63"
  accent-teal-bg: "rgba(0,122,99,0.08)"
  accent-teal-border: "rgba(0,122,99,0.24)"
  danger: "#d13a4a"

typography:
  display:
    fontFamily: Archivo
    weights: [500, 600, 700, 800]
  body:
    fontFamily: Inter
    weights: [400, 500, 600, 700]
  mono:
    fontFamily: JetBrains Mono
    weights: [500, 600]
    use: climbing grades only (`GradePill`) — don't reuse elsewhere

rounded:
  sm: 8px
  md: 12px
  lg: 20px
  full: 9999px

spacing:
  xxs: 4px
  xs: 6px
  sm: 8px
  md: 14px
  lg: 16px
  xl: 24px
  xxl: 32px
  xxxl: 48px
---

## Overview

Acordanza's design system is a small, light-canvas component set — a cool light-gray
page (`{colors.canvas}` — `#eef1f5`) with near-white elevated cards
(`{colors.surface}` — `#f8f9fb`). There is no dark theme and no theme switch; every
component reads its foreground from `--text`/`--text-dim`/`--border`, which resolve
to dark-on-light values.

Display type is **Archivo**, a bold grotesk reserved for headings — used at weight
700 with tight negative letter-spacing (`-0.01em` to `-0.02em`), never for body
copy. Body and UI text is **Inter** at weight 400–600. **JetBrains Mono** is reserved
for climbing grades (`GradePill`) and small monospace labels — not a general-purpose
typeface.

The brand accent is `{colors.primary}` (`#3f3fe0`) — a saturated cobalt violet — used
as a filled pill on `Button`'s primary variant, the loudest action on any given view
(use once per view). Everything else defaults to `variant="ghost"`, a low-emphasis
outlined pill. `{colors.accent-teal}` (`#007a63`) is the system's only other accent,
reserved for `GradePill` and success/positive states.

**Key characteristics:**
- One canvas, one elevated-surface step — no dark mode, no color-blocked bands.
- Archivo 700 for headings (tight tracking); Inter 400/600 for body and UI; JetBrains Mono for grades only.
- `Button`'s primary variant is a filled violet pill — the loudest action, used once per view. `variant="ghost"` (outlined, transparent) is the default for everything else.
- All buttons and chips are pill-shaped (`{rounded.full}`); cards and modals use `{rounded.lg}` (20px); inputs and grade pills use `{rounded.sm}`/`{rounded.md}`.
- Elevation comes from a soft border (`{colors.hairline}`) plus, on floating surfaces like `Modal`, a subtle shadow — never a hard drop-shadow.

## Colors

### Brand & Accent
- **Cobalt Violet** (`{colors.primary}` — `#3f3fe0`): the brand accent. `Button`'s primary variant only.
- **Cobalt Deep** (`{colors.primary-deep}` — `#3634c4`): primary button hover/active state.
- **On-Primary** (`{colors.on-primary}` — `#ffffff`): label color on top of `{colors.primary}`.

### Surface
- **Canvas** (`{colors.canvas}` — `#eef1f5`): the page background. Set once on `body`.
- **Surface** (`{colors.surface}` — `#f8f9fb`): elevated card/modal background — one step up from canvas.
- **Surface Hover** (`{colors.surface-hover}` — `#eef0f4`): hover state for interactive card rows (e.g. route list items).
- **Input Background** (`{colors.input-bg}` — `#ffffff`): form inputs, one step brighter than `{colors.surface}` for contrast against card backgrounds.
- **Hairline** (`{colors.hairline}` — 7% black): default 1px dividers and card borders.
- **Hairline Strong** (`{colors.hairline-strong}` — 15% black): ghost-button borders, empty-state dashed borders — anything that needs to read as an outline rather than a divider.
- **Chip Background** (`{colors.chip-bg}` — 4% black): `Chip`'s fill.

### Text
- **Ink** (`{colors.ink}` — `#181b22`): primary text.
- **Ink Dim** (`{colors.ink-dim}` — `#5b6270`): secondary text — labels, metadata, ghost-button default color.
- **Ink Faint** (`{colors.ink-faint}` — `#7d8492`): tertiary text — eyebrows, the least emphasized caption tier.

### Semantic
- **Accent Teal** (`{colors.accent-teal}` — `#007a63`): `GradePill` foreground and success text.
- **Accent Teal Background/Border** (`{colors.accent-teal-bg}` / `{colors.accent-teal-border}`): `GradePill`'s fill and border, both derived from `{colors.accent-teal}` at low opacity.
- **Danger** (`{colors.danger}` — `#d13a4a`): destructive state — `Button`'s `danger` ghost variant, delete actions.
- **Danger Background/Border** (`{colors.danger-bg}` / `{colors.danger-border}`): `Chip`'s `rejected` status fill and border, both derived from `{colors.danger}` at low opacity.

## Typography

| Use | Family | Weight | Notes |
|---|---|---|---|
| Page/section headings | Archivo | 700 | 20–24px, letter-spacing -0.01em |
| Modal titles | Archivo | 700 | 20px, letter-spacing -0.01em |
| Uppercase section labels | Archivo | 600 | 11–13px, letter-spacing 0.06–0.08em, `{colors.ink-faint}` or `{colors.ink-dim}` |
| Body copy | Inter | 400 | 16px default, 0.24px tracking |
| Button labels | Inter | 600 | 14–15px |
| Grades (`GradePill`) | JetBrains Mono | 600 | 13px — the only place this face appears |

**Principles:**
- Archivo is a display face only — never body copy, never below ~13px.
- Body Inter keeps a small positive tracking (`0.24px`) on running text, matching the app's existing mechanical-precision feel.
- Don't introduce a second display face or a second accent color; the palette stays intentionally narrow for a 6-person tool.

## Layout

- **Spacing scale**: `{spacing.xxs}` 4px · `{spacing.xs}` 6px · `{spacing.sm}` 8px · `{spacing.md}` 14px · `{spacing.lg}` 16px · `{spacing.xl}` 24px · `{spacing.xxl}` 32px · `{spacing.xxxl}` 48px. This is the only spacing scale — never a raw px gap in new CSS.
- **Radius scale**: `{rounded.sm}` 8px (inputs, grade pills) · `{rounded.md}` 12px (cards, dropdowns) · `{rounded.lg}` 20px (modals, empty states) · `{rounded.full}` (buttons, chips).
- **Content width**: mostly fluid, not a fixed centered column. List/grid views (routes list) reflow into more columns as the viewport widens (`repeat(auto-fill, minmax(280px, 1fr))`) and the map page has no cap at all, since both benefit from extra width. The remaining "boxed" pages — settings, and the admin manage section (sectores/usuarios/tareas pendientes, under a shared left-nav layout) — are centered content that doesn't scale usefully past a point (a form, a table, a tree), so they share one common cap, `--page-max-width` (1100px), rather than each picking its own number. All boxed and fluid pages alike use `{spacing.xxl}` page padding.

## Elevation

No traditional card-shadow language for static surfaces — cards register depth via a
1px `{colors.hairline}` border against the slightly darker canvas. `Modal`, as a
floating surface over a dimmed backdrop, is the one exception and carries a soft
shadow (`0 1px 2px rgba(0,0,0,.03), 0 10px 28px rgba(0,0,0,.08)`) for legibility
against whatever's behind it.

## Components

Source of truth for implementation is `design-system/src/` (11 components: `Button`,
`Chip`, `EmptyState`, `GradePill`, `Modal`/`ModalActions`, `Pagination`, `List`/`ListRow`,
`Badge`, `RemoveButton`, `Combobox`) — read the component CSS there for exact rules.
This section documents intent, not a duplicate spec.

**`Button`** — `variant="primary"` (default) is the filled violet pill,
`{colors.on-primary}` label, `{rounded.full}`, 46px tall. Use once per view — it's
the loudest action. `variant="ghost"` is transparent with a `{colors.hairline-strong}`
border and `{colors.ink-dim}` label; `small` tightens padding/type for compact
placements; `danger` swaps the hover color to `{colors.danger}` for destructive ghost
actions. `as` makes it polymorphic (e.g. `as="a"` for a link styled as a button).

**`Chip`** — a `{colors.chip-bg}`-filled pill for tags/filters; `onRemove` adds an
inline `RemoveButton` that turns `{colors.danger}` on hover. `status` selects one of
three visual states: `pending` (default, neutral `{colors.chip-bg}` fill), `approved`
(`{colors.accent-teal}` fill/border), or `rejected` (`{colors.danger}` fill/border).

**`GradePill`** — a small monospace badge for climbing grades, the only place
`{colors.accent-teal}` and JetBrains Mono appear together.

**`EmptyState`** — a dashed `{colors.hairline-strong}` box, `{rounded.lg}`, for
zero-result states.

**`Modal`/`ModalActions`** — a centered `{rounded.lg}` card over a dimmed overlay
(50% black); `ModalActions` right-aligns its children (typically a ghost "Cancelar"
next to a primary submit).

**`Pagination`** — two ghost `Button`s ("Anterior"/"Siguiente") flanking a
`{colors.ink-dim}` status label.

**`List`/`ListRow`** — a vertical stack of bordered `{rounded.md}` rows, each a
title/subtitle pair on the left and optional trailing content (a `RemoveButton`, one
or more `Button`s) on the right. Pass `onClick` to make the whole row a target (e.g.
opening a detail view) instead of just its trailing controls.

**`Badge`** — a small filled `{colors.danger}` pill for a numeric count (caps the
label at "99+"); renders nothing for a count of 0. Used inline (nav tabs, side-nav
items) or absolutely positioned via its `className` prop (the topbar notification
bell).

**`RemoveButton`** — a `×` control with a required `label` (its only accessible
name). `variant="plain"` (default) is a borderless `{colors.ink-dim}` glyph that
turns `{colors.danger}` on hover, used standalone in `ListRow`s and cards.
`variant="overlay"` is a circular dark badge for sitting on top of media (e.g. a topo
thumbnail).

**`Combobox`** — a search input that opens a `{rounded.md}` dropdown of options below
it; `leadingOption` pins an extra entry above the list (e.g. a "clear selection"
row), `closedValue` controls what shows when not focused (typically the current
selection's label). Filtering and multi/single-select behavior stay with the caller —
`Combobox` only owns the open/query/dropdown mechanics.

## Do's and Don'ts

### Do
- Use `Button`'s primary (filled violet) variant once per view for the loudest action; default to `ghost` everywhere else.
- Keep the accent palette narrow — violet for the primary CTA, teal for grades/success, red for danger. No third accent.
- Style new UI with the token set (`--bg`, `--text`, `--space-*`, `--radius-*`), not hardcoded values — see `design-system/README.md`'s conventions section.
- Reserve Archivo for headings ≥13px; keep body copy in Inter.

### Don't
- Don't add a dark theme or a theme switch — this is a single-canvas system by design.
- Don't reuse JetBrains Mono outside `GradePill`/grade display.
- Don't add drop shadows to static cards — elevation comes from the hairline border; reserve shadow for floating surfaces like `Modal`.
- Don't reference component internal class names (`.primary-btn`, `.tag-chip`, etc.) — they're implementation details, not a public API.

## Iteration Guide

1. Change one component at a time in `design-system/src/components/<Name>/`; its `.css` file and this doc's Components section should stay in sync.
2. New tokens go in `design-system/src/tokens.css` under the existing color/typography/radius/spacing groups — don't hardcode a new value directly in a component file.
3. Match this doc's token names verbatim in code and prose — don't paraphrase.
