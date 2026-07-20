# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Applies to `frontend/` — the React + TypeScript client, built with Vite, package-managed with Bun. See the root `CLAUDE.md` for how this fits with the backend.

## Commands

All run from `frontend/`, using `bun`, not `npm`/`yarn`:

```
bun install
bun dev              # dev server on http://localhost:5173
bun run build        # tsc -b (typecheck) then vite build -> dist/
bun run lint         # oxlint
bun run preview      # preview a production build
```

Needs `frontend/.env` (copy from `.env.example`) with `VITE_API_URL`, pointing at the backend (`http://localhost:5073/api` by default). No test suite exists yet.

## Architecture

Single-page app, no router — `App.tsx` is the only screen and toggles between `LoginScreen` and the main layout based on auth state. No global state library; state is local `useState` in `App.tsx` passed down as props, plus one context for auth.

- `context/AuthContext.tsx` — the only cross-cutting state. Holds `username`/JWT, persists `{ token, username }` to `localStorage` (`acordanza.auth`), and pushes the token into the API client via `setAuthToken`. On mount it validates a stored token against `GET /auth/me` and drops it silently on failure. `App` renders nothing while `loading`, then `LoginScreen` or the app shell based on `username`.
- `api/client.ts` — thin `fetch` wrapper (`api.get/post/put/delete`) that attaches `Authorization: Bearer` when a token is set and throws `ApiError` (with parsed `{ error }` body) on non-OK responses. `api/routes.ts` builds on it for route/tick endpoints; `api/types.ts` mirrors the backend DTOs (`RouteSummary`, `RouteDetail`, `Tick`, filter/input shapes).
- `components/` — one file per UI piece, all controlled by `App.tsx`: `Filters` (search/crag/type/tag, debounced 250ms in `App`'s effect), `RoutesList`/`RouteCard` (list rendering), `RouteFormModal` (create/edit, shared for both via the `editing` prop), `DetailModal` (route detail + tick log/add/delete), `LoginScreen`.
- Data is never cached beyond the current render — every mutation (`handleSaveRoute`, `handleDeleteRoute`, `handleAddTick`, `handleDeleteTick`) is followed by an explicit `refreshRoutes()`/`refreshDetail()` re-fetch rather than local state patching.

## Styling

Plain CSS in `src/styles.css` (no CSS-in-JS, no Tailwind), driven by CSS custom properties defined on `:root` (`--bg`, `--accent`, `--font-display`, etc.) — dark theme by default. Extend the theme by adding/reusing these variables rather than hardcoding colors in component-scoped styles (styling is global, not per-component).

## Linting & types

`oxlint` (`.oxlintrc.json`) enforces `react/rules-of-hooks` as an error; TypeScript is strict (`noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax` — type-only imports must use `import type`).
