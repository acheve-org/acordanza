# Backend integration tests (real Postgres + Azurite, real HTTP)

Status: slices 1-4 landed (steps 1-6, 8 of "Steps" below); slices 5-8 remain as follow-ups (step 7)
Depends on: [07](07-test-harness.md) (the test project and CI wiring)

## Problem

The backend is where this app's correctness actually lives: role-based authorization on every write endpoint, two dynamic permission grants layered on top, per-user privacy on ticks, the validate/invalidate lifecycle, subtree expansion over the area tree, and concurrency-safe approve/reject. None of it is verified by anything today, and none of it is reachable from a screenshot.

It also can't be meaningfully unit-tested. `backend/CLAUDE.md` is explicit that there is no business-logic layer — controllers talk to `AppDbContext` directly. Substituting an in-memory or mocked context would delete the parts that carry the behaviour: LINQ→SQL translation, the unique filtered indexes on the pending-request pairs, `DeleteBehavior.Restrict` vs cascade, `ExecuteUpdateAsync`'s atomicity, and the three-schema layout. Testing against real Postgres is not gold-plating here — it's the only way to test the code as written, and it's why this plan is the largest of the three.

## What the spike established

A throwaway xunit.v3 project referencing `Acordanza.Api.csproj` was built and run against Testcontainers-managed Postgres and Azurite. Findings, all verified rather than assumed:

