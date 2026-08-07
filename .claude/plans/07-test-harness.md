# Test harness + first pure unit tests

Status: planned (not started)
Depends on: nothing — this is the entry point for [08](08-backend-integration-tests.md) and [09](09-frontend-component-tests.md)

## Problem

There is no test suite on either side (`backend/CLAUDE.md`: "No test project exists yet"; `frontend/CLAUDE.md`: "No test suite exists yet"). The only way to verify a change today is the `run-acordanza` skill: boot Postgres + Azurite + the API + Vite, drive a headless browser, compare screenshots. That is slow, needs the whole stack up, and can only assert what is visible in a rendered page — it cannot say "a `moderator` gets 403 on `DELETE /api/routes/{id}`" or "a no-op route save keeps `ValidatedAt`".

A concrete demonstration that nothing catches regressions here: commit d8c2b8d ("improve reset password flows", #94) deleted `RoleSeeder.EnsureSeededAsync(...)` from the web-app startup block in `Program.cs` along with a genuinely one-time slug backfill. `backend/CLAUDE.md` still documents the seeder as "run on every startup", and it now only runs in the `create-user` CLI branch. This was found in minutes by the spike in plan 08, purely because a fresh database made the missing roles visible. See "Bug found while spiking" below.

This plan builds the harness and lands enough real tests to prove it works, without touching any infrastructure (no Docker, no database, no browser). Phases 08 and 09 then fill it in.

## Design

Three principles, so the suite stays worth having:

1. **Test through the seams that already exist.** The backend deliberately has no service layer (`backend/CLAUDE.md`: "There is no business-logic layer between controllers and `AppDbContext`"), so mocking a `DbContext` to unit-test a controller would assert almost nothing — the logic *is* the LINQ→SQL translation, the indexes, and the cascade rules. That's why the backend's centre of gravity is integration tests (plan 08) and this plan only unit-tests the genuinely pure pieces.
2. **Fast tests stay fast.** Everything in this plan runs with no Docker and no network, in well under a second. Plan 08's Docker-dependent tests get a trait so `dotnet test --filter "Category!=Integration"` stays instant.
3. **Tests that encode a documented manual invariant earn their keep first.** Several cross-file rules in this repo are maintained by hand and flagged in CLAUDE.md as such — those are the highest-value cheap tests.

### Backend: `backend/Acordanza.Api.Tests/`

New xUnit v3 project, added to `Acordanza.slnx` under the existing `/backend/` folder.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>   <!-- xunit.v3 runs as its own executable -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <!-- Required, and not just boilerplate: without it the ASP.NET Core assemblies
       this project pulls in through the API reference resolve as NuGet packages
       rather than from the shared framework, which drags in a vulnerable
       System.Security.Cryptography.Xml 10.0.7 (five high-severity advisories).
       The API project doesn't have this problem because it's Microsoft.NET.Sdk.Web;
       a plain Microsoft.NET.Sdk test project needs to ask for the framework. -->
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Acordanza.Api/Acordanza.Api.csproj" />
  </ItemGroup>
</Project>
```

Versions above were resolved against nuget.org and this exact csproj was built and run against the solution during the plan-08 spike on the installed SDK (10.0.302) — it compiles clean and `dotnet list package --vulnerable --include-transitive` reports nothing. Two things worth knowing before writing the first file:

- xunit.v3 does **not** provide implicit usings — test files need an explicit `using Xunit;`.
- The `FrameworkReference` above is load-bearing. Dropping it reintroduces the vulnerable transitive, which matters here because CI runs a security scan and a hobby project shouldn't be adding five high-severity advisories to acquire a test suite.

Shouldly over FluentAssertions: FluentAssertions moved to a paid licence for commercial use at v8, and `Should()`-style assertions aren't worth a licensing question on a private hobby project. Shouldly is Apache-2.0 and reads similarly (`result.ShouldBe(...)`).

### Backend: what to test in this phase

Only classes with no `DbContext` and no `HttpContext` — all of these are already pure and need no refactor:

| Target | Why it's worth a test |
|---|---|
| `Services/Slugify.ToSlug` | Accent folding ("Peña Rúa" → `pena-rua`), punctuation runs collapsing to a single `-`, trimmed edge dashes, and the `"sector"` fallback for input that slugifies to empty. It names blob paths, so a regression silently changes storage layout. |
| `Models/Validation` | `InvalidateIfChanged(entity, changed: false)` must be a no-op — that *is* the whole point of the helper (plan 03), and it's one `if` away from silently regressing. |
| `Services/AreaHierarchy` | The richest pure logic in the codebase and it takes a plain `IReadOnlyList<Node>` in its constructor, so it tests with zero infrastructure. Cover `DescendantIds` (deep subtree, leaf, unknown id), `TopLevelId`, `Ancestors` (outermost-first ordering), `NamePath` (the `›` separator), `SlugPath`, and `ResolveSlugPath` — including its sibling-scoped matching (two different parents owning the same slug) and its null-on-no-match. |
| `Authorization/RolePermissions.PermissionsFor` | Asserts the documented `user ⊂ moderator ⊂ admin` containment as a property rather than by reading the table, plus unknown-role and multi-role inputs. |
| `Authorization/Permissions.All` | Guard that `All` actually lists every `public const string` on the class — it's hand-maintained, and a permission missing from `All` silently never gets a policy registered in `Program.cs`, which fails *open* on that endpoint's `[Authorize(Policy = ...)]`. Reflection over the constants makes this a three-line test. |

That last one is the archetype for this phase: cheap, and it closes a real fail-open gap.

**Optional, decide when writing it:** a test that parses `frontend/src/api/permissions.ts` and asserts it matches `Permissions.cs` — `frontend/CLAUDE.md` explicitly says "keep the two in sync by hand". It's genuinely valuable but needs a path walk out of the test binary's directory to the repo root, which is a bit grubby. If it feels too fiddly, skip it here and reconsider once plan 09's harness exists (the same check is equally implementable from the frontend side).

### Frontend: Vitest in `frontend/`

Vitest rather than `bun test`: it reuses the existing `vite.config.ts` (the `@vitejs/plugin-react` transform, `import.meta.env`, path resolution) instead of needing a parallel config, and `@testing-library/react` + jsdom is a far better-trodden path there. Vitest transpiles with esbuild/rolldown and does not typecheck at runtime, so the workspace's `typescript@^7` is not a problem for it (the `tsc -b` in `bun run build` still typechecks test files, since they live under `src` — see plan 09, which leans on that).

```
bun add -d vitest@4 jsdom @testing-library/react @testing-library/user-event @testing-library/jest-dom
```

Resolved latest versions at time of writing: `vitest` 4.1.10, `jsdom` 30.0.1, `@testing-library/react` 16.3.2, `@testing-library/user-event` 14.6.3, `@testing-library/jest-dom` 7.0.0. MSW is deliberately *not* added here — it arrives in plan 09 with the tests that need it.

Config goes in the existing `vite.config.ts` (a `test` block) rather than a separate `vitest.config.ts`, so there's one config to keep in step:

```ts
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
```

`globals: true` so `describe`/`it`/`expect` need no import in every file (matching the jest-dom matcher setup) — add `"types": ["vitest/globals"]` to `tsconfig.app.json` so `tsc -b` agrees. Scripts: `"test": "vitest run"` and `"test:watch": "vitest"`.

`design-system/` gets no test setup in this phase — its component tests land in plan 09, where the Testing Library helpers they need are already established.

### Frontend: what to test in this phase

Pure modules only, no rendering:

| Target | Why it's worth a test |
|---|---|
| `api/routes.ts` `listRoutes` | The `if (filters.x) params.set(...)` chain: empty filters produce a bare `/routes` with no `?`, each filter maps to its own param, falsy values are omitted, and `exactArea` only appears when truthy. Note `hasTopo` is a *string* in `RouteFilters`, not a boolean — exactly the kind of thing worth pinning down. |
| `api/errorMessage.ts` | `ApiError` passes its Spanish message through; anything else (a `TypeError` from a network failure) falls back. Two tests, and it's on every error path in the app. |
| `api/queryKeys.ts` | Key shape stability — `queryKeys.routes(filters)` must stay prefix-matchable by the bare `['routes']` that `SearchPage`'s `invalidateRouteQueries` invalidates with. That coupling is invisible at both call sites and breaks silently (a mutation just stops refreshing the list). |
| `api/types.ts` label maps | `orientationLabels` and `gearLabels` must cover exactly the backend's `AllowedOrientations` / `AllowedGear` sets in `RoutesController`. A missing key renders `undefined` in the UI. Mirror the expected key set in the test with a comment pointing at the C# — imperfect, but it turns a silent UI bug into a failing test. |

### CI

`.github/workflows/ci-cd.yml` already anticipates a backend test project:

```yaml
- name: Test
  run: |
    if find .. -iname '*.Tests.csproj' | grep -q .; then
      dotnet test --no-build -c Release
    else
      echo "No test projects yet — skipping."
    fi
```

**This step will not work as written once the project exists** and must be fixed in this phase. The job's working directory is `backend/Acordanza.Api`, and the preceding steps restore and build `Acordanza.Api.csproj` *only*. So `dotnet test --no-build` would resolve the project in the current directory (the API, not a test project) and, either way, the test assembly was never built. Change the backend job to work at the solution level:

```yaml
- name: Restore
  run: dotnet restore Acordanza.slnx
- name: Build
  run: dotnet build Acordanza.slnx --no-restore -c Release
- name: Test
  run: dotnet test Acordanza.slnx --no-build -c Release
```

(run from the repo root — drop the job's `working-directory: backend/Acordanza.Api`, or point the paths at it). Once plan 08 adds Docker-dependent tests, this becomes `--filter "Category!=Integration"` plus a separate integration step; keep that in mind but don't pre-build it here.

Frontend job: add a `bun run test` step after `Lint`, before `Build`.

## Steps

1. Create `backend/Acordanza.Api.Tests/` with the csproj above; register it in `Acordanza.slnx`. Confirm `dotnet test` discovers zero tests and exits 0.
2. Write the five backend unit-test classes from the table. Expect to spend the most time on `AreaHierarchy` — it has the most branches and is the most worth covering.
3. Fix the CI backend job to build and test the solution (see above). Verify the `find`-guard removal doesn't break the no-test-project case — there now always is one, so the conditional can go entirely.
4. Add Vitest to `frontend/` with the config, scripts, `src/test/setup.ts` (importing `@testing-library/jest-dom/vitest`), and `"types": ["vitest/globals"]` in `tsconfig.app.json`. Confirm `bun run build` still typechecks clean.
5. Write the four frontend unit-test modules from the table.
6. Add the `bun run test` step to the frontend CI job.
7. Update the three CLAUDE.md files that currently claim no tests exist: root ("No test suite exists yet in either project"), `backend/CLAUDE.md` ("No test project exists yet."), `frontend/CLAUDE.md` ("No test suite exists yet."). Document how to run each suite, and note that the backend suite gains a Docker requirement in plan 08.

## Bug found while spiking — FIXED

`RoleSeeder.EnsureSeededAsync` was called only from the `create-user` CLI branch of `Program.cs`, not from the web-app startup block, while `backend/CLAUDE.md` documented it as running "on every startup". Commit d8c2b8d removed it alongside a one-time slug backfill that *was* correct to delete — collateral damage rather than an intentional change.

Impact was limited but real: deployments are bootstrapped via `create-user`, so the three roles did get created there and `UsersController.InviteUser` (which calls `AddToRoleAsync`) worked in practice. What was lost were the seeder's two *backfill* behaviours, which never ran on deploy: backfilling the `user` role onto accounts holding none, and retiring the legacy `first-ascensionist` role.

Fixed by restoring the call after `MigrateAsync()` in the web-host startup block. Verified against a fresh database with the plan-08 spike: a test that seeds a user and assigns a role — which previously failed with `Role ADMIN does not exist` — now passes with the fixture doing no seeding of its own. `backend/CLAUDE.md` needed no change; it already described the intended behaviour.
