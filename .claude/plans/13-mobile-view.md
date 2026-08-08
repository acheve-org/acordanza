# Mobile view: hamburger navigation, and the small-screen fixes around it

Status: planned (not started)
Issue: [#115](https://github.com/acheve-org/acordanza/issues/115)
Depends on: nothing. Frontend-only — no backend, no DTO, no migration, no OpenAPI regeneration. The test harness from [09](09-frontend-component-tests.md) is in `main`, so the navigation parts get real tests; the CSS parts do not (see Testing).

## Problem

The app has exactly one breakpoint today — a single `@media (max-width: 720px)` block at the very bottom of `frontend/src/styles.css` — and it only reflows things. Nothing collapses. On a phone the top bar becomes:

```
[▨ Acordanza]                                   [Gestionar] [🔔] [👤]
[Busca] [Explora] [Sigue] [Mi acordanza]        ← second row, horizontally scrollable
```

because the mobile block sets `.main-nav { order: 3; flex-basis: 100% }`. Two rows of chrome before any content, and the four section links sit in an `overflow-x: auto` strip with no scroll affordance. The same happens one level down: `.side-nav` (shared by `/manage` and `/my-acordanza`) becomes `flex-direction: row; flex-wrap: wrap`, so up to six section links wrap across two or three lines above the page content.

Issue #115 asks for three specific things:

1. `.brand-name` hidden on mobile (the mark stays).
2. The links inside `.main-nav` collapse into a hamburger menu.
3. The `/manage` and `/my-acordanza` side-navs do the same, with the hamburger sitting **to the right of the section name** ("Gestionar" / "Mi acordanza" stay visible; the items collapse).

…and then: "explore more improvements in the mobile view without visual changes in the desktop view". Reading the two stylesheets against a 320–390px viewport turns up nine more, listed in §6. The hard constraint on all of it is the second half of that sentence: **desktop must not move by a pixel.**

Two existing details constrain the implementation:

- `styles.css` ends with a load-bearing comment: *"Keep this block last: at equal specificity, a same-selector rule declared later in the cascade wins regardless of the media query, so every base rule it overrides must appear above this point in the file."* Every new mobile rule goes in that block, at the bottom, or it silently loses.
- `design-system/` currently has **no media queries at all**. §5 adds the first one.

## Design

### 1. CSS-only collapsing — no JS breakpoint, no duplicated links

The obvious implementation (a `useMediaQuery`/`matchMedia` hook that renders either a nav or a hamburger) is rejected:

- it duplicates the breakpoint in JS and CSS, so the two drift;
- `matchMedia` doesn't exist in jsdom, so every page test that renders the shell needs a polyfill in `src/test/setup.ts`;
- it makes the desktop render depend on JS state, which is exactly what "no visual changes in desktop" wants to avoid.

The second obvious implementation — render the links twice, once inline for desktop and once inside a popup for mobile, hiding one with CSS — is also rejected: two copies of every link to keep in sync, and `getByRole('link', { name: 'Sectores' })` starts matching two elements in the existing `ManageLayout.test.tsx`.

Instead: **one set of links, wrapped in a container that is `display: contents` on desktop and a real positioned box on mobile.**

```css
/* base — desktop unchanged: the links stay direct flex children of .main-nav,
   so .main-nav's own `gap` still applies to them exactly as today */
.main-nav-items { display: contents; }
.nav-toggle { display: none; }

/* inside the trailing @media (max-width: 720px) block */
.main-nav { position: relative; }
.nav-toggle { display: flex; /* … */ }
.main-nav-items { display: none; position: absolute; /* popup chrome */ }
.main-nav-items.is-open { display: flex; flex-direction: column; }
```

`display: contents` makes the wrapper vanish from the box tree, so on desktop the DOM change is invisible to layout — the links remain flex items of `.main-nav` and inherit its `gap: var(--space-lg)`. The trigger button is `display: none` outside the breakpoint, which also removes it from the accessibility tree, so its `aria-expanded` never lies on desktop. The same three-rule shape does both menus.

This is the one technique the whole change rests on; write the comment in `styles.css` explaining it, because a future reader will otherwise "simplify" `display: contents` to `display: flex` and break desktop spacing.

### 2. `hooks/useDisclosure.ts` — the behaviour, shared

Three menus (top nav, side nav, and the existing user menu) need identical behaviour, and `AppLayout` already hand-rolls two thirds of it inline. Extract it once:

```ts
export function useDisclosure() {
  // returns { open, toggle, close, containerRef, triggerRef }
}
```

- `open`/`toggle`/`close` — the state.
- outside `mousedown` on `containerRef` closes (this is the effect already living in `App.tsx`, moved).
- **Escape closes and returns focus to `triggerRef`** — the user menu does not do this today; it's a free fix that comes with the extraction.
- **closes on `useLocation().pathname` change** — tapping a link inside the popup navigates *and* dismisses. Doing it from the pathname rather than from an `onClick` on every link means it also covers the `<Navigate>` redirects (`ManageIndexRedirect`, `MyAcordanzaIndexRedirect`) and the browser back button.
- both effects are gated on `open`, matching the existing code — no listeners while closed.

New `frontend/src/hooks/` folder (there is none today; `components/` is documented as "shared UI pieces", and this is not one). One file, one hook.

**Not a design-system component.** `design-system/` holds product-agnostic widgets with no app-shell or breakpoint knowledge, and it has no media queries by policy so far; a `Menu` component there would need both. The generic part here is ~30 lines of event handling, and the two menus' chrome is genuinely different (a floating top-bar popup vs. a disclosure hanging off a section heading). If a third *visually identical* dropdown ever appears, promoting a `Menu` to the design system is the right move then, not now.

### 3. Top bar: `components/MainNav.tsx`

Lift the `<nav className="main-nav">` block out of `AppLayout` into its own component — same reason `ManageLayout` is testable and `App.tsx` isn't: a component can be rendered in isolation with `renderWithProviders`.

```tsx
<nav className="main-nav" ref={containerRef}>
  <button
    type="button"
    className="nav-toggle"
    aria-label="Menú"
    aria-haspopup="true"
    aria-expanded={open}
    aria-controls="main-nav-items"
    ref={triggerRef}
    onClick={toggle}
  >
    {/* three-bar hamburger, 20×20 inline SVG, same shape as the bell/user icons */}
  </button>
  <div id="main-nav-items" className={`main-nav-items nav-popup${open ? ' is-open' : ''}`}>
    {/* the four existing NavLinks, unchanged */}
  </div>
</nav>
```

The four `NavLink`s and their `main-nav-item` class are copied across verbatim.

**The hamburger sits immediately right of the brand**, not at the far left edge, because that keeps DOM order equal to visual order (no `order:` shuffling, so tab order stays sane) and the header reads `[▨] [☰] ……… [🔔] [👤]` once the wordmark is hidden. With `.main-nav`'s mobile `order: 3; flex-basis: 100%` deleted, the top bar is back to **one row** on mobile — which is most of the win.

**"Gestionar" stays where it is** — in `.topbar-right`, not folded into the hamburger. It lives outside `.main-nav` (issue #115's wording is specifically about the menu *inside* `.main-nav`), it's only rendered for admins/moderators, and it fits: at 320px the mobile row is `padding 16 + mark 22 + 14 + toggle 44 + auto + "Gestionar" ~66 + 16 + bell 44 + 16 + avatar 44 + padding 16` ≈ 298px. Folding it in would mean either duplicating the link (§1) or moving it into `.main-nav` and re-creating its right-alignment with `margin-left: auto`, which is exactly the kind of desktop-pixel risk this issue forbids. See "Decisions to confirm".

`AppLayout`'s user menu is refactored onto `useDisclosure` at the same time — markup and classes untouched, only the inline `useState`/`useEffect` pair replaced. Pure refactor plus Escape-to-close.

### 4. Side nav: `components/SideNav.tsx`

`ManageLayout` and `MyAcordanzaLayout` each hand-write the same `<aside className="side-nav"><h2>…</h2>{items}</aside>`. Extract the shell so the hamburger is implemented once:

```tsx
<SideNav title="Gestionar">
  {/* the same NavLinks each layout already renders, unchanged */}
</SideNav>
```

```tsx
<aside className="side-nav" ref={containerRef}>
  <div className="side-nav-header">
    <h2>{title}</h2>
    <button className="nav-toggle" aria-label={`Secciones de ${title}`} aria-expanded={open}
            aria-controls={itemsId} ref={triggerRef} onClick={toggle} /* hamburger svg */ />
  </div>
  <div id={itemsId} className={`side-nav-items nav-popup${open ? ' is-open' : ''}`}>{children}</div>
</aside>
```

Both wrappers get `display: contents` at base, so on desktop the `h2` and the links stay direct flex children of `.side-nav` and every existing rule (`gap: var(--space-xs)`, the `h2`'s `margin: 0 0 var(--space-xs)`, `position: sticky`) applies exactly as today. `itemsId` comes from React's `useId()` — two `SideNav`s never coexist, but hardcoding a DOM id in a shared component is the kind of thing that bites later.

On mobile: `.side-nav-header` becomes `display: flex; align-items: center; justify-content: space-between`, `.side-nav` gets `position: relative`, and `.side-nav-items` becomes the popup anchored under the header. The mobile block's current `.side-nav { flex-direction: row; flex-wrap: wrap }` is deleted — that rule exists only to make the un-collapsed nav bearable, and it's what this replaces.

Note what stays visible when collapsed: the section name ("Gestionar"/"Mi acordanza") plus the page's own `<h1>` in `.side-nav-content` ("Usuarios", "Mis sectores", …). So the user still knows both where they are and which sub-page they're on — no need to duplicate the active item's label into the header.

### 5. Shared popup chrome

Both popups get a `nav-popup` class, styled **once, entirely inside the mobile block** (it has no effect on desktop because the `display: none` that hides it also lives there):

```css
.nav-popup {
  position: absolute;
  top: calc(100% + var(--space-xs));
  left: 0;
  min-width: 200px;
  z-index: 20;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  padding: var(--space-xs) 0;
}
```

Deliberately the same recipe as `.user-menu-popup` (tokens, radius, border, `--space-xs` vertical padding) so the three menus read as one family; not literally reusing `.user-menu-popup`, because that rule is unconditional and would then need mobile-only overrides in the other direction. `z-index: 20` sits above page content and below `.modal-overlay` (100) and `.loading-bar` (1200).

`.nav-toggle` gets the `.user-menu-trigger` treatment (36×36 circle, `--bg-card`, hairline border) bumped to **44×44** on mobile — see §6.7.

### 6. The other mobile fixes

All in the trailing `@media (max-width: 720px)` block of `styles.css` unless noted. Each is independent; each is a one- or two-line rule.

1. **`.area-page` never got mobile padding.** The block covers `.layout`, `.settings-page`, `.side-nav-layout`, `.explore-page`, `.home-page`, `.fa-page` — but not `.area-page`, which keeps `padding: var(--space-xxl)` (32px each side) on a phone. Add `.area-page { padding: var(--space-xl); }`. Straight bug, same class as the others.
2. **Horizontal overflow below ~328px.** `.routes-list` is `repeat(auto-fill, minmax(280px, 1fr))` inside `.layout`'s 24px×2 mobile padding: at a 320px viewport the track is 280px in a 272px box, so the whole document scrolls sideways. Fix with `grid-template-columns: repeat(auto-fill, minmax(min(280px, 100%), 1fr))` — a base-rule change (not mobile-only), safe because `min()` resolves to 280px at every width where 280px fits, i.e. desktop is byte-identical in effect. Apply the same guard to `.fa-grid` (240px) and `.topo-grid` (96px) for the same reason, even though they only bite at narrower widths.
3. **`.tab-bar` overflows.** `PendingTasksPage` has four tabs, `MyContributionsPage` three, laid out `display: flex; gap: var(--space-lg)` with no wrap and no scroll — "Vías / Sectores / Aperturistas" already exceeds 320px. Add `overflow-x: auto; scrollbar-width: none;` plus `::-webkit-scrollbar { display: none }` on mobile. Scroll, not wrap: wrapping breaks the single bottom-border rule the active-tab underline sits on.
4. **`.detail-head` squeezes the route title.** Title and `.detail-actions` (up to four buttons, `flex-shrink: 0`) share a `justify-content: space-between` row. On mobile: `.detail-head { flex-direction: column; align-items: stretch; }` and `.detail-actions { flex-wrap: wrap; }`.
5. **Modal chrome eats the screen.** `.modal-overlay` has `padding: var(--space-xl)` and `.modal` `padding: var(--space-xxl)`, so on a 360px screen the content column is 360 − 48 − 64 = **248px**. In `design-system/src/components/Modal/Modal.css`, add the package's first media query: overlay padding → `--space-md`, modal padding → `--space-xl`, giving 360 − 28 − 48 = 284px. Also `align-items: flex-start` on the overlay so a tall modal doesn't get vertically centred against the on-screen keyboard.
6. **Map heights.** `.area-map` is `height: 65vh; min-height: 420px` — on a 568px-tall iPhone SE the `min-height` wins and the map alone is taller than the viewport minus the top bar, pushing `/explore`'s zone list entirely below the fold. On mobile: `height: 50vh; min-height: 240px`. Likewise `.modal-wide .area-map` (`60vh`/`360px`) inside a modal capped at `max-height: 88vh` → `50vh`/`220px`.
7. **Tap targets.** `.notif-bell` and `.user-menu-trigger` are 36×36; the iOS/Android guidance is 44/48. On mobile only: `width: 44px; height: 44px` for both plus `.nav-toggle`. Icons stay 20px. Desktop keeps 36.
8. **`.users-sort-bar`** is a label plus three `white-space: nowrap` sort buttons in a `gap: var(--space-md)` row — it overflows below ~360px. Add `flex-wrap: wrap` on mobile. (`.users-toolbar` and `.users-page-header` are already handled.)
9. **`.area-tree-children { padding-left: var(--space-xl) }`** — 24px per nesting level, so a three-deep sector tree loses 72px of an already narrow row. Reduce to `var(--space-md)` on mobile.
10. **Viewport plumbing**, in the base rules (not the mobile block): `html { -webkit-text-size-adjust: 100%; }` (stops iOS inflating body text in landscape) and `body`/`.login-screen` `min-height: 100dvh` instead of `100vh` (the mobile URL bar makes `100vh` taller than the visible viewport, which is why the login card sits slightly off-centre on a phone). `100dvh` is supported everywhere the app already targets; no fallback needed for a private six-person tool, but `min-height: 100vh` can stay on the line above as one if wanted.

`input, select, textarea { font-size: 16px }` is already correct and must stay — anything smaller makes iOS Safari zoom on focus.

### 7. Optional, and last: collapse the filters on "Busca"

Not asked for, listed separately because it's the largest remaining mobile annoyance and the only item here that changes a page's information architecture rather than its spacing: on `/routes`, `.filters` (search, area, grade, orientation, gear, tag, topo — seven controls at 48px each) stacks above `.routes-list`, so a phone user scrolls ~400px of form before the first route. The fix is the same primitive as everything above: a "Filtros" ghost `Button` visible only on mobile, toggling `.filters` with `useDisclosure`, defaulting closed, with the active-filter count in the label. Implement it only if the first six steps land cleanly — it deserves its own review pass and is easy to drop.

### Non-goals

- **No second breakpoint.** One `720px` value, as today. Tablet-specific tuning is a different issue.
- **No dark mode, no theme work** — unrelated, and `styles.css` is explicitly a light theme.
- **No design-system `Menu` component** (§2), and no promotion of `MainNav`/`SideNav` into the design system: both are app-shell, not product-agnostic.
- **No touch gestures** — no swipe-to-open drawer, no off-canvas slide-in. A popup is enough for a six-person tool and needs no scroll-locking or focus-trap machinery.
- **No change to any route, permission, label, or API call.** Every `NavLink` moves file, not target.
- **`AreaMap`/Leaflet interaction on touch** is left alone beyond the height change; per `frontend/CLAUDE.md` it's out of scope for the jsdom test layer and gets manual checking only.

## Steps

1. **`frontend/src/hooks/useDisclosure.ts`** — new folder, the hook from §2 (outside-click, Escape + focus return, close on `useLocation().pathname` change).
2. **`frontend/src/components/MainNav.tsx`** — new; the `<nav className="main-nav">` block moved out of `AppLayout` verbatim plus the trigger and `.main-nav-items` wrapper (§3). `App.tsx` renders `<MainNav />` in its place and switches its user menu onto `useDisclosure`, deleting the local `useState`/`useEffect`/`userMenuRef` trio.
3. **`frontend/src/components/SideNav.tsx`** — new; the `<aside>` shell from §4. `ManageLayout.tsx` and `MyAcordanzaLayout.tsx` each swap their `<aside className="side-nav">…</aside>` for `<SideNav title="…">…</SideNav>`, keeping their `NavLink`s and permission gating exactly as-is.
4. **`frontend/src/styles.css`, base rules** — `.brand-name` stays as-is here; add `.main-nav-items`/`.side-nav-items`/`.side-nav-header { display: contents }`, `.nav-toggle { display: none }`, the `min()` grid guards (§6.2), and the `html`/`body`/`.login-screen` rules (§6.10). Comment the `display: contents` trick per §1.
5. **`frontend/src/styles.css`, the trailing `@media (max-width: 720px)` block** — everything else. Delete `.main-nav { order: 3; flex-basis: 100% }` and `.side-nav { flex-direction: row; flex-wrap: wrap }`; add `.brand-name { display: none }`, the `.nav-toggle`/`.nav-popup`/`.main-nav`/`.side-nav`/`.side-nav-header` mobile rules from §1/§3/§4/§5, and §6 items 1, 3, 4, 6, 7, 8, 9. Keep the block last and keep its existing comment.
6. **`design-system/src/components/Modal/Modal.css`** — the mobile padding query (§6.5). Repeat the `720px` literal with a comment noting it must match `frontend/src/styles.css`: CSS custom properties can't be used in media conditions, and adding a PostCSS `@custom-media` plugin to two build pipelines to share one number isn't worth it.
7. **Tests** — see below.
8. **Docs** — `frontend/CLAUDE.md`: in Styling, the single 720px breakpoint, the trailing-block rule (already implicit in the CSS comment, now worth stating), and the `display: contents` collapse technique; in Architecture, one line each for `components/MainNav.tsx`, `components/SideNav.tsx` and `hooks/useDisclosure.ts` (and that `ManageLayout`/`MyAcordanzaLayout` now share the shell). `DESIGN.md`: a short "Responsive" paragraph under Layout — one breakpoint, what collapses at it, and the `Modal` padding step-down. Root `CLAUDE.md` needs no change.
9. **Verify in a real browser** — see Verification.

## Testing

jsdom applies no CSS and never matches a media query, so **nothing here can assert "hidden on mobile" / "visible on desktop"**. Write assertions against state and structure, never `toBeVisible()`:

- **`components/MainNav.test.tsx`** (new): all four links render with their correct `to`; the trigger exposes `aria-expanded="false"` and an `aria-controls` matching the items container's `id`; clicking it flips `aria-expanded` to `"true"` and adds `is-open` to that container; Escape flips it back and focus returns to the trigger; navigating (render at `/routes`, click "Explora") leaves it closed.
- **`pages/ManageLayout.test.tsx`** (existing, extended): the six current cases must keep passing untouched — that's the regression guard proving the `SideNav` extraction changed nothing about permission gating, and it works precisely because jsdom ignores the CSS that would hide the collapsed items. Add the toggle/Escape/close-on-navigate trio against the section-name trigger (`aria-label="Secciones de Gestionar"`).
- **`hooks/useDisclosure`** gets no separate test file — it has no behaviour the two component tests above don't exercise, and testing it in isolation would mean mounting a fake component to drive it.
- **`components/Filters.test.tsx`** (existing) must keep passing if §7 is implemented; the toggle wrapper doesn't remove anything from the DOM in jsdom.

The CSS half of this change — which is most of it — has no automated coverage and can't have any in this stack. That's the honest trade-off and the reason the verification step below is not optional.

## Verification

With the `run-acordanza` skill, driving the browser at three viewports:

- **375×812 and 320×568 (mobile):** the top bar is one row, wordmark hidden, mark + hamburger left, bell + avatar right; the hamburger opens a popup with the four sections, closes on outside tap, on Escape, and on picking a link. `/manage` and `/my-acordanza` show `Gestionar ☰` / `Mi acordanza ☰` with the items collapsed and the page `h1` right below. Then walk `/`, `/routes`, `/explore`, `/areas/*`, `/first-ascensionists`, `/manage/users`, `/manage/pending-tasks`, `/my-acordanza/contributions`, `/settings`, `/routes/new`, and one open `DetailModal` — checking on each that `document.documentElement.scrollWidth === document.documentElement.clientWidth` (no horizontal scroll; this is the single most useful check and the one §6.2 exists for) and that no control is clipped.
- **1440×900 (desktop):** screenshot the same set **before and after** the change and diff them. Expect zero difference. If anything moves, `display: contents` was dropped somewhere or a new rule landed outside the media block.

## Decisions to confirm before starting

- **"Gestionar" stays in `.topbar-right` on mobile rather than folding into the hamburger** (§3). It's outside `.main-nav`, it fits at 320px, and folding it in costs either a duplicated link or a desktop-layout risk. If it should join the menu, say so — it's the one item here that changes the answer to "what's in the hamburger".
- **The hamburger sits right of the brand mark, not at the far left edge** (§3) — keeps DOM order equal to visual order. Moving it to the edge is a one-line `order: -1`.
- **The side-nav collapses to `<section name> ☰` with no active-item label** (§4) — the page's own `<h1>` already says which sub-page is open.
- **One breakpoint (720px), duplicated as a literal in `design-system/`** (§6.5) rather than introducing a PostCSS plugin to share it.
- **§7 (collapsing the "Busca" filters) is optional** and explicitly separable from the rest.
- **`100dvh`** (§6.10) — no `100vh` fallback chain unless wanted.
