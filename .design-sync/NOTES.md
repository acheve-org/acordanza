# design-sync notes — acordanza-design-system

## Setup

- `design-system/dist/` is gitignored (repo convention) — always run `cfg.buildCmd`
  (`cd design-system && bun run build`) before re-running the converter. The
  package-build step will fail with `[NO_DIST]` if dist/ is stale or missing.
- No npm on this machine's PATH — converter deps (`esbuild`, `ts-morph`,
  `@types/react`, `playwright`) were installed into `.ds-sync/` with `bun add`
  instead of `npm i`. Works identically; just don't reach for `npm` here.
- Chromium wasn't cached locally on first sync — installed via
  `bunx playwright install chromium` (playwright@1.62.1, matching the
  cached build's pinned browser revision). ~270MB one-time download.

## Design facts that shaped the sync

- **The component set is a light "Cool Minimal" theme** (`--bg: #eef1f5`,
  `--text: #181b22`) — as of 2026-08-04 (`cc04a98`, "add light to the
  system design"). It was dark-canvas (`--bg: #000`) before that commit;
  a sync on 2026-08-04 ran against the dark tokens ~3h before `cc04a98`
  landed and left `conventions.md`/this file describing the old theme —
  the token *names* still validated (only values changed), so the
  automated check never caught it. Corrected on the 2026-08-04 17:xx
  re-sync. Every authored preview wraps its content in a
  `background: var(--bg)` container (converter's default preview-card
  background is plain white and there's no `cfg` override for it) — with
  the light theme `--bg` this is closer to a no-op than it was under the
  dark theme, but it's still correct and left in place.
- Fonts: `Inter` (body/UI) and `JetBrains Mono` (grades) were already
  runtime-loaded via the host's Google Fonts `<link>`; `Archivo` (display,
  used for `Modal`'s `h2`/`h3`) was added in the same `cc04a98` commit but
  missed being added to `runtimeFontPrefixes`, so it round-tripped as
  `[FONT_MISSING]` on this re-sync until added. All three are loaded by
  the host app at runtime via a Google Fonts `<link>` in
  `frontend/index.html` — the package ships no `@font-face`. Keep
  `runtimeFontPrefixes: ["Inter", "JetBrains Mono", "Archivo"]` in sync
  with that `<link>`'s `family=` list — a new family added there needs a
  matching entry here, or it'll `[FONT_MISSING]` next sync. Local
  render-check screenshots fall back to a system font since the
  offline-ish capture sandbox doesn't always have the webfont loaded in
  time; this is expected and not a bug — the real claude.ai/design host
  is expected to load the same Google Fonts URL, same contract as the
  production frontend.
- `Modal` is an overlay (fixed inset:0 backdrop) — set
  `cfg.overrides.Modal: {"cardMode": "single", "viewport": "600x520"}` so
  the open state renders inside the card instead of escaping/collapsing.
  Only the primary export (`AddTick`) renders in the card; `Wide` is a
  second authored variant that only shows via `?story=Wide`.
- `ModalActions` auto-grouped into a `"modal"` group (name-based heuristic)
  while every other component landed in `"general"` — cosmetic only, not
  worth a `docsMap` override.

## Known render warns

None — render check is fully clean (0 bad, 0 thin, 0 variantsIdentical) as
of the last sync.

## Re-sync risks

- `design-system/dist/` not being committed means a fresh clone MUST run
  `bun install && cd design-system && bun run build` before any re-sync,
  or the converter sees a stale/missing entry.
- The background-wrapper style is duplicated inline across 6 of the 7
  preview `.tsx` files (`background: var(--bg)`, etc.) rather than a shared
  helper — if the token names (`--bg`, `--text`) ever get renamed upstream,
  all six need updating together. Grep `.design-sync/previews/*.tsx` for
  `var(--bg)` to find them.
- **Confirmed risk, not just theoretical**: `conventions.md` names token
  values inline in its table — on 2026-08-04 the DS flipped its whole
  theme (dark → light, `cc04a98`) shortly after a sync had already run,
  and `conventions.md` kept describing the old dark theme (`--bg: #000`,
  etc.) for ~3 hours of wall-clock/several commits until the next re-sync
  caught it by inspection, not by an automated check — token *names*
  still validated throughout, only values were wrong. Skim
  `conventions.md` against `design-system/src/tokens.css` / `DESIGN.md`
  on any re-sync that touches `tokens.css`, don't rely on validate alone.
- This is a small (7-component) package with no Storybook and no
  `docs/`/`documentation/` directory — `docs: 0/7 components matched` in
  every build log is expected, not a regression; every `.prompt.md` is
  synthesized from `.d.ts` + the authored preview, which is working fine.