- **`WebApplicationFactory<Program>` works with no production change.** The top-level `Program.cs` (which returns `int` and has the `create-user` branch before `builder.Build()`) resolves fine on .NET 10 — no `public partial class Program` declaration and no `InternalsVisibleTo` needed. This was the main risk and it isn't one.
- **Environment variables are the config seam, not `ConfigureAppConfiguration`.** `Program.cs` reads `ConnectionStrings:Default`, `Jwt:Key`, and `Storage:*` off `builder.Configuration` *before* `Build()`, which is earlier than `WebApplicationFactory`'s configuration hooks can reach. Setting `ConnectionStrings__Default`, `Storage__ConnectionString`, `Jwt__Key`, etc. as process environment variables before constructing the factory works, and needs nothing from `Program.cs`.
- **Startup runs end-to-end against the containers.** `db.Database.MigrateAsync()` applies the full migration chain to an empty database (so the suite tests the migrations too, for free), and `BlobStorageService.EnsureContainerExistsAsync()` provisions against Azurite.
- **Azurite needs `--skipApiVersionCheck`**, exactly as `docker-compose.yml` already documents for local dev — the `Azure.Storage.Blobs` SDK sends a newer `x-ms-version` than the image supports and Azurite rejects every request otherwise. Pass it via `.WithCommand("--skipApiVersionCheck")`. Pin Postgres to `postgres:18` to match compose and production.
- **Roles are seeded by app startup — but only since the fix.** The spike initially failed with `Role ADMIN does not exist` on a fresh database, because `RoleSeeder.EnsureSeededAsync` ran only in the `create-user` CLI branch (see plan 07's bug note). That has since been fixed in `Program.cs`, and re-running the spike confirmed the fixture no longer needs to seed anything itself. Respawn still has to preserve or re-create the roles between tests — see "Isolation" below.

The spike ended green — 3 passed, 0 failed, ~500ms of test time after container startup — covering: an unauthenticated `GET /api/routes` returns 401; the `AppDbContext` is queryable through the factory's service provider; and a user seeded via `UserManager` (after `RoleSeeder` runs) can `POST /api/auth/login` and use the returned JWT on a protected endpoint. That last one validates the whole `TestUsers` design below, which is what the rest of this plan rests on.

The test project also needs `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — see plan 07's csproj for why.

## Design

### Fixture

```
backend/Acordanza.Api.Tests/
  Infrastructure/
    ApiFixture.cs        # containers + env vars + WebApplicationFactory + Respawn
    TestUsers.cs         # seed a user in a role, hand back an authenticated HttpClient
    SeedData.cs          # area trees, routes, first ascensionists
```

`ApiFixture` starts both containers, sets the environment variables, builds the factory once, seeds roles, and creates a Respawn checkpoint. It is an **assembly fixture** (xunit.v3 `[assembly: AssemblyFixture(typeof(ApiFixture))]`), not a per-class one: container startup is the expensive part, and — more importantly — the env-var seam is process-global, so two differently-configured hosts must never exist at once.

```csharp
private readonly PostgreSqlContainer _postgres =
    new PostgreSqlBuilder().WithImage("postgres:18").Build();

private readonly AzuriteContainer _azurite =
    new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite")
        .WithCommand("--skipApiVersionCheck")   // see docker-compose.yml for why
        .Build();
```

Packages: `Testcontainers.PostgreSql` 4.13.0, `Testcontainers.Azurite` 4.13.0, `Respawn` 7.0.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10. (Both Testcontainers builders' parameterless constructors are obsolete in 4.13 — pass the image explicitly as above.)

### Isolation between tests

Respawn resets the database between tests, configured for Postgres with the three schemas the app uses:

```csharp
new RespawnerOptions {
    DbAdapter = DbAdapter.Postgres,
    SchemasToInclude = ["identity", "auth", "core"],
    TablesToIgnore = [new Table("identity", "AspNetRoles")],  // keep seeded roles
}
```

Keeping `AspNetRoles` out of the reset avoids re-seeding on every test — app startup seeds them once, and nothing after that needs to recreate them. If the ignore-list turns out to be fiddly (it also has to keep `AspNetUserRoles` consistent), resetting everything and re-running `RoleSeeder.EnsureSeededAsync` in the reset hook is the simpler fallback, and it's idempotent by design. Decide when writing it.

Tests do **not** run in parallel within the assembly (one shared database). Set `[assembly: CollectionBehavior(DisableTestParallelization = true)]`. The suite is a few seconds of container startup plus fast tests; parallelism isn't worth per-test databases here.

### Authentication in tests

`TestUsers.CreateClientAsync(role)` seeds an `ApplicationUser`, assigns the role, and returns an `HttpClient` carrying a real JWT obtained through `POST /api/auth/login`. Going through the real endpoint rather than minting a token directly is deliberate: it exercises `JwtTokenService` and `DynamicPermissions` together, so the claim set under test is the one production issues. Overloads for the two dynamic grants — a user linked to a `FirstAscensionist`, and an approved `AreaExpert` of a given area — since those are precisely the paths worth testing.

### Test slices, in priority order

1. **The authorization matrix.** Every write endpoint × every role → expected 200/403, as a data-driven `[Theory]`. This is the single highest-value block in the plan: it is the app's core security property, it is currently entirely unverified, and it's the thing most likely to be broken silently by adding an endpoint and forgetting the `[Authorize(Policy = ...)]`. Pair it with the `Permissions.All` reflection test from plan 07, which catches the same class of bug from the other end.
2. **`GET /api/routes` filters.** Each of `q`, `areaId`, `grade`, `orientation`, `gear`, `tag`, `hasTopo`, `firstAscensionistId` in isolation and a couple in combination — plus the `exactArea` distinction (subtree expansion via `AreaHierarchy.DescendantIds` vs the area's own routes only), which is a documented behavioural fork with two callers relying on opposite sides of it.
3. **Privacy invariants.** Ticks are private per user: `hasTick` reflects only the caller; `GET /routes/{id}` exposes no other user's notes or ratings; `DELETE /ticks/{id}` on someone else's tick silently no-ops rather than deleting. Add a blunt assertion that no response body reachable by a normal user contains an email address — `ApplicationUser.Email` is PII that must never surface, and a serializer or DTO change could leak it from several places at once.
4. **Validation lifecycle.** A `PUT` that changes nothing keeps `ValidatedAt`/`ValidatedByUserId`; a change to each individual field clears them (including the order-insensitive first-ascensionist id set, which is the subtle one). This directly guards plan 03's rule.
5. **Area-expert scoping.** An approved expert of a top-level area can validate a route in a nested subarea, cannot validate one in a different tree, and a plain `route.validate` claim holder can validate anywhere. Covers `AreaExpertAuthorizer`'s two branches.
6. **Concurrency in `ReviewRequestService`.** Fire two approvals of the same pending request in parallel and assert exactly one `Applied` and one `AlreadyResolved`. The service's doc comment claims this property explicitly; a real database is the only place it can be demonstrated.
7. **Area tree constraints.** The case-exact unique `(ParentAreaId, Name)` and `(ParentAreaId, Slug)` indexes including the nulls-treated-as-equal root case (two root areas with the same name must collide — that's a non-obvious `OnModelCreating` detail); slug-path resolution through `ResolveSlugPath`; and the `Restrict` vs cascade delete behaviours.
8. **Auth flows.** `ForgotPassword` returns `{ ok: true }` for an unknown email (no account enumeration); reset tokens are single-use (the security stamp rotates); `ValidateToken` doesn't consume the token; invite acceptance sets a usable password on a `PasswordHash == null` account.

Slices 1-4 are the ones to land first; 5-8 can follow incrementally. Topo/blob upload tests are worth having eventually (Azurite is already running) but rank below all of the above.

### Keeping the fast tests fast

Mark these `[Trait("Category", "Integration")]` so plan 07's pure unit tests remain runnable without Docker:

- local default: `dotnet test --filter "Category!=Integration"` (milliseconds, no Docker)
- full run: `dotnet test`

In CI, the backend job runs both — `ubuntu-latest` has Docker, and the `openapi-contract-check` job already depends on it, so this adds no new runner requirement. Add the integration run as its own step after the unit step, so a failure reads unambiguously in the log.

## Steps

1. Add the four packages to `Acordanza.Api.Tests.csproj`. Port `ApiFixture` from the spike (it already boots successfully); add the assembly-fixture and no-parallelization attributes.
2. Confirm roles exist after boot without the fixture seeding them (already true since the `Program.cs` fix — this step is just a guard against it regressing again).
3. Wire up Respawn; prove isolation with two tests that would collide on a unique index if the reset didn't work.
4. Write `TestUsers` and `SeedData`, including the first-ascensionist-linked and area-expert variants.
5. Land slices 1-4, in that order. Slice 1 is the one to get right — shape it so adding an endpoint means adding a row to the theory data.
6. Add the CI steps (unit filtered, then integration) to the backend job.
7. Land slices 5-8 as follow-ups; they don't need to be one PR.
8. Update `backend/CLAUDE.md`: the test project, the Docker requirement, the two-tier run commands, and the env-var config seam (a non-obvious constraint the next person will otherwise rediscover the hard way).

## Notes

- The suite tests the migration chain as a side effect, since every run migrates an empty database. That's real coverage of something otherwise only exercised in production.
- The fixture's env vars leak into the test process, not the developer's shell — but a `dotnet test` run and a `dotnet run` in the same shell session are unaffected by each other either way, since they're separate processes.
- Don't add an in-memory-database mode "for speed". It doesn't translate the same SQL, doesn't enforce the filtered unique indexes, and doesn't run `ExecuteUpdateAsync` atomically — every one of which is load-bearing in the slices above.
