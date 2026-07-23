# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Applies to `backend/Acordanza.Api/` — the ASP.NET Core (.NET 10) Web API. See the root `CLAUDE.md` for how this fits with the frontend.

## Commands

All run from `backend/Acordanza.Api/`:

```
dotnet build                                                      # compile
dotnet run                                                        # run the API on http://localhost:5073
dotnet run -- create-user <email> <password>                      # provision an account (only way to create one — no HTTP registration)
dotnet ef migrations add <Name>                                   # add a migration after changing a model/AppDbContext
dotnet ef database update                                         # apply pending migrations
```

First-time setup (once per machine): `dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"`. The connection string in `appsettings.json` assumes the `docker-compose.yml` Postgres defaults (`localhost:5432`, db/user/password `acordanza`).

Migrations run automatically on startup (`db.Database.MigrateAsync()` in `Program.cs`) as well as before the `create-user` CLI path — you don't need to run `dotnet ef database update` before every `dotnet run`, only when you want the schema applied without booting the full app.

No test project exists yet.

## Architecture

Flat, controller-per-resource structure — there is no service/repository layer; controllers talk to `AppDbContext` directly via EF Core:

- `Controllers/` — `AuthController` (login incl. passkey second factor, `/auth/me`), `AccountController` (self-service password change), `PasskeysController` (register/list/delete the current user's passkeys), `RoutesController` (CRUD on routes + nested `POST /{id}/ticks`), `TicksController` (tick deletion). All routes except login require `[Authorize]` (JWT bearer).
- `Models/` — `ApplicationUser` (extends `IdentityUser`, no extra fields — email *is* the username), `ClimbingRoute`, `Tick`, `PasskeyCredential` (a registered WebAuthn credential, belongs to one `ApplicationUser`, cascades on user delete). `Tick` is a logged ascent: belongs to one `ClimbingRoute` and one `ApplicationUser` (the climber).
- `Dtos/` — request/response records per resource (`AuthDtos.cs`, `AccountDtos.cs`, `PasskeyDtos.cs`, `RouteDtos.cs`, `TickDtos.cs`). Controllers project EF entities into these directly in LINQ `Select` calls rather than mapping in a separate layer.
- `Data/AppDbContext.cs` — `IdentityDbContext<ApplicationUser>` plus `Routes`/`Ticks`/`PasskeyCredentials` `DbSet`s. Indexes on `Grade`, `Crag`, `Tick.RouteId`; `ApplicationUser.NormalizedEmail` is unique (email is the username). `ClimbingRoute.AddedBy` and `Tick.Climber` use `DeleteBehavior.Restrict` (deleting a user does not cascade); `Tick.Route` and `PasskeyCredential.User` cascade.
- `Services/JwtTokenService.cs` — issues the bearer token consumed by the frontend.
- `Migrations/` — EF Core migrations; regenerate with `dotnet ef migrations add` after any model or `OnModelCreating` change, don't hand-edit (the one documented exception is `AddUniqueEmailAndPasskeys`, which hand-inserts a `migrationBuilder.Sql(...)` data backfill ahead of the generated unique-index creation — see that migration's comments).

### Auth model

Identity is used in "core" mode (`AddIdentityCore`, no cookie/UI scaffolding) purely for password hashing and user storage. Email is the username (`RequireUniqueEmail = true`; `UserName` and `Email` are always set to the same value). There is no registration endpoint — accounts are provisioned via the `create-user` CLI branch at the top of `Program.cs` (`dotnet run -- create-user <email> <password>`), which runs before the normal web host pipeline is built. Password policy is relaxed (`RequiredLength = 8`, no complexity requirements) since accounts are hand-provisioned for a small trusted group.

Passkey (WebAuthn/FIDO2) second-factor login is **opt-in per user**, implemented manually (not via ASP.NET Identity's built-in 2FA, which assumes `SignInManager`/cookies this app doesn't use) with `Fido2.AspNet`: a user with no registered passkey logs in with just a password; a user with one or more registered via `PasskeysController` gets a `mfaRequired` response from `POST /api/auth/login` carrying WebAuthn assertion options and a `sessionId`, and must complete `POST /api/auth/login/passkey` with the browser's assertion before a JWT is issued. WebAuthn challenges are cached in-process (`IMemoryCache`, 5 min TTL) between the options/complete steps — fine for a single-instance deployment, but would need a distributed cache if ever scaled to multiple replicas.

Everyone with an account has equal read/write access to all routes and ticks — there is no per-user ownership check on update/delete, only `[Authorize]` (any valid JWT).

### Configuration

`Jwt:Key` is read from user-secrets/environment, never checked into `appsettings.json`. `Cors:AllowedOrigins` (array) must include the frontend's origin — defaults to `http://localhost:5173` in `appsettings.json`; the same array is reused as the Fido2 `Origins` allowlist. `Fido2:ServerDomain` (the bare domain, no scheme/port — e.g. `localhost` in dev) and `Fido2:ServerName` configure the WebAuthn relying party.
