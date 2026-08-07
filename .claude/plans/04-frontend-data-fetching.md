# Unify frontend data fetching (adopt TanStack Query)

Status: planned (not started)
Depends on: nothing; pairs well with openapi-client-types.md but independent

## Problem

Every page hand-rolls its own fetch/refresh/error combination, inconsistently:

- `SearchPage.tsx` calls `routesApi.listRoutes(filters).then(setRoutes)` with **no** `.catch` — a failed request is a silent unhandled rejection and a blank list; same for its detail fetch.
- `MyAreasPage.tsx` does catch (`ApiError` → Spanish error string in local state) — the "correct" local pattern, but copy-pasted per page.
- The documented discipline "every mutation is followed by an explicit re-fetch" (`refreshRoutes()`/`refreshDetail()`, `PendingTasksContext.refresh()`) is a hand-rolled version of query invalidation.
- No loading states on most pages; no request dedup when two components need the same data.

## Decision

Adopt **@tanstack/react-query**. It reproduces the app's existing semantics with less code: default `staleTime: 0` matches "data is never cached beyond the current render"; `queryClient.invalidateQueries` replaces the manual refresh functions; `error`/`isPending` come free. The alternative (a homegrown `useFetch` hook) saves the dependency but re-invents invalidation and would still be bespoke — not worth it. Set `refetchOnWindowFocus: false` initially to avoid surprising refetches, revisit later.

## Constraints to preserve (from frontend/CLAUDE.md)

- **Modal/dropdown data is fetched by the thing that needs it, when it opens** — with react-query this is natural: put the `useQuery` inside the modal component, or `enabled: open` for pickers. Do not hoist queries to pages just because a provider exists.
- All user-facing error text stays Spanish; `ApiError.message` already carries the backend's Spanish `{ error }` — surface it, falling back to a generic Spanish message.
- `AuthContext` stays as-is (it's session state, not server data). `PendingTasksContext` can be converted last or left alone.

## Steps

1. `cd frontend && bun add @tanstack/react-query`. Wrap the app in `<QueryClientProvider>` in `main.tsx` with the defaults above.
2. Establish query-key conventions in one place (e.g. `src/api/queryKeys.ts`): `['routes', filters]`, `['route', id]`, `['areas']`, `['ticks', 'mine', filters, page]`, `['areaExperts', 'mine']`, `['pendingTasks']`, etc.
3. **Pilot: convert `SearchPage.tsx`** (it's the worst offender and exercises list + detail + mutations): `useQuery(['routes', debouncedFilters])`, `useQuery(['route', detailRouteId], { enabled: detailRouteId !== null })`, mutations (`deleteRoute`, `createTick`, topo upload/delete, `validateRoute`) via `useMutation` + `invalidateQueries`. Keep the 250 ms debounce as a debounced-filters value feeding the query key.
4. Add one shared error presentation for query/mutation errors (small `errorMessage(err)` util reusing the `ApiError` check; render like existing `.error-text`).
5. Convert remaining pages incrementally, one PR-sized batch at a time (suggested order: `MyTicksPage` (pagination → `keepPreviousData` or infinite query), `AreaPage`, `FirstAscensionist*`, `Settings`, management pages, modals/pickers last). Delete each page's hand-rolled `refresh*` functions as it converts.
6. Optionally convert `PendingTasksContext` into a `usePendingTasks()` query hook (its `Promise.allSettled` becomes four queries + a derived total) — keep the gating so pages with nothing to review issue no requests (`enabled:` flags).
7. `bun run build && bun run lint`; update `frontend/CLAUDE.md` (architecture bullet about no caching / explicit re-fetch must be rewritten to describe query keys + invalidation as the new discipline).

## Verification

`run-acordanza` skill driver end-to-end (it exercises search, detail modal, tick add/delete, edit, filters, delete). Manually kill the backend mid-session once to confirm pages now show a Spanish error instead of blank content.

## Rollback

Each page converts independently; the provider is additive. Abandoning mid-way leaves a mixed but working codebase — acceptable, but note the mix in frontend/CLAUDE.md if paused.
