# Paginate "Contribuciones": three lazy endpoints, one per kind

Status: planned (not started)
Issue: [#149](https://github.com/acheve-org/acordanza/issues/149)
Depends on: [10](10-my-contributions.md) (the page, the controller and the `ContributionDto` this reshapes). Backend + frontend, no schema change, no migration.

## Problem

`GET /api/contributions/mine` (plan 10, §5) returns **everything** the caller ever created — every route, every area, every first ascensionist — in one unpaginated payload, in one round trip, on every visit to `/my-acordanza/contributions`. Plan 10 named this as a deliberate, accepted risk ("at this app's scale a contributor has tens of rows … if a prolific user makes it unpleasant, add per-tab paging"). Issue #149 is that moment.

Two costs, and the second is the one that actually bites:

1. **Unbounded response.** A prolific contributor's payload grows without limit, and each row carries a joined decider name and a server-computed area breadcrumb.
2. **The page pays for all three tabs to render one.** `routes` is the default tab; `areas` and `firstAscensionists` are fetched, serialized and shipped whether or not the user ever clicks them. The whole rest of the frontend follows the opposite rule — "modal/dropdown data is fetched by the thing that needs it, when it opens" (`frontend/CLAUDE.md`), enforced mechanically in tests by MSW's `onUnhandledRequest: 'error'`. This page is the outlier.

The issue also asks to drop the per-tab count badges. That isn't cosmetic bundling: a count badge on an unvisited tab is precisely what would force that tab's data to be fetched eagerly. Removing the badges is what *makes* lazy loading possible, so the two halves of the issue are one change.

## Design

### 1. Three endpoints replacing one

`ContributionsController` gains three actions and loses `GetMine`:

| Endpoint | Returns |
|---|---|
| `GET /api/contributions/routes?page=&pageSize=` | `PagedContributionsResponse` |
| `GET /api/contributions/areas?page=&pageSize=` | `PagedContributionsResponse` |
| `GET /api/contributions/first-ascensionists?page=&pageSize=` | `PagedContributionsResponse` |

```csharp
public record PagedContributionsResponse(List<ContributionDto> Items, int TotalCount, int Page, int PageSize);
```

`ContributionDto` is **unchanged** — same eight fields, same lowercased `ValidationStatus`, same `Detail` semantics per kind. Only the envelope changes. `MyContributionsResponse` is deleted; nothing else references it.

Decisions inside that table:

**Three paths, not one `?kind=` parameter.** The issue asks for three endpoints, and they earn it: each has a different `DbSet`, a different `Detail` projection, and — see §2 — a different service dependency. A `kind` query parameter would be a switch statement wearing a route parameter's clothes, and would give the frontend a stringly-typed URL where a typo is a 400 instead of a compile error.

**No `mine` segment in the paths.** The whole controller is "the caller's own contributions" — there is no notion of anyone else's, and every action derives its filter from `userManager.GetUserId(User)` with no id ever accepted from the client. `/contributions/routes` reads correctly and keeps the paths short; the class-level comment already states the scoping, and §5's tests pin it.

**`PagedContributionsResponse` is a new record, not a generic `Paged<T>`.** The codebase has three sibling envelopes already — `PagedAreaNodesResponse`, `PagedMyTicksResponse`, `PagedUsersResponse` — each a concrete record with the same four members. A fourth concrete one is consistent; retrofitting a generic across all four is a separate, larger refactor with a real cost (generics in the OpenAPI document produce names like `PagedResponseOfContributionDto` in the generated frontend types) and no behavioural gain.

**Paging shape copies `UsersController` verbatim**: `private const int DefaultPageSize = 10; private const int MaxPageSize = 100;`, `if (page < 1) page = 1; if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;`. Default 10 is what the issue asks for and what `UsersController`/`AreasController` already use. Out-of-range values are clamped rather than rejected, same as everywhere else — a paging parameter is not user input worth a 400.

### 2. Query shape: page in SQL, project in memory

Today each list is materialised in full (`ToListAsync()`) and then `Select`ed in memory, because `AreaHierarchyService`'s `NamePath` is a client-side lookup over an in-memory snapshot and can't be translated. With paging that ordering must flip — `Skip`/`Take` has to run in the database, or the change buys nothing:

```csharp
var query = db.Routes.AsNoTracking().Where(r => r.AddedByUserId == userId);
var totalCount = await query.CountAsync();
var rows = await query
    .Include(r => r.DecidedBy)
    .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
    .Skip((page - 1) * pageSize).Take(pageSize)
    .ToListAsync();
```

then the same in-memory `Select` into `ContributionDto` as today. Two round trips per request (count + page), exactly like `UsersController.ListUsers`.

**`.ThenByDescending(x => x.Id)` is new and load-bearing.** `CreatedAt` alone is not a total order — `DateTimeOffset.UtcNow` at construction time can collide across rows created in the same request or the same tick, and Postgres is free to return tied rows in any order per query. Unpaginated that was invisible; paginated it means a tied row can appear on two pages and another on none. `Id` is the tie-break the other paginated endpoints get implicitly from their unique sorts.

**`AreaHierarchyService` is only injected-and-used by two of the three actions.** The first-ascensionists action passes `null` for `Detail` and needs no hierarchy snapshot at all — so with lazy tabs, opening only the Aperturistas tab no longer materialises the whole area tree. The service stays a constructor dependency (it's scoped and cheap to inject; `GetAsync()` is what costs), just called in two actions instead of one.

**No index work.** `AddedByUserId` is a foreign key on all three tables, so EF already created the index this filter wants (plan 10, §2).

### 3. Frontend: one query, keyed by tab and page

`api/contributions.ts` replaces `getMyContributions` with one parameterised wrapper:

```ts
export type ContributionKind = 'routes' | 'areas' | 'first-ascensionists';

export function listMyContributions(kind: ContributionKind, page: number, pageSize = 10): Promise<PagedContributions> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  return api.get<PagedContributions>(`/contributions/${kind}?${params}`);
}
```

One function rather than three, deliberately — the three endpoints are shape-identical and the only caller selects between them from tab state, so three wrappers would be three identical bodies plus a lookup table at the call site. The union type keeps the path segment as safe as three named functions would.

`api/types.ts`: `MyContributions` is deleted, and gains

```ts
export type PagedContributions = Omit<components['schemas']['PagedContributionsResponse'], 'items'> & {
  items: Contribution[];
};
```

The `Omit`/re-add is the same narrowing `Contribution` itself already does — the backend's `validationStatus` is a plain `string` in the contract, and the frontend narrows it to `RequestStatus` so `Chip status={...}` and `validationStatusLabels[...]` keep type-checking.

`api/queryKeys.ts`: `myContributions()` → `contributions: (kind: ContributionKind, page: number) => ['contributions', kind, page] as const`.

**`MyContributionsPage`:**

- Tab state as today, but the tab keys become the API's kind strings (`'routes' | 'areas' | 'first-ascensionists'`) so no mapping layer is needed; `tabLabels` keys move with them.
- Page state is **per tab** — `useState<Record<ContributionKind, number>>({ routes: 1, areas: 1, 'first-ascensionists': 1 })` — which is what "a user can paginate them independently" means in practice: switching to Sectores and back to Vías returns to the page you were on rather than resetting to 1.
- **One** `useQuery`, keyed on the *active* tab and its page:

  ```ts
  const { data, error } = useQuery({
    queryKey: queryKeys.contributions(activeTab, pages[activeTab]),
    queryFn: () => contributionsApi.listMyContributions(activeTab, pages[activeTab]),
    placeholderData: keepPreviousData,
  });
  ```

  This is the lazy loading the issue asks for, with no `enabled` flags and no conditional hooks: a tab that is never active is never part of a query key, so its endpoint is never called. `placeholderData: keepPreviousData` keeps the current rows on screen while the next page loads instead of flashing the empty state — the global `LoadingBar` (`main.tsx`) already signals the in-flight request, so the page needs no loading state of its own.
- **`Badge` and its import go.** The tab buttons render only `tabLabels[tab]`. Nothing else on the page changes shape: `List`/`ListRow`, the `Chip` trailing status and `ContributionSubtitle` are all untouched.
- `Pagination` (`acordanza-design-system`) below the list, rendered only when `pageCount > 1`, with `pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE))` — the same expression `ExplorePage` and `UsersPage` use.
- Empty state (`EmptyState`) and the error branch stay as they are, now scoped to the active tab, which is the honest reading: one tab failing to load says nothing about the other two.

### 4. What this does not change

- **`ContributionDto` and everything it carries** — status, decider, rejection reason, `Detail`. This is a paging change, not a content change.
- **Which records appear.** Still `AddedByUserId == caller`, still including pending/rejected/approved alike, still excluding pre-existing rows with no recoverable creator (plan 10, §2).
- **Server-side counts of any kind.** The issue is explicit: no contribution counts. `TotalCount` in the envelope is a paging total, not a badge feed — it exists to compute `pageCount` for the active tab only.
- **Any other page.** `queryKeys.myContributions()` and `getMyContributions` have exactly one caller each; nothing invalidates them from elsewhere.

## Steps

1. **Backend.** `Dtos/ContributionDtos.cs`: add `PagedContributionsResponse`, delete `MyContributionsResponse`. `Controllers/ContributionsController.cs`: replace `GetMine` with the three actions (§1–§2), including the page-size constants and the `Id` tie-break.
2. **Regenerate the contract.** `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` (Postgres + Azurite up, `Jwt:Key` set), then `bun run generate-api` in `frontend/`; commit both diffs — CI's `openapi-contract-check` fails the build otherwise.
3. **Frontend.** `api/types.ts` (`PagedContributions` in, `MyContributions` out), `api/contributions.ts`, `api/queryKeys.ts`, then `pages/MyContributionsPage.tsx` (tab keys, per-tab page state, single keyed query, `Pagination`, `Badge` removed).
4. **Tests** (§5).
5. **Docs.** `backend/CLAUDE.md`: the `ContributionsController` bullet currently says `GET /mine` — becomes the three paginated per-kind endpoints. `frontend/CLAUDE.md`: the `MyContributionsPage` sentence gains the per-tab lazy/paginated shape and drops nothing else.
6. **Verify** with the `run-acordanza` skill: as a user with more than 10 routes created, confirm the Vías tab pages through them newest-first, that switching to Sectores issues its first request only then (browser network tab), and that returning to Vías lands back on the page you left.

## Testing

**Backend** — new `backend/Acordanza.Api.Tests/Controllers/ContributionsTests.cs`, `IntegrationTestBase` + `[Trait("Category", "Integration")]`, seeded via `SeedData`:

- Each of the three endpoints returns only the caller's own rows — a second user's route/area/first ascensionist never appears (the scoping that replaces §1's absent `mine` segment).
- 25 seeded routes → `page=1` returns 10 items with `totalCount: 25`, `page=3` returns 5, `page=4` returns empty with the same `totalCount`; no row appears on two pages and none is missing across the three pages.
- Ordering is `CreatedAt` descending; rows seeded with an identical `CreatedAt` still partition cleanly across pages (the `Id` tie-break).
- `page=0`/`pageSize=0`/`pageSize=1000` clamp rather than 400, and `pageSize=5` is honoured.
- Status passthrough: a rejected route surfaces `validationStatus: "rejected"` with its `rejectionReason` and `decidedByName` — one row, to prove the projection survived the reshape.
- Anonymous request → 401 on all three.

**Frontend** — new `frontend/src/pages/MyContributionsPage.test.tsx` (the page has no test today), using `renderWithProviders` + MSW handlers built from a `makeContribution` factory added to `src/test/factories.ts`:

- Renders the routes tab with contract-typed rows, showing name, status chip and rejection reason.
- **Lazy loading, mechanically**: register a handler for `/contributions/routes` only, render, and assert the page settles — `onUnhandledRequest: 'error'` fails the test if the page also hits `areas` or `first-ascensionists`. Then register the areas handler, click the Sectores tab, and assert its rows appear. This is the one assertion that keeps §3's design from silently regressing.
- Independent paging: page forward on Vías, switch to Sectores (page 1) and back — Vías is still on page 2.
- `Pagination` is absent when `totalCount <= 10`.
- No count badge is rendered on any tab.

`dotnet test Acordanza.slnx` from the repo root (Docker up) and `bun run test` + `bun run build` in `frontend/`.
