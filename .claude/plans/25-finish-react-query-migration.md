# Finish the react-query migration (and delete four hand-rolled race guards)

Status: done
Depends on: nothing hard. Frontend only — no backend change, no DTO change, no OpenAPI regeneration. Overlaps [24](24-shared-presentation-helpers.md) in a few files (both touch error handling); land 24 first if both are queued, so the converted `useQuery` error branches are written with `errorMessage()` from the start rather than converted twice.

## Problem

`frontend/CLAUDE.md` describes the data-fetching layer as "converting to react-query, page by page", with the older `useState`/`useEffect`/`refresh*` pattern still in place "until they convert too". That was an honest description of work in flight. It is now a description of a stall:

- **Converted: 3 files** — `SearchPage` (its list query), `MyContributionsPage`, `RouteDetailOverlay`.
- **Not converted: 17 pages, 8 components, and `PendingTasksContext`.**
- **`api/queryKeys.ts` has 2 entries**, and its own comment ("add an entry here as each page converts") has been waiting on 20 pages.

Two styles side by side is a reasonable transitional state; it is a bad steady state, and after plans 19, 20 and 21 all landed without moving the needle, this is a steady state. The concrete costs:

**1. Four different hand-rolled solutions to the same race.** Five pages debounce a search box and fetch on the debounced value, with three different guards against an earlier response landing after a later one:

| site | guard |
|---|---|
| `FirstAscensionistsDirectoryPage` | `let active = true` + cleanup |
| `RequestFirstAscensionistLinkModal` | `let cancelled = false` + cleanup |
| `MyTicksPage` | `requestIdRef` counter compared on resolve |
| `UsersPage` | `let active = true` in the effect — **but none in `refreshUsers()`**, the post-invite refresh path |
| `SearchPage` | none needed: it debounces the *value* and lets the query key change |

`SearchPage` is the one that got it right, and it got it right by not solving the problem at all — a query keyed on the debounced filters can't be raced, because react-query owns which response is current. Every other site is hand-maintaining an invariant the library provides, and `UsersPage` shows what that costs: the same file has the guard in one path and not the other, which is exactly how this class of bug survives review.

**2. Refetch-after-mutation is wired by hand, per page.** `MyAreasPage`, `ClaimFirstAscensionistPage`, `MyInvitesPage`, `UsersPage`, `AreaTree`, `PendingTasksPage` and others each define a local `refresh()`/`refreshList()` and call it from every mutation handler. Nothing enforces that a new mutation remembers to call it, and cross-page staleness is invisible: approving a request on `/manage/pending-tasks` cannot refresh `/my-acordanza/my-areas`, because the two pages share no cache — only a query key could connect them.

**3. Dependent fetches are expressed as effect chains.** `AreaPage` fetches an area by slug path, then fetches that area's routes in a second effect keyed on `area?.id`, with two `// eslint-disable-next-line react-hooks/exhaustive-deps` comments holding it together. That is `enabled: !!area` written the long way, and the disables are load-bearing rather than incidental — the honest signal that the pattern is fighting the tool.

## Design

### 1. `useDebouncedValue`, and the guards go

New `src/hooks/useDebouncedValue.ts` — the generalisation of what `SearchPage` already does inline:

```ts
// Debounce the *value*, not the fetch: a query keyed on the debounced value
// can't be raced by an earlier response landing late, so call sites need no
// `active`/`cancelled`/requestId guard of their own.
export function useDebouncedValue<T>(value: T, delayMs = 250): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const handle = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(handle);
  }, [value, delayMs]);
  return debounced;
}
```

`250` as the default matches every existing call site. `SearchPage` adopts it too, replacing its inline effect. `AreaMap`'s viewport-bounded fetch keeps its own `fetchHandle` ref with a different (300 ms) debounce over map events rather than over a state value — leave it alone; it is not the same shape.

### 2. Query keys, all at once

`api/queryKeys.ts` grows an entry per resource as each slice lands, staying the single source of key shapes it already claims to be. The shape follows what is there: `['routes', filters]`, `['contributions', kind, page]` — resource name first, then the parameters that make the response different.

Mutations invalidate by **prefix**, which is what makes cross-page staleness go away: `queryClient.invalidateQueries({ queryKey: ['area-experts'] })` after approving an area-expert request refreshes both `/manage/pending-tasks` and `/my-acordanza/my-areas` without either page knowing about the other.

### 3. Slices

Independently landable, in this order. Each is a PR; none depends on a later one.

**Slice A — foundation.** `useDebouncedValue` + its test. `SearchPage` adopts it (no behaviour change; proves the hook against the one already-correct site).

