# The account's email in "Perfil", and changing it through a confirmed link

Status: planned (not started)
Issue: [#147](https://github.com/acheve-org/acordanza/issues/147)
Depends on: nothing. No migration and no new entity — Identity's own `ChangeEmail` token carries the whole pending change, so nothing has to be persisted between request and confirmation. Touches both sides plus one new email template; the test harness from [08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is in `main`, so it gets automated tests on both.

## Problem

`pages/SettingsPage.tsx`'s "Perfil" section has exactly one field, `Nombre`. The account's email — the thing you type at the login screen — is shown **nowhere** in the app for an ordinary user: `/manage/users` lists emails but is admin-only, and the user menu shows `name`. So a user who's forgotten which of their addresses this account uses has no way to find out from inside the app.

Issue #147 asks for two things: the email rendered read-only in "Perfil", and a button that opens *"a new modal with information about the change email flow and if something like Passkeys or some other things in the app are going to change"* — i.e. the change is gated behind an explanation of its consequences, not offered as a bare text field.

Two facts shape everything below.

**The email is the login identity, not a contact field.** Both account-creation sites set `UserName` and `Email` to the same string (`Program.cs`'s `create-user`, `UsersController.InviteUser`), `AuthController.Login` resolves accounts with `FindByEmailAsync`, and `JwtTokenService` puts `UserName` in the `unique_name` claim. Changing it changes what you type to log in.

**Displaying it costs nothing.** `MeResponse.Username` *is* the email (`user.UserName`), and `AuthContext` already holds it as `username`. §1 is a render; the entire backend cost of this issue is the change flow.

What actually changes when the address changes — this is also the modal's content (§4):

| | effect |
|---|---|
| Login | the new address from then on; password and passkey step unchanged |
| Password-reset and invite emails | go to the new address |
| Notification emails (area-expert and FA-link decisions, new-user notices to admins) | go to the new address |
| **Passkeys** | **keep working.** The credential is bound to the WebAuthn user handle, and `PasskeysController.RegisterOptions` sets `Fido2User.Id = Encoding.UTF8.GetBytes(user.Id)` — the account id, not the email. Only the *label* the authenticator or password manager stored (`Fido2User.Name`/`DisplayName`, the email at registration time) keeps showing the old address; nothing can update that remotely, and it's cosmetic |
| Everything else — ticks, routes, areas, topos, invites sent, the first-ascensionist link, area-expert grants | untouched. Every one of them keys on `ApplicationUser.Id`, and none stores the email |
| Sessions already open on other devices | keep working until the JWT expires (7 days) — see §6 |

## Design

### 1. The read-only email in "Perfil" — frontend only

`ProfileSection` renders `username` from `useAuth()` above the `Nombre` field, as a static row (label + value), **not** a disabled `<input>`: a greyed-out input reads as "editable, but not right now" and invites clicking at the exact spot where the answer is "use the button below". A `.field-static` rule goes in `styles.css` next to the other form rules, above the trailing `720px` block (see `frontend/CLAUDE.md` on why that block must stay last).

Do **not** add an `email` field to `MeResponse`. `Username` already is the email at both creation sites and stays so after §3 (which sets both), and a second field carrying the same string is a second thing to keep in sync for no gain. Add a one-line comment at the render site saying so, since `username` reads like a display name.

Below it, a `<Button variant="ghost" small>` labelled `Cambiar correo electrónico`, opening the modal from §4.

### 2. `POST /api/account/email/change-request` (authenticated)

In `AccountController` (already `[Authorize]`-wide), body `ChangeEmailRequest(string NewEmail, string CurrentPassword)`:

1. `CheckPasswordAsync` against the current password. This is a re-authentication step, same as `change-password` demands, and it's what stops a stolen-but-live JWT from silently walking off with the account. (Requiring a passkey assertion too was considered and dropped: it would double the flow's complexity for accounts that have one and do nothing for accounts that don't.)
2. Validate the new address: non-empty and `new EmailAddressAttribute().IsValid(email)` (same check `InviteUser` uses), different from the current one (case-insensitive), and not already taken (`FindByEmailAsync`).
3. `var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail)` — the default `DataProtectorTokenProvider`, so the 1-hour `DataProtectionTokenProviderOptions.TokenLifespan` configured in `Program.cs` applies. The purpose string embeds the new address, so a token minted for `a@x` can never confirm `b@y`, and the security stamp is baked in, so it dies the moment it's used. Base64url-encode it (`WebEncoders.Base64UrlEncode`) exactly as `ForgotPassword`/`InviteUser` do — it's going into a query string.
4. Send **two** emails:
   - to the **new** address, the confirmation link: `{Frontend:BaseUrl}/confirm-email-change?uid={user.Id}&email={newEmail}&token={encoded}`;
   - to the **old** address, a heads-up that a change was requested, naming the new address and telling the reader to change their password if it wasn't them.

   The heads-up goes out at *request* time, not on completion, and that's the point: once the change lands, password-reset mail goes to the new address, so a notice sent afterwards reaches someone who can no longer recover the account on their own. At request time the old owner still controls every recovery path.
5. Return `{ ok = true }`.

On the taken-address check the plan deliberately returns a specific `Ese correo electrónico ya está en uso` rather than a vague message: it's an authenticated, password-confirmed, rate-limited endpoint in an invite-only app, so the "email enumeration oracle" it technically is buys an attacker who is already inside almost nothing, and the vague alternative makes a legitimate typo unfixable.

`uid` in the link, not `email=<current>` as the reset/invite links use: at confirmation time the account still carries the *old* address, so the new one can't find it, and the old one has no business appearing in the new inbox. The id is the caller's own Identity GUID — not a secret, and useless without the stamp-bound token.

Rate limiting: a new `change-email` policy alongside the existing `forgot-password` one in `Program.cs`, partitioned by the authenticated user id (`httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)`, falling back to the remote IP), 3 per hour. `app.UseRateLimiter()` runs after `app.UseAuthentication()`, so the user id is populated by then. Same motivation as `forgot-password`'s: without it this endpoint is an authenticated way to flood an arbitrary inbox and burn the Resend quota.

### 3. `POST /api/auth/confirm-email-change` (anonymous)

In `AuthController`, next to `reset-password`/`accept-invite`, and anonymous for the same reason they are: the link is opened in whatever browser reads the new inbox, which is usually not a logged-in one.

Body `ConfirmEmailChangeRequest(string UserId, string NewEmail, string Token)`:

1. `FindByIdAsync(userId)`; decode the token with the existing `TryDecodeToken` helper. Any failure → `400 { error = "El enlace no es válido o ha caducado" }`, the wording already used by the other two flows.
2. Inside a transaction (`db.Database.BeginTransactionAsync()`, mirroring `AcceptInvite`):
   - `ChangeEmailAsync(user, newEmail, token)` — verifies purpose + security stamp, sets `Email`, sets `EmailConfirmed`, rotates the stamp (so the link is single-use), and re-runs the user validators, which means `RequireUniqueEmail` is enforced *again* here: the window between request and confirmation can't leave two accounts on one address.
   - `SetUserNameAsync(user, newEmail)` — mandatory, not cosmetic. `UserName == Email` is the invariant §1 leans on and the source of `MeResponse.Username` and the JWT's `unique_name`.

   The transaction is what makes those two one operation: `SetUserNameAsync` runs its own validation and can fail on its own, and an account left with a new `Email` and an old `UserName` would be a half-changed identity with no way to retry (the token is spent).
3. Return `{ ok = true }`.

No `validate-token` pre-check for this link, unlike `/reset-password` and `/accept-invite`. That endpoint's whole reason for existing is "don't make someone fill in a form behind a dead link", and this screen has no form — there's nothing to type, so it can just submit on mount and report the outcome (§5). It also wouldn't fit: `validate-token` looks accounts up by email and checks two fixed purposes, while this token is found by id and its purpose embeds the new address.

### 4. `components/ChangeEmailModal.tsx`

Built on the design system's `Modal`/`ModalActions`, following `InviteUserModal`'s shape. One component, two steps in local state — the explanation is the preamble to the form, and stacking a second `Modal` on top of the first buys nothing:

- **`info`** — what the issue asks for: how the flow works (a link goes to the new address; nothing changes until it's opened; the link lasts an hour) and what changes (login address, where account emails go, passkeys keep working but may keep showing the old address in your password manager, nothing else in the app moves). Actions: `Cancelar` / `Continuar`.
- **`form`** — `Correo electrónico nuevo` (`type="email"`, required) + `Contraseña actual` (`type="password"`, required) → `accountApi.requestEmailChange(...)`. `ApiError` messages surface through `.error-text` like every other form here.
- **`sent`** — replaces the form on success: "Te hemos enviado un enlace a `<address>`. Ábrelo para confirmar el cambio; tu correo actual no cambia hasta entonces." Single `Entendido` action.

Copy is drafted in the appendix so implementation isn't inventing wording at 2am.

### 5. `pages/ConfirmEmailChangeScreen.tsx` + the route

A third pathname special-cased in `App.tsx` **before** the auth gate, next to the two `if (location.pathname === ...)` lines already there. Reuses the `.login-screen`/`.login-card`/`<Brand />` frame that `ResetPasswordScreen` and `AcceptInviteScreen` use, with three states: `checking`, `success`, `invalid`.

It POSTs on mount, and that needs an explicit guard: `main.tsx` mounts under `<StrictMode>`, which double-invokes effects in dev, and the second call would fail (the stamp is already rotated) and flip a successful screen to "el enlace no es válido". A `useRef` latch around the single call, and a test that asserts exactly one request (§Testing).

On success, if a session is stored, call `AuthContext.logout()` — it clears `localStorage` and the query cache — and show `Iniciar sesión`. Without it the shell keeps rendering a `username` that no longer exists, and the next `GET /auth/me` on a stale identity is a confusing failure rather than a clean re-login.

### 6. Not doing

- **Invalidating sessions on other devices.** JWT validation doesn't check the security stamp, so an already-issued token stays good until it expires (7 days). That's pre-existing — `change-password` has exactly the same property — and closing it means a stamp claim plus a per-request lookup for *every* endpoint, which is its own change, not a rider on #147. The modal doesn't promise otherwise.
- Prompting to re-register passkeys so their stored label matches. They keep working; the stale label is cosmetic, and a "delete and re-add your passkey" nudge is a good way to talk someone out of their second factor.
- An email-change audit trail, and admin-initiated email changes from `/manage/users`. Neither is in the issue.

### 7. OpenAPI + generated client

Two new endpoints and their DTOs change the contract, so regenerate the checked-in OpenAPI document and `frontend/src/api/generated.ts` (`bun run generate-api`) and commit both — CI's `openapi-contract-check` fails the build otherwise. `api/account.ts` declares its return types by hand today (`Promise<{ ok: true }>`); the two new functions follow that.

## Files touched

| File | Change |
|---|---|
| `backend/Acordanza.Api/Dtos/AccountDtos.cs` | `ChangeEmailRequest` |
| `backend/Acordanza.Api/Dtos/AuthDtos.cs` | `ConfirmEmailChangeRequest` |
| `backend/Acordanza.Api/Controllers/AccountController.cs` | `POST email/change-request` (§2) |
| `backend/Acordanza.Api/Controllers/AuthController.cs` | `POST confirm-email-change` (§3) |
| `backend/Acordanza.Api/Services/EmailTemplates.cs` | `ConfirmEmailChange(link, …)`, `EmailChangeRequested(newEmail, …)` |
| `backend/Acordanza.Api/Program.cs` | `change-email` rate-limit policy |
| `backend/Acordanza.Api/openapi/Acordanza.Api.json` | regenerated |
| `frontend/src/api/account.ts` | `requestEmailChange` |
| `frontend/src/api/auth.ts` | `confirmEmailChange` |
| `frontend/src/api/generated.ts` | regenerated |
| `frontend/src/pages/SettingsPage.tsx` | read-only email + button (§1) |
| `frontend/src/components/ChangeEmailModal.tsx` | new (§4) |
| `frontend/src/pages/ConfirmEmailChangeScreen.tsx` | new (§5) |
| `frontend/src/App.tsx` | `/confirm-email-change` before the auth gate |
| `frontend/src/styles.css` | `.field-static` |

## Testing

**Backend** — `Acordanza.Api.Tests/Controllers/EmailChangeTests.cs`, `IntegrationTestBase` + `TestUsers`. The suite doesn't intercept outgoing mail, so the confirmation step mints the same token the controller would, in-process via `UserManager.GenerateChangeEmailTokenAsync` — the technique `UsersListTests` already uses for invites.

- request with the wrong current password → 400, and `GET /auth/me` still shows the old address;
- request to an address another account already holds → 400;
- happy path: request → confirm → `/auth/me` shows the new address, `POST /auth/login` succeeds with the new one and fails with the old;
- confirming the same link twice → the second is 400 (stamp rotated);
- a token minted for `a@x` used to confirm `b@y` → 400;
- unauthenticated `POST /account/email/change-request` → 401. (No `AuthorizationMatrixTests` row: that matrix covers `[Authorize(Policy = …)]` endpoints, and this one is plain `[Authorize]`, like `change-password`.)

**Frontend** — Vitest + MSW:

- `SettingsPage.test.tsx` (new): the email renders from auth state and is *not* an editable field; the button opens the modal; `Continuar` reveals the form; a successful submit shows the "te hemos enviado un enlace" state; an API error surfaces in the modal.
- `ConfirmEmailChangeScreen.test.tsx` (new): a valid link posts **exactly once** (the StrictMode latch from §5) and renders the success state; a 400 renders the dead-link message.

## Appendix — Spanish copy

Modal title: **Cambiar correo electrónico**

Step `info`:

> Tu correo electrónico es también tu usuario: es lo que escribes para iniciar sesión.
>
> - Te enviaremos un enlace de confirmación al correo nuevo. Nada cambia hasta que lo abras, y el enlace caduca en 1 hora.
> - A partir de entonces iniciarás sesión con el correo nuevo y ahí recibirás los avisos de la aplicación.
> - Tus llaves de acceso seguirán funcionando. Puede que tu gestor de contraseñas siga mostrando el correo antiguo junto a ellas: es solo la etiqueta que guardó al crearlas.
> - El resto de tu cuenta no cambia: tus vías, tus aperturas, tus sectores y tus invitaciones siguen igual.
>
> También avisaremos a tu correo actual de que has pedido el cambio.

Buttons: `Cancelar` · `Continuar` · `Enviar enlace` (`Enviando…` while submitting) · `Entendido`.

Fields: `Correo electrónico nuevo`, `Contraseña actual`.

Sent state: `Te hemos enviado un enlace a {email}. Ábrelo para confirmar el cambio.`

Confirmation screen: `Comprobando enlace…` / `Correo electrónico actualizado. Inicia sesión con tu correo nuevo.` / `El enlace no es válido o ha caducado.`

Emails — to the new address: subject `Confirma tu nuevo correo de Acordanza`, body "Has pedido usar esta dirección para tu cuenta de Acordanza." + button `Confirmar correo electrónico` + "Si tú no has pedido esto, puedes ignorar este correo." To the old address: subject `Se ha solicitado cambiar el correo de tu cuenta`, body "Se ha solicitado cambiar el correo de tu cuenta de Acordanza a **{nuevo}**. El cambio no se aplicará hasta que se confirme desde esa dirección." + "Si no has sido tú, cambia tu contraseña ahora mismo."
