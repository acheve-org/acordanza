# Adopt the shared presentation helpers everywhere (errors, dates, status labels)

Status: not started
Depends on: nothing. Frontend only — no backend change, no DTO change, no OpenAPI regeneration. Mechanical, but §2 fixes a user-visible bug.

## Problem

Three shared helpers exist, are documented in `frontend/CLAUDE.md`, and are bypassed by most of the call sites they were written for. Each bypass is individually trivial; together they are the reason the next person can't tell whether a helper is the rule or an option.

**1. `errorMessage()` is used by 3 files out of 24.** `api/errorMessage.ts` is the designated `ApiError` → Spanish-string helper. It is called from `RouteDetailOverlay`, `MyContributionsPage` and `SearchPage` — the three files converted to react-query. Everywhere else, the same expression is written out by hand: **37 occurrences of `err instanceof ApiError ? err.message : '…'` across 21 files** (`LoginScreen` has 4, `SettingsPage` 3, `FirstAscensionistsPage` 3, `AreaTree` 3, …). Because the helper arrived with the react-query conversion, its adoption tracks that migration rather than the actual rule, which is "this is how errors become strings".

**2. Dates render in the browser's locale on 9 of 14 sites — in a Spanish-only app.** `frontend/CLAUDE.md` and the root `CLAUDE.md` are explicit that all user-facing text is Spanish. But:

- **5 sites** pin it: `toLocaleDateString('es-ES')` — `UsersPage`, `SettingsPage`, `PendingRequestsList`.
- **9 sites** don't: bare `toLocaleDateString()` — `DetailModal` (×3: validated-by, rejected-by, "Última búsqueda"), `MyContributionsPage` (×2), `PendingTasksPage` (×2), and others.

On a browser set to `en-US`, the unpinned half renders `8/11/2026` where the pinned half renders `11/8/2026` — two different dates for the same day, sometimes on the same screen (`PendingTasksPage` shows creation dates unpinned while `PendingRequestsList`, rendered by the same page, pins them). This is a real bug, not just inconsistency: it is silently wrong for any user whose browser isn't Spanish, which is exactly the case nobody developing on a Spanish laptop ever sees.

**3. `MyAreasPage` defines a third status-label map.** `api/types.ts:164` says, in a comment:

> `// Shared by MyAreasPage/ClaimFirstAscensionistPage's self-service request flows`

`ClaimFirstAscensionistPage` does use `reviewStatusLabels`. `MyAreasPage.tsx:8` has its own local `statusLabels` instead, with different wording for the same three states ("Solicitud pendiente de aprobación" / "Experto aprobado" / "Solicitud rechazada" vs "Pendiente de aprobación" / "Aprobado" / "Rechazado"). So the two sibling pages that plan [15](15-consolidate-request-pages.md) deliberately made identical in layout now describe identical states in different words, and `types.ts` documents a sharing that half exists.

## Design

### 1. `errorMessage(err, fallback)` at every call site

Pure substitution — the helper's body *is* the expression being replaced:

```ts
setError(err instanceof ApiError ? err.message : 'No se pudo enviar la invitación');
// becomes
setError(errorMessage(err, 'No se pudo enviar la invitación'));
```

The fallback strings are kept **exactly as they are**, one per site. They are per-action Spanish copy ("No se pudo retirar la solicitud", "No se pudieron cargar los subsectores"), not a generic default worth centralising — a shared "algo ha ido mal" would be a downgrade.

Two sites are **not** substitutions and must be left alone, because they branch on `status`, not on message:

- `pages/AcceptInviteScreen.tsx:32` and `pages/ResetPasswordScreen.tsx:31` — `err instanceof ApiError && err.status === 400 ? 'invalid' : 'error'`.
- `components/DetailModal.tsx:82` — `err instanceof ApiError && (err.status === 409 || err.status === 503)`.

`components/FirstAscensionistNameModal.tsx:32` is a multi-line ternary picking between two fallbacks (create vs rename); it becomes `errorMessage(err, area ? '…' : '…')`, same as `AreaNameModal.tsx:32`.

After the sweep, `ApiError` should be imported only by `api/client.ts`, `api/errorMessage.ts` and the three status-branching files above. That is the check that the sweep is complete — and a much better invariant than a count.

### 2. `formatDate()` and one locale

New `src/format.ts`:

```ts
// Every date the UI renders is a day, in Spanish, in a Spanish-only app — never
// the browser's locale, which silently renders 8/11/2026 for 11/8/2026 on an
// en-US browser. Use this instead of calling toLocaleDateString at a call site.
export function formatDate(value: string): string {
  return new Date(value).toLocaleDateString('es-ES');
}
```

