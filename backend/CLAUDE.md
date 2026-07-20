# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Applies to `backend/Acordanza.Api/` — the ASP.NET Core (.NET 10) Web API. See the root `CLAUDE.md` for how this fits with the frontend.

## Commands

All run from `backend/Acordanza.Api/`:

```
dotnet build                                                      # compile
dotnet run                                                        # run the API on http://localhost:5073
dotnet run -- create-user <username> <password>                   # provision an account (only way to create one — no HTTP registration)
dotnet ef migrations add <Name>                                   # add a migration after changing a model/AppDbContext
dotnet ef database update                                         # apply pending migrations
```

First-time setup (once per machine): `dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"`. The connection string in `appsettings.json` assumes the `docker-compose.yml` Postgres defaults (`localhost:5432`, db/user/password `acordanza`).

Migrations run automatically on startup (`db.Database.MigrateAsync()` in `Program.cs`) as well as before the `create-user` CLI path — you don't need to run `dotnet ef database update` before every `dotnet run`, only when you want the schema applied without booting the full app.

No test project exists yet.

## Architecture

Flat, controller-per-resource structure — there is no service/repository layer; controllers talk to `AppDbContext` directly via EF Core:

- `Controllers/` — `AuthController` (login, `/auth/me`), `RoutesController` (CRUD on routes + nested `POST /{id}/ticks`), `TicksController` (tick deletion). All routes except login require `[Authorize]` (JWT bearer).
- `Models/` — `ApplicationUser` (extends `IdentityUser`, no extra fields), `ClimbingRoute`, `Tick`. `Tick` is a logged ascent: belongs to one `ClimbingRoute` and one `ApplicationUser` (the climber).
- `Dtos/` — request/response records per resource (`AuthDtos.cs`, `RouteDtos.cs`, `TickDtos.cs`). Controllers project EF entities into these directly in LINQ `Select` calls rather than mapping in a separate layer.
- `Data/AppDbContext.cs` — `IdentityDbContext<ApplicationUser>` plus `Routes`/`Ticks` `DbSet`s. Indexes on `Grade`, `Crag`, `Tick.RouteId`. `ClimbingRoute.AddedBy` and `Tick.Climber` use `DeleteBehavior.Restrict` (deleting a user does not cascade); `Tick.Route` cascades (deleting a route deletes its ticks).
- `Services/JwtTokenService.cs` — issues the bearer token consumed by the frontend.
- `Migrations/` — EF Core migrations; regenerate with `dotnet ef migrations add` after any model or `OnModelCreating` change, don't hand-edit.

### Auth model

Identity is used in "core" mode (`AddIdentityCore`, no cookie/UI scaffolding) purely for password hashing and user storage. There is no registration endpoint — accounts are provisioned via the `create-user` CLI branch at the top of `Program.cs` (`dotnet run -- create-user <username> <password>`), which runs before the normal web host pipeline is built. Password policy is relaxed (`RequiredLength = 8`, no complexity requirements) since accounts are hand-provisioned for a small trusted group.

Everyone with an account has equal read/write access to all routes and ticks — there is no per-user ownership check on update/delete, only `[Authorize]` (any valid JWT).

### Configuration

`Jwt:Key` is read from user-secrets/environment, never checked into `appsettings.json`. `Cors:AllowedOrigins` (array) must include the frontend's origin — defaults to `http://localhost:5173` in `appsettings.json`.