**Slice B — debounced list pages.** `UsersPage`, `FirstAscensionistsPage`, `FirstAscensionistsDirectoryPage`. Each becomes `useDebouncedValue(filters)` + one `useQuery`, and each deletes its guard, its `refresh*`, and its `error` state (the query's `error` plus `errorMessage()` replaces it). This is where the §1 bug in `UsersPage` disappears rather than being fixed.

**Slice C — `MyTicksPage`.** The infinite-scroll page: `useInfiniteQuery` with `getNextPageParam` derived from `items.length < totalCount`, keeping the `IntersectionObserver` sentinel exactly as it is and calling `fetchNextPage()` from it. Deletes `requestIdRef`, the manual `items`/`page`/`totalCount`/`loading` state and `refreshList()`. Its own slice because it is the only page using this pattern and the conversion is genuinely different from B.

**Slice D — simple list pages and their mutations.** `ExplorePage`, `MyInvitesPage`, `MyAreasPage`, `ClaimFirstAscensionistPage`, `MyOpeningsPage`, `FirstAscensionistPage`. One `useQuery` each; withdraw/cancel/invite become `useMutation` with `onSuccess: invalidateQueries`. Note `MyAreasPage`/`ClaimFirstAscensionistPage` currently call `refresh()` *unconditionally after* a failed withdraw (the `catch` sets an error, then `refresh()` runs anyway) — `useMutation`'s `onSuccess` makes that ordering explicit rather than accidental.

**Slice E — `AreaPage`.** The dependent chain: `useQuery` for the area by slug path, then `useQuery` for its routes with `enabled: !!area`, deleting both `exhaustive-deps` disables and the `notFound` state (`error` + `ApiError.status === 404` carries it). Media upload/remove become mutations invalidating the area key.

**Slice F — `PendingTasksContext`.** The five `Promise.allSettled` categories become five `useQuery` calls, each with its own `enabled` flag (the existing `showRoutes`/`showAreas`/… permission gates map to `enabled` one-to-one), and `total` sums their `data?.length ?? 0`. This deletes the `allSettled` block, the five `console.error` branches and the manual `loading` state, and — the real win — lets `PendingTasksPage`'s approve/reject mutations invalidate these keys directly instead of calling `refresh()` through context. The context keeps its shape and its `usePendingTasks()` consumers; only its insides change.

**Slice G — pickers, trees and modals.** `AreaPicker`, `FirstAscensionistPicker` (`Combobox`'s `onOpen` becomes `enabled`-gated queries), `RequestAreaExpertModal`, `RequestFirstAscensionistLinkModal`, `AreaTree` (per-node children queries keyed `['areas', 'children', id]`, replacing the local `children` state and `loadChildren()`), `AreaPolygonModal`. **The "modal/dropdown data is fetched by the thing that needs it, when it opens" rule survives intact** — `enabled` is how it is expressed, and MSW's `onUnhandledRequest: 'error'` keeps enforcing it mechanically. This slice is last because it is the one that could regress that rule if done carelessly.

### 4. Not converting

- **`AcceptInviteScreen`, `ResetPasswordScreen`, `ConfirmEmailChangeScreen`.** Each does one `POST /auth/validate-token` on mount, outside the auth gate, never refetched, never invalidated by anything. A query key would be ceremony around a single fire-and-forget call.
- **`RouteFormPage`'s load-for-edit.** Same argument: one fetch keyed on the route id, feeding form state that the user then edits. Putting server state that is *immediately copied into local form state* behind a cache invites the classic "the form reset while I was typing" bug. Its submit could become a mutation that invalidates the routes key — worth it, and small enough to fold into slice D.
- **`SettingsPage`'s three sections.** `PasskeysSection` has a genuine list+mutate shape and could convert; the profile and password sections are pure submits. Low value either way; fold into slice G only if it is free.
- **`queryClient.ts`'s defaults.** `staleTime: 0` and `refetchOnWindowFocus: false` stay. The comment there says they exist to match the app's "never cache beyond the current render" discipline and to avoid surprise refetches "until we've lived with the library a bit" — that judgement is about UX, and revisiting it is a separate decision that should be made deliberately, on its own, once most pages are actually on the library.

## Steps

Per slice: convert, delete the state/guards/`refresh*` the conversion makes dead, add the `queryKeys` entries, update or add the page's tests, run `bun run lint && bun run test && bun run build`.

After the last slice:

1. Grep for `refresh` and `setTimeout` under `src/` — the only survivors should be `AreaMap`'s map-event debounce and `useDebouncedValue` itself.
2. `frontend/CLAUDE.md`: rewrite the two data-fetching bullets. The "converting page by page … don't be surprised to find both styles side by side" paragraph and the "pre-react-query pattern, still in use on unconverted pages" bullet both go, replaced by a statement of the single rule (queries via `useQuery` keyed through `api/queryKeys.ts`; mutations invalidate by prefix; the exceptions in §4 named explicitly, with their reasons). Add `useDebouncedValue` next to `useDisclosure` in the hooks sentence.

## Testing

The suite is what makes this safe to do in slices: 32 files / 126 tests already cover most of these pages against MSW, asserting **rendered output**, not fetch mechanics. A correct conversion therefore leaves the existing assertions untouched — if a test needs rewriting to accommodate a conversion, that is the signal the conversion changed behaviour.

Per slice:

- **Existing tests pass unchanged.** `UsersPage.test.tsx`, `MyTicksPage.test.tsx`, `AreaPage.test.tsx`, `FirstAscensionistPage.test.tsx`, `PendingTasksContext.test.tsx`, `AreaPicker.test.tsx` and `AreaTree.test.tsx` all cover slice targets directly.
- **`renderWithProviders` already supplies a fresh `QueryClient` with `retry: false`** per test, so error-path tests fail fast rather than retrying — no harness change is needed for any slice.
- **New, slice A:** `useDebouncedValue` — value updates once after the delay, not per keystroke (fake timers).
- **New, slices B–D:** for one converted page per slice, a test that a mutation refetches the list (register a handler returning N rows, mutate, assert the second response's rows render) — this is what pins the `invalidateQueries` wiring that replaces `refresh()`.
- **New, slice F:** `PendingTasksContext.test.tsx` gains a case where one category's endpoint fails and the badge still counts the other four — the `allSettled` behaviour the current implementation has a comment about but, being per-query now, needs an assertion to keep.
- **Slice G is guarded by the harness itself:** `onUnhandledRequest: 'error'` fails any test where a picker or modal fetches before it opens. Do not relax it for a query's `enabled` flag; that flag is the rule.

Finally, one `run-acordanza` pass over the four sections (Busca/Explora/Sigue/Mi acordanza) plus `/manage/pending-tasks`, since the cross-page invalidation in slice F is the one behaviour no jsdom test observes: approve a request in "Tareas pendientes" and confirm the header bell count drops without a reload.
