# Manage users: show who invited each account, and when it was confirmed

Status: implemented
Issue: [#135](https://github.com/acheve-org/acordanza/issues/135)
Depends on: nothing. `ApplicationUser.InvitedByUserId`/`InvitedAt` already exist (migration `20260806144549_AddUserInvitedBy`), so half the data this issue asks for is already stored — only the confirmation timestamp is missing. The test harness from [07](07-test-harness.md)/[08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is in `main`, so this gets automated tests on both sides.

## Problem

`/manage/users` (`frontend/src/pages/UsersPage.tsx`) renders one `ListRow` per account with three pieces of information:

```tsx
<ListRow
  key={u.id}
  title={u.pending ? <Chip>Pendiente</Chip> : u.name}
  subtitle={u.email}
  trailing={roleLabels[u.role] ?? u.role}
/>
```

Issue #135 asks the row to show six:

| Field | Stored today? | Returned by `GET /users` today? |
|---|---|---|
| Name | yes (`ApplicationUser.Name`) | yes |
| Email | yes | yes |
| Role | yes (Identity role) | yes |
| Pending marker | derived (`PasswordHash == null`) | yes (`Pending`) |
| **Who invited it, and when** | **yes** (`InvitedByUserId`/`InvitedAt`) | **no** |
| **Confirmation date** | **no — nothing records it** | no |

So this is one new column, two new DTO fields that already have a source, and a row layout that can carry six things instead of three.

Two smaller things fall out of it:

- **The current row hides the name for pending accounts.** `title={u.pending ? <Chip>Pendiente</Chip> : u.name}` swaps the name *out* for the chip rather than showing both. That happens to be harmless today (an invited account's `Name` is `""` until `AcceptInvite` sets it), but the issue asks for the chip *and* the other fields, so the row has to stop treating them as alternatives. The chip's wording also changes: "Pendiente" → "Sin confirmar".
- **Not every account was invited.** `dotnet run -- create-user` provisions accounts with no `InvitedByUserId`/`InvitedAt` at all, and every account that predates migration `AddUserInvitedBy` is in the same state. The row must degrade cleanly rather than print "Invitado por undefined".

## Design

### 1. `ApplicationUser.ConfirmedAt`

One new nullable column — the moment the account became usable:

```csharp
public DateTimeOffset? ConfirmedAt { get; set; }
```

Set in exactly two places, both of which are already the only ways an account gets a password:

- `AuthController.AcceptInvite` — inside the existing transaction, next to `user.Name = name;`, so a failure partway through rolls the timestamp back with the password and the name rather than marking an account confirmed that isn't.
- `Program.cs`'s `create-user` CLI branch — set on the `ApplicationUser` being created. A CLI-provisioned account is confirmed the moment it exists (it has a real password and no invite to accept); leaving it null would make every founding account render an empty confirmation cell forever, which reads as missing data rather than as "there was nothing to confirm".

Nullable, not defaulted: pre-existing accepted accounts genuinely have no recoverable confirmation date, and inventing one (backfilling from `InvitedAt`, say) would put a wrong date on screen with no way to tell it apart from a real one. They render without the confirmation segment instead — see §3.

**Not** derived from something already stored: Identity's `AspNetUsers` has no creation or last-modified timestamp, and `SecurityStamp` is an opaque GUID that rotates. There is nothing to derive from, which is why this is a column.

`Pending` stays derived from `PasswordHash == null` — it already is, it's correct, and `ConfirmedAt` must not become a second source of truth for the same state (legacy accounts would be "pending" forever under a `ConfirmedAt == null` test).

### 2. `UserAccountDto` gains three fields

```csharp
public record UserAccountDto(
    string Id, string Name, string Email, string Role, bool Pending,
    string? InvitedByName, DateTimeOffset? InvitedAt, DateTimeOffset? ConfirmedAt);
```

`InvitedByName`, not `InvitedByEmail` and not an inviter id: `backend/CLAUDE.md`'s rule is that `Name` is the only user-facing display value and email is PII. `GET /users` is admin-only (`Permissions.UserManage`) and already returns the listed accounts' own emails because managing accounts requires it — that's not a reason to widen it to a *second* account per row. `Name` is always populated for anyone capable of inviting (inviting requires being logged in, which requires having accepted an invite or been created by the CLI, both of which set a name), so no fallback is needed; the field is nullable only for accounts that were never invited.

`GetUsers`' projection picks it up through the existing `InvitedBy` navigation — EF translates it to a `LEFT JOIN` on the self-referencing FK, which already has an index (`IX_AspNetUsers_InvitedByUserId`):

```csharp
InvitedByName = user.InvitedBy != null ? user.InvitedBy.Name : null,
user.InvitedAt,
user.ConfirmedAt,
```

The correlated-subquery role lookup and the `q`/`role`/`sortBy` handling are untouched. In particular **`q` keeps searching name/email/role only** — not the inviter's name. Searching an inviter is a different feature ("who did Ana invite?"), it's already served by `GET /users/my-invites` for one's own invites, and folding it into the same free-text box makes the results ambiguous.

**Sorting by invite/confirmation date is out of scope.** `SortableFields` stays `name`/`email`/`role`. The issue asks for the fields to be *shown*; adding two more sort buttons is a separate call about what that toolbar is for.

### 3. The row shows six fields on three lines

`ListRow`'s slots are `title` / `subtitle` / `trailing`, all `ReactNode`. That's enough — no design-system change:

```
Ana Ruiz  [Sin confirmar]                                    Moderador
ana@example.com
Invitada por Hugo el 12/07/2026 · Confirmada el 14/07/2026
```

- **`title`** — `u.name` plus, when `u.pending`, `<Chip>Sin confirmar</Chip>`. Both, never one instead of the other. A pending account has no name yet, so it renders as `—` followed by the chip.
- **`subtitle`** — a fragment: the email, then a second `<span className="user-row-meta">` line assembled from whichever segments have data:
  - `Invitado por {invitedByName} el {date}` when both are present; `Invitado el {date}` if somehow only the date is (defensive — pre-`AddUserInvitedBy` rows), and nothing at all when neither is.
  - `Confirmado el {date}` when `confirmedAt` is set. Omitted entirely when null (legacy account) — no "Confirmado el —" and no invented date. Never shown for a pending account, which by definition has none.
  - Segments joined with ` · `, same separator style the app already uses. If no segment has data, the whole line is skipped rather than rendering an empty row.
- **`trailing`** — the role label, unchanged.

Dates format with `new Date(x).toLocaleDateString('es-ES')`, matching `PendingRequestsList` and `SettingsPage`. Dates only, no times: this is an audit-at-a-glance column, not a log.

Spanish gender: the app's existing validation strings agree with their subject (`Validada`/`Validado`). Here the subject is a person of unknown gender, so both segments use the masculine-as-neutral form (`Invitado por` / `Confirmado el`) rather than attempting agreement — consistent with `roleLabels`' `Usuario`/`Administrador`/`Moderador`, which are already invariant.

One new CSS rule in `frontend/src/styles.css`, next to the existing `.users-*` block: `.user-row-meta { display: block; color: var(--text-dim); font-size: 13px; }`. Page-layout detail, so `styles.css` rather than the design system — `ListRow` itself gains nothing.

### 4. `/users/my-invites` is left alone

`MyInviteDto` (`Email`, `Pending`, `HasInvitedOthers`) backs the user's own invites list under "Mi acordanza". It could show `InvitedAt` too, and arguably should — but the issue is about the admin's manage-users row, and the two lists have different audiences and different columns. Explicit non-goal; worth its own issue if wanted.

## Steps

1. **`Models/ApplicationUser.cs`** — add `ConfirmedAt`. Then `dotnet ef migrations add AddUserConfirmedAt` from `backend/Acordanza.Api/` (needs Postgres up; the generated migration should be a single nullable `timestamp with time zone` column on `identity."AspNetUsers"` — no backfill SQL, see §1).
2. **Set it on both provisioning paths** — `AuthController.AcceptInvite` (inside the existing transaction, beside `user.Name = name`) and `Program.cs`'s `create-user` branch (on the `ApplicationUser` initializer).
3. **`Dtos/UserDtos.cs`** — the three new `UserAccountDto` fields (§2).
4. **`Controllers/UsersController.GetUsers`** — add `InvitedByName`/`InvitedAt`/`ConfirmedAt` to the anonymous projection and to the `UserAccountDto` construction. Nothing else in the method changes.
5. **Regenerate the contract.** `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` from `backend/Acordanza.Api/` (Postgres + Azurite up, `Jwt:Key` configured), then `bun run generate-api` in `frontend/`. Commit both diffs — CI's `openapi-contract-check` fails otherwise. `api/types.ts` needs no edit: `UserAccount` is already a re-export of `components['schemas']['UserAccountDto']`, so the new fields arrive for free.
6. **`pages/UsersPage.tsx`** — the three-line row from §3, plus the `.user-row-meta` rule in `src/styles.css`. Extract the meta-line assembly into a small local helper (`inviteMeta(u): string | null`) rather than inlining three ternaries in JSX — it's the part with the real branching, and it's the part the test will assert on.
7. **Backend tests.** New `backend/Acordanza.Api.Tests/Controllers/UsersListTests.cs` (integration, `[Trait("Category", "Integration")]`):
   - an admin listing users sees `invitedByName`/`invitedAt` populated for an account created through `POST /users/invite`, and null/null for one seeded directly by `TestUsers`;
   - the same account has `pending: true` and `confirmedAt: null` before acceptance, and `pending: false` with a `confirmedAt` in the last minute after it;
   - `invitedByName` is the inviter's `Name` and the response body contains the inviter's email **only** if that inviter is itself one of the listed accounts (i.e. the DTO never leaks a second email per row) — the natural place for this is a case in the existing `Controllers/PrivacyInvariantsTests.cs` instead, if it fits that file's shape better.

   Driving acceptance in-process: don't try to intercept the invite email (`ApiFixture` doesn't swap `IEmailSender`, and adding a capture double just for this is more machinery than the test is worth). Instead resolve `UserManager<ApplicationUser>` in a `factory.Services.CreateScope()`, call `GenerateUserTokenAsync(user, InviteTokens.ProviderName, InviteTokens.Purpose)`, base64url-encode it exactly as `UsersController.InviteUser` does, and `POST /api/auth/accept-invite` with it — the same token the email would have carried, minus the delivery.

   `Authorization/AuthorizationMatrixTests.cs` needs no new row: `GET /api/users` is already covered and no endpoint is added.
8. **Frontend test.** New `frontend/src/pages/UsersPage.test.tsx` (there is none today): with an admin auth value and an MSW handler for `GET /users` returning three `makeUserAccount`-built rows — one confirmed-and-invited, one pending, one CLI-style (no inviter, no confirmation) — assert the name, email and role render for each; the "Sin confirmar" chip renders for the pending one and only that one; the "Invitado por … el …" and "Confirmado el …" text renders for the first; and neither string appears for the third. Add `makeUserAccount` to `src/test/factories.ts` (typed off `components['schemas']['UserAccountDto']`, like every other factory) if it isn't there yet.
9. **Docs.** `backend/CLAUDE.md`: mention `ConfirmedAt` alongside the existing `PasswordHash == null` note in the `ApplicationUser` bullet, and that `AcceptInvite`/`create-user` both set it. `frontend/CLAUDE.md`: the one-line `UsersPage` description already exists — extend it to say the row shows inviter and confirmation date. Root `CLAUDE.md` needs no change.
10. **Verify** with the `run-acordanza` skill: as an admin, open `/manage/users` and confirm the founding CLI account shows a confirmation date and no inviter; invite a new account and confirm the row shows "Sin confirmar" plus "Invitado por <admin> el <today>" and no confirmation segment; accept the invite via the link the backend logs to the console (no `Resend:ApiKey` locally), reload, and confirm the chip is gone, the chosen name is shown, and the confirmation date is today.

## Decisions to confirm before starting

- **`create-user` sets `ConfirmedAt`** (§1). The alternative — leaving it null because no invite was confirmed — is more literal but makes every founding admin account render a permanently blank cell. Recommended as written; it's a one-line change either way.
- **No backfill for existing accepted accounts** (§1). They render without the confirmation segment. Backfilling from `InvitedAt` would show a date that is wrong by however long the invitee took to accept, and indistinguishable from a real one.
- **`InvitedByName`, never the inviter's email or id** (§2) — keeps the PII rule intact even on an admin-only endpoint.
- **Search and sort are unchanged** (§2): `q` still covers name/email/role only, `SortableFields` still has three entries.
- **The pending chip is additive, not a substitute for the name** (§3), and its wording becomes "Sin confirmar" per the issue.
- **`GET /users/my-invites` is out of scope** (§4).
