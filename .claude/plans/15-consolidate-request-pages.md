# Consolidate the "Mi acordanza" request pages onto the "Mis invitaciones" layout

Status: done
Issue: [#150](https://github.com/acheve-org/acordanza/issues/150)
Depends on: nothing. Mostly frontend — no DTO change and no OpenAPI regeneration — but §5 (one first-ascensionist claim per user) is a rule the backend does not enforce today, so it also carries a controller change and one migration. The test harness from [08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is in `main`, so both sides get real tests.

## Problem

`/my-acordanza` has three pages that are the same thing — a list of the user's records plus one primary action that opens a modal — and three different layouts for it.

`pages/MyInvitesPage.tsx` is the one that reads right:

```
.users-page-header  →  <h1>Mis invitaciones</h1> ····· [ Invitar usuario ]
<p>                 →  Te quedan 3 de 5 invitaciones.
.error-text         →  (only on failure)
List | EmptyState   →  the rows, unwrapped
```

The action is where the eye goes first (top right, on the title's line), the explanatory line sits directly under the title, and the rows are the `List`/`ListRow` cards on the page background.

`pages/MyAreasPage.tsx` and `pages/ClaimFirstAscensionistPage.tsx` invert that. Both put the title alone on its line, wrap the list in a `.settings-section` card (so `List`'s already-carded rows sit on a second card — a box inside a box that carries no information), and then put the primary action *below* the list in a **second** `.settings-section` with its own `<h2>` — so on any account with a few requests, the only button on the page is below the fold and the error text is down there with it, far from the list it usually refers to. `MyAreasPage` adds a third divergence on top: a `.expert-banner` (tinted, bordered, shield icon) between the title and the list, where `MyInvitesPage` has a plain `<p>`.

Issue #150: make both look like `MyInvitesPage`, with the request button top right.

Nothing about *behaviour* is wrong here — the API calls, the withdraw action, the modals, the "excluded ids" wiring all stay exactly as they are. This is a layout change.

## Design

### 1. The shared skeleton

All three pages end up rendering the same shape, in this order:

```tsx
<div className="page-header">
  <h1>{title}</h1>
  <Button type="button" onClick={openModal}>{actionLabel}</Button>
</div>

<p>{one-line explanation}</p>

{error && <p className="error-text">{error}</p>}

{items.length === 0 ? <EmptyState>…</EmptyState> : <List>…</List>}

{modalOpen && <TheModal … />}
```

`.side-nav-content` (the `MyAcordanzaLayout` shell) is already `display: flex; flex-direction: column; gap: var(--space-lg)`, so these are direct children and need no wrapper of their own — that is exactly how `MyInvitesPage` gets its spacing today.

Three things drop out of the two pages: the `.settings-section` wrappers (both instances, on both pages), the `<h2>` headings that titled the action sections (`Solicitar acceso a un sector`, `Solicitar vínculo` — the button's own label already says it, and `MyInvitesPage` has no such heading), and `MyAreasPage`'s `.expert-banner` markup (§3).

### 2. Rename `.users-page-header` → `.page-header`

The class is not about users: `UsersPage`, `FirstAscensionistsPage` and `MyInvitesPage` all use it already, and this plan adds two more. Rename it in `styles.css` (both the base rule at ~line 622 and the `flex-direction: column` override in the trailing `720px` block at ~line 864) and at all five call sites. No test asserts on it (checked), and it's not a design-system class — it's app-shell layout, so it stays in `frontend/src/styles.css` rather than moving to `design-system/`.

Scope note: `.users-toolbar` and `.users-sort-bar` are *not* renamed here even though `FirstAscensionistsPage` borrows the former. They're a search/sort bar, neither of these two pages has one, and dragging them in makes this a CSS-naming PR instead of the layout fix the issue asked for.

The `720px` override already does the right thing for the new pages — the header stacks and the button goes full-bleed under the title — so the mobile step needs no new rule, and nothing is added to that trailing block. (It must stay last in the file; see `frontend/CLAUDE.md`.)

### 3. `MyAreasPage`'s expert banner becomes the intro paragraph

The banner is the single biggest visual difference between this page and `MyInvitesPage`, and "make it look like `MyInvitesPage`" means the tinted box goes and its sentence stays, as the plain `<p>` under the title — the slot where `MyInvitesPage` puts `Te quedan N de M invitaciones.`

Copy is unchanged, minus the line break the old markup had mid-sentence:

> Como experto de un sector puedes validar las vías de ese sector y de sus subsectores desde la ficha de cada vía.

`.expert-banner`, `.expert-banner-icon`, `.expert-banner h2`, `.expert-banner p` and `.expert-banner strong` (`styles.css` ~530–553) have no other consumer once this lands — delete all five rules rather than leaving them as dead CSS. The inline `<svg>` shield goes with them.

The alternative — keep the banner and give `MyInvitesPage` and `ClaimFirstAscensionistPage` one too — was considered and rejected: it consolidates in the wrong direction (the issue names `MyInvitesPage` as the target), and a permanent tinted callout on every page stops reading as emphasis once it's on every page.

`ClaimFirstAscensionistPage` has no intro line at all today, so it gets one written for this plan, in the same register:

> Al vincularte con un aperturista podrás gestionar sus vías y aparecerás como tal en su ficha.

If that overstates what the link actually grants at implementation time, check `DynamicPermissions` in the backend and trim the sentence to match — do not invent capability in the copy.

### 4. Per-page details

**`pages/MyAreasPage.tsx`** — header `Mis sectores` + `Solicitar acceso`; intro from §3; error under the intro; the existing `EmptyState`/`List`/`RemoveButton` block unwrapped and otherwise untouched; `RequestAreaExpertModal` with the same `excludedAreaIds` computation.

**`pages/ClaimFirstAscensionistPage.tsx`** — header `Vincular aperturista` + `Solicitar vínculo`; intro from §3 (superseded by §5's state-dependent line); same treatment of the list; plus the one-claim rule in §5.

**`pages/MyInvitesPage.tsx`** — only the class rename touches it.

`MyAreasPage`'s button gets no `disabled` condition: a user can legitimately be expert of many sectors, there's no quota, and "you already have a pending request for every area" is the modal's own empty state to report — a page-level check for that would need the modal's data, which `frontend/CLAUDE.md` explicitly rules out (a page must not fetch what only a modal consumes). `ClaimFirstAscensionistPage` is the opposite case, and §5 covers why.

### 5. One first-ascensionist claim per user

An account can be linked to one aperturista and one only. So the `Solicitar vínculo` button is disabled once the user has a claim — and, more importantly, the backend has to say so too, because right now it doesn't.

**What counts as "has a claim": any request whose status is not `rejected`** — i.e. `pending` or `approved`. Approved is obvious (they're linked). Pending counts because the point of the rule is that you don't get to shop around: submitting three claims and seeing which one a moderator waves through is exactly the thing one-per-user exists to prevent. Rejected does not count, or a single rejection would lock the account out of ever claiming anyone, including the right person after a mistaken name.

#### Frontend

```tsx
const activeClaim = mine.find((r) => r.status !== 'rejected');
```

`disabled={activeClaim !== undefined}` on the header button. A disabled button with no stated reason is a dead end, so the intro `<p>` from §3 carries the reason — the same device `MyInvitesPage` uses, where `Te quedan 0 de 5 invitaciones.` is what explains its disabled button:

| state | line |
|---|---|
| no active claim | `Al vincularte con un aperturista podrás gestionar sus vías y aparecerás como tal en su ficha. Solo puedes vincularte con un aperturista.` |
| `pending` | `Tu solicitud para vincularte con {name} está pendiente de aprobación. Solo puedes vincularte con un aperturista, así que no puedes solicitar otro vínculo mientras tanto.` |
| `approved` | `Estás vinculado con {name}. Solo puedes vincularte con un aperturista; si necesitas cambiarlo, pídeselo a un moderador.` |

That last sentence is a statement of fact, not a promise of a feature — there is no self-service unlink, and building one is not in scope here (§6).

`activeRequestFirstAscensionistIds` and the modal's `excludedFirstAscensionistIds` prop both become dead and should be deleted, not left as belt-and-braces: the modal can now only ever open when the user has zero active claims, so the exclusion set is always empty. The modal's other filter, `!fa.linkedUserName`, stays — that one hides aperturistas already claimed by *other* accounts and is still load-bearing. This is the only change to `RequestFirstAscensionistLinkModal`.

#### Backend — the rule is not enforced today

Worth being blunt about, since a disabled button is a hint and not a rule, and someone with the endpoint can still POST:

- `FirstAscensionistLinksController.RequestLink` only rejects a duplicate **pending request for the same aperturista** (the `alreadyPending` check, backed by the unique filtered index on `(FirstAscensionistId, UserId) WHERE Status = Pending`). Pending requests for two *different* aperturistas are accepted today.
- `Approve` guards that the *target* is unclaimed (`ExecuteUpdateAsync` conditioned on `f.UserId == null`, with the auto-reject-on-conflict path below it). It does not guard that the *requester* is unclaimed.
- `FirstAscensionist.UserId` has no unique index (`AppDbContext` ~line 84), so nothing at the storage layer stops one account from owning two aperturista records.

Three changes, cheapest first:

1. **`RequestLink`** — before inserting, 400 if the caller already holds a claim:
   - `db.FirstAscensionistLinkRequests.AnyAsync(l => l.UserId == userId && l.Status == ReviewStatus.Pending)` → `ya tienes una solicitud de vínculo pendiente`;
   - `db.FirstAscensionists.AnyAsync(f => f.UserId == userId)` → `ya estás vinculado a un aperturista`.

   Two messages rather than one, because they tell the user different things to do (wait vs. talk to a moderator). Both are lowercase-initial to match the file's existing `{ error }` strings.

2. **`Approve`** — re-check at decision time. The window between request and approval is real: two requests from the same user can both be pending when this rule starts being enforced (existing rows), and a moderator can approve them in either order. Extend the existing conditional update to only link when the requester is still unclaimed, and route the failure into the auto-reject branch that's already there — that branch exists precisely so a request that can never be approved doesn't sit Pending forever, and "the requester got linked to someone else first" is the same situation as "the target got claimed first". Reason string: `Esta cuenta ya está vinculada a otro aperturista.`

3. **A unique filtered index on `FirstAscensionist.UserId`** (`WHERE "UserId" IS NOT NULL`), plus its EF migration. This is the actual enforcement; 1 and 2 exist to turn its violation into a clean Spanish 400 instead of a `DbUpdateException`. `RequestLink` already has a `catch (DbUpdateException)` for exactly this pattern — `Approve`'s link step needs the equivalent.

   **Check the data before writing the migration.** If any account is already linked to two aperturistas, the index creation fails on deploy:

   ```sql
   SELECT "UserId", count(*) FROM core."FirstAscensionists"
   WHERE "UserId" IS NOT NULL GROUP BY "UserId" HAVING count(*) > 1;
   ```

   If that returns rows, resolving them is a data decision for the repo owner, not something the migration should guess at — stop and ask.

No DTO changes anywhere here, so `generated.ts` and the checked-in OpenAPI document are untouched.

### 6. Not doing

- Self-service unlinking, or moving a claim from one aperturista to another. A moderator can already clear `FirstAscensionist.UserId` from `/manage/first-ascensionists`; a user-facing "cambiar vínculo" flow is its own issue.
- Any equivalent cap on area-expert requests — being expert of several sectors is the intended behaviour.

## Files touched

| File | Change |
|---|---|
| `frontend/src/styles.css` | rename `.users-page-header` → `.page-header` (base + `720px` override); delete the five `.expert-banner*` rules |
| `frontend/src/pages/MyAreasPage.tsx` | §1 skeleton, §3 intro, banner removed |
| `frontend/src/pages/ClaimFirstAscensionistPage.tsx` | §1 skeleton, §3 intro, §5 one-claim gate |
| `frontend/src/components/RequestFirstAscensionistLinkModal.tsx` | `excludedFirstAscensionistIds` prop removed (§5) |
| `frontend/src/pages/MyInvitesPage.tsx` | class rename only |
| `frontend/src/pages/UsersPage.tsx` | class rename only |
| `frontend/src/pages/FirstAscensionistsPage.tsx` | class rename only |
| `frontend/src/pages/MyAreasPage.test.tsx` | new |
| `frontend/src/pages/ClaimFirstAscensionistPage.test.tsx` | new |
| `backend/Acordanza.Api/Controllers/FirstAscensionistLinksController.cs` | one-claim checks in `RequestLink` and `Approve` (§5) |
| `backend/Acordanza.Api/Data/AppDbContext.cs` | unique filtered index on `FirstAscensionist.UserId` (§5) |
| `backend/Acordanza.Api/Migrations/*` | that index |
| `backend/Acordanza.Api.Tests/Controllers/FirstAscensionistLinkTests.cs` | new (§5) |

## Testing

CSS isn't covered by the Vitest layer (jsdom has no layout), so the tests here guard the thing a markup reshuffle can actually break: the button still exists, and it still opens the right modal after being moved out of its old section and into the header.

Both new files use `renderWithProviders` + MSW (`server.use`) with `factories.ts` types, per `frontend/CLAUDE.md`:

- the page renders the rows returned by `GET /area-experts/mine` / `GET /first-ascensionist-links/mine`, with the status subtitle;
- the header button is present and clicking it opens the request modal (assert on the modal's own heading/field, and register the modal's picker endpoint — `onUnhandledRequest: 'error'` will otherwise fail the test, which is the intended signal);
- a `pending` row shows `Retirar solicitud` and an `approved`/`rejected` one does not;
- a failing list request renders `.error-text` with the page's fallback message.

`ClaimFirstAscensionistPage.test.tsx` additionally covers §5, which is the part with actual logic in it — the button is **enabled** with no requests and with only `rejected` ones, and **disabled** with a `pending` one and with an `approved` one, each with the matching intro line rendered. That "rejected doesn't count" row is the one worth having: it's the case a naive `mine.length > 0` gets wrong.

Confirm the exact endpoint paths against `api/areaExperts.ts` and `api/firstAscensionistLinks.ts` when writing the handlers rather than trusting the paths above.

**Backend** — `Acordanza.Api.Tests/Controllers/FirstAscensionistLinkTests.cs` (new), `IntegrationTestBase` + `TestUsers`, `[Trait("Category", "Integration")]` like the rest:

- request a link while already holding a pending one for a *different* aperturista → 400 (this fails against `main` today, which is the point);
- request a link while already linked to an aperturista → 400;
- request after a *rejected* one → 200, the rule doesn't stick;
- approving a second pending request for a user who got linked in the meantime → 400, the request ends up `rejected` with the §5 reason, and the second aperturista is still unlinked;
- the existing behaviour doesn't regress: a first claim requests and approves cleanly and sets `FirstAscensionist.UserId`.

No `AuthorizationMatrixTests` row is needed — no new endpoint and no policy change.

Then the eyeball check the tests can't do — the point of the issue — via the `run-acordanza` skill: `/my-acordanza/my-invites`, `/my-acordanza/my-areas`, `/my-acordanza/claim-first-ascensionist` at desktop width and at ≤720px, each with an empty list and with a few rows. The three should be indistinguishable in structure.

`bun run lint` and `bun run build` from `frontend/` before opening the PR; `noUnusedLocals` will catch any import left behind by the deleted banner. `dotnet test Acordanza.slnx` from the repo root for the backend half (Docker must be up for the integration trait).
