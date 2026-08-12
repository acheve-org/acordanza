# Frontend component & page tests (Testing Library + MSW)

Status: done
Depends on: [07](07-test-harness.md) (Vitest setup). Independent of [08](08-backend-integration-tests.md) — can land in either order.

## Problem

Every page in `frontend/src/pages/` is verified today only by looking at it. That leaves the behaviours that aren't visible in a still frame untested: the 250ms filter debounce, whether a mutation actually invalidates the right query key and refetches, whether an action is hidden for a user lacking the permission, whether a modal defers its fetch until it opens, and whether the passkey second-step login path works at all.

Several of these are documented disciplines in `frontend/CLAUDE.md` — "always re-fetch after a mutation, never patch local state"; "modal/dropdown data is fetched by the thing that needs it, when it opens" — which currently rely entirely on review to hold.

## Design

### Rendering harness

`src/test/renderWithProviders.tsx` wraps a subject in the three things pages assume: a fresh `QueryClient` (`retry: false`, so an error test fails fast instead of retrying for seconds), a `MemoryRouter` with configurable `initialEntries`, and auth state.

**The one real decision is how to supply auth state.** `AuthProvider` has no injection seam — it always reads `localStorage` and calls `GET /auth/me`. Two options, and the plan uses both deliberately:

- **Default: provide the context value directly.** Export the existing `AuthContext` object from `context/AuthContext.tsx` (a one-word change, `const` → `export const`) and let `renderWithProviders` accept `{ permissions: [...], isAreaExpert: true, ... }` and supply a value. This makes permission-matrix tests trivial — vary one array, assert what renders — with no network mocking at all.
- **For session tests specifically: use the real `AuthProvider`** with `localStorage` seeded and `/auth/me` mocked. That's the only way to test the bootstrap-and-drop-invalid-token path, the two-step passkey login, and that `logout()` clears the query cache.

Exporting the context is a smaller and more honest change than restructuring the provider to take injected state, and it doesn't alter any production behaviour.

### API mocking: MSW, typed from the generated contract

`msw` 2.15.0, `setupServer` in `src/test/setup.ts`, with **`onUnhandledRequest: 'error'`**. That setting is doing real work here, not just hygiene: it's what mechanically enforces the "modals fetch their own data, pages don't prefetch for them" rule — an accidental page-level prefetch shows up as a failing test rather than as a review comment.

The handlers' response bodies get typed from `api/generated.ts`:

```ts
import type { components } from '../api/generated'
type RouteDto = components['schemas']['RouteDto']

const makeRoute = (o: Partial<RouteDto> = {}): RouteDto => ({ /* ... */, ...o })
```

This is the part worth being deliberate about. Test files live under `src`, so the existing `tsc -b` in `bun run build` typechecks them — which means a backend DTO change that regenerates `generated.ts` breaks the *mocks* at build time, not just the app. Combined with CI's existing `openapi-contract-check` job (which fails if `generated.ts` is stale), the loop closes: mocks cannot silently drift from the real API contract. Without this, hand-written fixtures rot into fiction and the suite starts passing against an API that no longer exists.

Factories (`makeRoute`, `makeArea`, `makeMeResponse`, …) live in `src/test/factories.ts`, built on those generated types.

### Test slices, in priority order

