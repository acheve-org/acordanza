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

- **The component set is dark-canvas only** — no light theme exists. Ghost
  buttons, `Chip`, `EmptyState`, and `Pagination`'s status text all read
  `--text-dim`/`--border`, which resolve to near-white. Every authored
  preview wraps its content in a `background: var(--bg)` container to stay
  legible — the converter's default preview-card background is plain white
  and there's no `cfg` override for it, so this had to be done in the
  preview `.tsx` files themselves, not config. Documented in
  `conventions.md` so the design agent does the same.
- Fonts (Inter, JetBrains Mono) are loaded by the host app at runtime via a
  Google Fonts `<link>` in `frontend/index.html` — the package ships no
  `@font-face`. Set `runtimeFontPrefixes: ["Inter", "JetBrains Mono"]` to
  suppress `[FONT_MISSING]`. Local render-check screenshots fall back to a
  system font since the offline-ish capture sandbox doesn't always have the
  webfont loaded in time; this is expected and not a bug — the real
  claude.ai/design host is expected to load the same Google Fonts URL,
  same contract as the production frontend.
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
- The dark-canvas wrapper style is duplicated inline across 6 of the 7
  preview `.tsx` files (`background: var(--bg)`, etc.) rather than a shared
  helper — if the token names (`--bg`, `--text`) ever get renamed upstream,
  all six need updating together. Grep `.design-sync/previews/*.tsx` for
  `var(--bg)` to find them.
- `conventions.md` names token values (e.g. `--bg` = `#000`,
  `--accent-2` = `#00a87e`) inline in its table — if the DS's actual token
  *values* change (not just names), the header will still validate (names
  still exist) but the stated hex values will be stale. Worth a skim on
  major token-file changes.
- This is a small (7-component) package with no Storybook and no
  `docs/`/`documentation/` directory — `docs: 0/7 components matched` in
  every build log is expected, not a regression; every `.prompt.md` is
  synthesized from `.d.ts` + the authored preview, which is working fine.