**Why `src/format.ts` and not `src/api/format.ts`.** `errorMessage` lives under `api/` because it takes an `ApiError` — it is about the API layer. A date formatter is not; it takes a string and returns display copy. `src/format.ts` (sibling to `styles.css`, `App.tsx`) is where app-wide presentation helpers belong, and it is the file the next such helper (relative times, number formatting) joins without anyone having to think about it.

**Scope: dates only.** All 14 sites format a day with no time component, so there is no `formatDateTime` in this plan — add one when something needs it, not before.

**`'es-ES'` literal, not a constant.** It appears once now.

All 14 sites become `formatDate(x)`. Three of them (`DetailModal`'s validated/rejected lines) currently use a non-null assertion — `new Date(route.decidedAt!)` — which stays as it is; `formatDate(route.decidedAt!)` keeps the same assumption in the same place rather than smuggling a behaviour change into a formatting sweep.

### 3. `MyAreasPage` uses `reviewStatusLabels`

Delete the local `statusLabels` map (`MyAreasPage.tsx:8-12`) and use `reviewStatusLabels` from `api/types.ts`, exactly as `ClaimFirstAscensionistPage` does. The rendered wording changes: "Solicitud pendiente de aprobación" → "Pendiente de aprobación", "Experto aprobado" → "Aprobado", "Solicitud rechazada" → "Rechazado".

That is the intended direction, not a regression: the subtitle already sits under a title that names the area, on a page titled "Mis sectores", so "Experto aprobado" restates context the row already carries — and the sibling page has read this way since plan 15 landed without complaint. If the shorter wording turns out to be too bare, the fix is to change `reviewStatusLabels` for both pages, not to reintroduce a local map.

**Do not touch `validationStatusLabels`.** `frontend/CLAUDE.md` is explicit that the review and validation maps stay separate even though both are keyed by `RequestStatus` — this plan removes a *third* map, it does not merge the documented two.

### 4. Optional guard rail

`.oxlintrc.json` can ban the bare call so §2 cannot silently regress:

```json
"no-restricted-syntax": ["error", {
  "selector": "CallExpression[callee.property.name='toLocaleDateString']",
  "message": "Use formatDate from src/format.ts — dates must render in es-ES, not the browser locale."
}]
```

with `src/format.ts` itself exempted via an `overrides` entry. Worth doing: this is a bug class that is invisible on a Spanish developer machine, which is precisely the kind a linter should catch instead of a person. Land it in the same PR or leave it out — the sweep stands either way.

## Steps

1. `src/format.ts` with `formatDate`, plus `src/format.test.ts` (§Testing).
2. Sweep §2: all 14 `toLocaleDateString` call sites → `formatDate`. Grep `toLocaleDateString` afterwards; only `format.ts` should match.
3. Sweep §1: all 37 `errorMessage` substitutions across the 21 files, skipping the three status-branching sites. Grep `instanceof ApiError` afterwards; only `client.ts`, `errorMessage.ts` and those three should match.
4. §3: `MyAreasPage` onto `reviewStatusLabels`; delete the local map.
5. Optional: the `.oxlintrc.json` rule from §4.
6. `bun run lint`, `bun run test`, `bun run build`.
7. `frontend/CLAUDE.md`: the `api/errorMessage.ts` sentence gains "used at every call site that turns an `ApiError` into UI copy — the only exceptions branch on `err.status`", and a new short sentence introduces `src/format.ts`/`formatDate` with the locale reasoning.

## Testing

**New — `src/format.test.ts`:** `formatDate('2026-07-12T10:00:00Z')` is `'12/7/2026'`. One test, and it is the one that would have caught the bug: it fails under any locale other than `es-ES` if the `'es-ES'` argument is ever dropped.

**Existing tests are the real safety net for §2, and some will need updating.** `pages/UsersPage.test.tsx:60` already asserts `'Invitado por Hugo el 12/7/2026 · Confirmado el 14/7/2026'` — an `es-ES` string, pinned because that site was already correct. The unpinned sites' tests pass today only because CI's Node defaults to a locale that happens to agree; after this change they assert `es-ES` deliberately. Where a test covering a newly-pinned site (`DetailModal.test.tsx`'s validated/rejected lines, `MyContributionsPage.test.tsx`'s "Creado el …") does not currently assert the rendered date, add that assertion — it is what pins the fix.

**§1 and §3 need no new tests.** §1 is a pure refactor whose behaviour is already covered wherever an error path is tested (`LoginScreen.test.tsx`, `MyContributionsPage.test.tsx`). §3 changes rendered copy on a page with no test file — that gap is real but belongs to [26](26-modal-submit-and-tests.md)'s coverage push, not here; adding a whole `MyAreasPage.test.tsx` to justify deleting five lines would be the tail wagging the dog.

`bun run test` (32 files, 126 tests) and `bun run build` must both be green. No `run-acordanza` pass needed: §2's effect is visible in the unit tests, and §3's is three words on one page.