1. **`SearchPage`** — the converted react-query reference page, so it sets the pattern for the rest. Cover: typing in the filter issues one request after the debounce, not one per keystroke (fake timers); each filter reaches the request as the right query param; deleting a route invalidates and refetches the list; a failed load renders the Spanish error text. This is also the test that pins `queryKeys.routes(filters)` staying prefix-matchable by the bare `['routes']` used to invalidate it.
2. **Auth: `AuthContext` + `LoginScreen`.** Password-only login; the `mfaRequired` two-step passkey path (mock `@github/webauthn-json`'s `get` — the browser API is unavailable in jsdom and this flow is otherwise untestable without a real authenticator); a stored token rejected by `/auth/me` is dropped silently; `logout()` clears the query cache. This is the highest-risk untested area in the frontend — it has a genuine state machine and the manual cost of exercising it is high, which is exactly why it's under-tested today.
3. **Permission gating.** The frontend mirror of plan 08's authorization matrix: for a table of permission sets, assert which actions `RouteDetailModal`, `Filters`, and the nav render. Cheap and data-driven once `renderWithProviders` takes a permission list. Worth stating plainly: this tests only what the UI *shows* — the backend matrix in plan 08 is what tests whether it's *enforced*. Neither substitutes for the other.
4. **Deferred fetching.** `AreaPicker` / `FirstAscensionistPicker` issue no request on mount without a selection and fetch on `Combobox`'s `onOpen`; `AreaTree` loads roots first and children only on expand. Assert the *absence* of the request before opening — with `onUnhandledRequest: 'error'` these tests are what keep the documented rule true.
5. **`PendingTasksContext`.** Its `Promise.allSettled` fan-out: one category's request failing must not blank the other three counts. That's the explicit reason the code uses `allSettled`, and it's invisible unless something fails — the exact profile of a bug that ships.
6. **`design-system/` components.** Its own Vitest setup (same deps, own `vite.config.ts` `test` block), covering `Combobox` (keyboard navigation, `onOpen` firing once), `Modal`/`ConfirmModal` (focus trap, Escape, confirm/cancel callbacks), and `Pagination` (boundary states). Keyboard and focus behaviour is where component bugs actually live and where screenshots tell you nothing.

### Explicitly out of scope

- **Leaflet map rendering** (`AreaMap`, `AreaPolygonModal`). Leaflet needs real layout and jsdom has none; testing it there means mocking so much that the test asserts the mocks. Cover the surrounding logic (viewport-bounded fetching, the polygon payload sent on save) and leave rendering to manual checking.
- **Visual/screenshot testing.** Explicitly not the goal — replacing it is the point of this work.
- **Full end-to-end.** Playwright is already vendored under `.claude/skills/run-acordanza/` and the `run-acordanza` skill remains the right tool for "does the real stack work". If a handful of E2E smoke journeys are ever wanted, they belong in that skill, kept deliberately tiny; plans 07-09 should carry the regression load instead.

## Steps

1. Add `msw`, write `src/test/setup.ts` (`setupServer`, `onUnhandledRequest: 'error'`, lifecycle hooks) and `src/test/factories.ts` typed off `api/generated.ts`.
2. Export `AuthContext` from `context/AuthContext.tsx`; write `src/test/renderWithProviders.tsx` supporting both the direct-value and real-`AuthProvider` modes.
3. Land slice 1 (`SearchPage`) complete, as the reference other page tests are modelled on.
4. Land slices 2-3.
5. Land slices 4-5.
6. Add Vitest to `design-system/` and land slice 6.
7. Update `frontend/CLAUDE.md`: the test setup, the two auth modes in `renderWithProviders`, the contract-typed-factories rule (and *why* — the `generated.ts` coupling), and the `onUnhandledRequest: 'error'` convention. Note in `/DESIGN.md` or `design-system`'s docs that components there carry their own tests.

## Working agreement (once 07-09 have landed)

Worth writing into the CLAUDE.md files so it survives this conversation:

- **Bug fix** → a failing test that reproduces it, first. This is where the suite pays for itself fastest, and it's the specific thing screenshots can never do: prove the bug is gone *and* stay behind to prove it stays gone.
- **New endpoint** → a row in plan 08's authorization matrix, plus a happy path and one edge case.
- **New page or component** → at minimum, it renders with realistic contract-typed data, and any permission-gated action is covered both ways.
- Not a coverage target. Coverage percentages would push tests toward the trivial pages and away from the authorization matrix, which is where the value actually is.
