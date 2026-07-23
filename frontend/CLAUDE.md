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

`react-router` (`BrowserRouter` in `main.tsx`) provides two routes. `App.tsx` gates on auth state (`LoginScreen` while logged out, nothing while `loading`), then renders `AppLayout` (header/user menu) with nested routes: `/` → `pages/MainView.tsx` (the routes-list screen, previously `App.tsx`'s body verbatim), `/settings` → `pages/SettingsPage.tsx`. No global state library; state is local `useState` per page, plus one context for auth. `pages/` holds route-level screens; `components/` holds shared pieces reused across them.

- `context/AuthContext.tsx` — the only cross-cutting state. Holds `username`/JWT, persists `{ token, username }` to `localStorage` (`acordanza.auth`), and pushes the token into the API client via `setAuthToken`. On mount it validates a stored token against `GET /auth/me` and drops it silently on failure. Login is two-step when the account has a registered passkey: `login(email, password)` returns a `PasskeyChallenge` instead of completing when the backend responds `mfaRequired`; `completeLoginWithPasskey(challenge)` runs the WebAuthn assertion via `@github/webauthn-json`'s `get()` and finishes the session. `LoginScreen` drives this — on `mfaRequired` it shows a "usa tu llave de acceso" step and auto-triggers the assertion.
- `api/client.ts` — thin `fetch` wrapper (`api.get/post/put/delete`) that attaches `Authorization: Bearer` when a token is set and throws `ApiError` (with parsed `{ error }` body) on non-OK responses. `api/routes.ts`/`api/account.ts`/`api/passkeys.ts` build on it per resource; `api/types.ts` mirrors the backend DTOs (`RouteSummary`, `RouteDetail`, `Tick`, `LoginResponse`, `PasskeySummary`, filter/input shapes).
- `components/` — one file per shared UI piece: `Filters` (search/crag/type/tag, debounced 250ms in `MainView`'s effect), `RoutesList`/`RouteCard` (list rendering), `RouteFormModal` (create/edit, shared for both via the `editing` prop), `DetailModal` (route detail + tick log/add/delete), `LoginScreen`.
- `pages/SettingsPage.tsx` — password change (`api/account.ts`) and passkey management (`api/passkeys.ts`): list/register/delete. Registration calls `@github/webauthn-json`'s `create()` with the options the backend returns, wrapped as `{ publicKey: options }` — the backend's Fido2NetLib option objects already serialize to the same WebAuthn-L3 JSON shape the library expects, no manual base64url handling needed.
- Data is never cached beyond the current render — every mutation (`handleSaveRoute`, `handleDeleteRoute`, `handleAddTick`, `handleDeleteTick`) is followed by an explicit `refreshRoutes()`/`refreshDetail()` re-fetch rather than local state patching.

## Styling

Plain CSS in `src/styles.css` (no CSS-in-JS, no Tailwind), driven by CSS custom properties defined on `:root` (`--bg`, `--accent`, `--font-display`, etc.) — dark theme by default. Extend the theme by adding/reusing these variables rather than hardcoding colors in component-scoped styles (styling is global, not per-component).

## Linting & types

`oxlint` (`.oxlintrc.json`) enforces `react/rules-of-hooks` as an error; TypeScript is strict (`noUnusedLocals`, `noUnusedParameters`, `verbatimModuleSyntax` — type-only imports must use `import type`).
