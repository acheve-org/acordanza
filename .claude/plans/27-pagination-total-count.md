# `Pagination` takes `totalCount`/`pageSize` and hides itself when there's one page

Status: done
Depends on: nothing. `design-system/` + four frontend call sites. No backend change, no DTO change, no OpenAPI regeneration. The smallest plan in this batch — worth landing as a warm-up.

## Problem

`Pagination` (`design-system/src/components/Pagination/Pagination.tsx`) takes `page`, `pageCount`, `onPageChange`. But no API in this app returns a page *count* — every paginated endpoint returns `{ items, totalCount, page, pageSize }` (`PagedUsers`, `PagedAreaNodes`, `PagedContributions`, `PagedMyTicks`). So every call site converts, and writes the same line to do it:

```
components/AreaTree.tsx:201    const totalPages = Math.max(1, Math.ceil(r.totalCount / r.pageSize));
components/AreaTree.tsx:228    const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
pages/UsersPage.tsx:98         const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
pages/MyContributionsPage.tsx:38 const pageCount = Math.max(1, Math.ceil((data?.totalCount ?? 0) / PAGE_SIZE));
pages/ExplorePage.tsx:23       const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
```

Five copies of one expression, in two spellings (`totalPages`/`pageCount`), including two in the same file. The `Math.max(1, …)` is not decoration — without it an empty result renders "Página 1 de 0" — which means each call site is independently responsible for remembering an edge case that has exactly one right answer.

**And they've drifted on the more visible question: whether to render at all.**

| call site | renders when there's one page? |
|---|---|
| `ExplorePage` | no — `{pageCount > 1 && <Pagination … />}` |
| `MyContributionsPage` | no — `{pageCount > 1 && (…)}` |
| `UsersPage` | **yes** — unconditional |
| `AreaTree` | **yes** — unconditional |

So `/manage/users` with three accounts and `/manage/areas` with two zones show a dead "Anterior — Página 1 de 1 — Siguiente" control with both buttons disabled, while `/explore` and `/my-acordanza/contributions` correctly show nothing. Four call sites, two behaviours, no reason for the difference beyond who wrote which page.

## Design

### 1. New props

```tsx
export interface PaginationProps {
  page: number
  totalCount: number
  pageSize: number
  onPageChange: (page: number) => void
}

export function Pagination({ page, totalCount, pageSize, onPageChange }: PaginationProps) {
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))
  if (pageCount <= 1) return null
  …
}
```

**Replace `pageCount`, don't add alongside it.** A component that accepts either `pageCount` or `totalCount`+`pageSize` needs a union type, a runtime branch and a doc comment explaining when to use which — for four call sites, all of which have `totalCount` and `pageSize` in hand. This is a private workspace package with no external consumers; a breaking prop change costs one commit.

**Returning `null` at one page is the component's call, not the caller's.** It is a presentational rule about a control that has nothing to control — exactly the kind of decision a design-system component should own, and the reason two of four callers got it wrong is that it was left to them. Callers drop their `{pageCount > 1 && …}` wrappers; `UsersPage` and `AreaTree` get the fix for free.

The one thing to check before landing: `.pagination`'s margins in `Pagination.css`. If a page relies on the control's top margin for spacing below its list, that page will tighten up when the control disappears on a short list. Fix it on the page, not by rendering an invisible control.

### 2. Call sites

Each drops its `totalPages`/`pageCount` line and its conditional wrapper:

```tsx
// pages/UsersPage.tsx — before
const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
…
<Pagination page={filters.page} pageCount={totalPages} onPageChange={(p) => set('page', p)} />

// after
<Pagination
  page={filters.page}
  totalCount={result.totalCount}
  pageSize={result.pageSize}
  onPageChange={(p) => set('page', p)}
/>
```

`ExplorePage` and `MyContributionsPage` pass their local `PAGE_SIZE` constant (their endpoints' page size is fixed client-side); `UsersPage` and `AreaTree` pass the server-reported `result.pageSize`, which is the more honest source — it reflects any server-side clamping (`UsersController` clamps `pageSize` to `MaxPageSize`).

### 3. Not in scope

The `PAGE_SIZE = 10` constants duplicated across `ExplorePage`, `AreaTree`, `MyContributionsPage` and `MyTicksPage` (which uses 20). They look like the same duplication but aren't: each is that page's request parameter, and centralising them would couple four unrelated screens' page sizes into one number nobody can change independently.

## Steps

1. `design-system/src/components/Pagination/Pagination.tsx`: new props, internal `pageCount`, early `null`.
2. `design-system/src/components/Pagination/Pagination.test.tsx`: update for the new props, add the one-page and zero-results cases (§Testing).
3. Update the four call sites: `ExplorePage`, `MyContributionsPage`, `UsersPage`, `AreaTree` (two spots). `tsc -b` finds them all — the prop change is breaking on purpose.
4. `bun run test` in `design-system/`, then `bun run lint && bun run test && bun run build` in `frontend/` (`prebuild`/`pretest` rebuild the design system first, so no manual build step).
5. `/DESIGN.md`: update the `Pagination` entry's props and note that it renders nothing below two pages.

## Testing

**`design-system/`** — its own Vitest suite already has `Pagination.test.tsx`:

- `totalCount: 25, pageSize: 10` → "Página 2 de 3", "Anterior" enabled, "Siguiente" enabled on page 2 and disabled on page 3.
- **`totalCount: 5, pageSize: 10` renders nothing** (the new rule).
- **`totalCount: 0` renders nothing** — the case the old `Math.max(1, …)` existed to paper over, now a component-level assertion instead of five call-site copies.

**`frontend/`** — existing tests carry the call sites, but only positively: `MyContributionsPage.test.tsx` asserts `'Página 2 de 2'` renders (lines 78, 85) and nothing anywhere asserts the control's *absence*. Plan [19](19-contributions-pagination.md) called for a "`Pagination` is absent when `totalCount <= 10`" case; it never landed. Add it now — to `MyContributionsPage.test.tsx`, where it was always meant to be, and to `UsersPage.test.tsx`, whose behaviour actually changes with this plan (three users, and the dead control is gone).

No `run-acordanza` pass needed — the change is fully observable in jsdom.
