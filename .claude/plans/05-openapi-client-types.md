# Generate frontend API types from the backend's OpenAPI document

Status: done
Depends on: nothing; pairs well with frontend-data-fetching.md but independent

## Problem

`frontend/src/api/types.ts` (~300 lines) hand-mirrors the backend `Dtos/` records, and `frontend/src/api/permissions.ts` hand-mirrors `Authorization/Permissions.cs` — both kept in sync manually per frontend/CLAUDE.md. Drift fails silently: a renamed/retyped DTO property compiles fine on both sides and breaks at runtime. Turn contract drift into a build-time error.

## Design

1. **Backend: expose an OpenAPI document.** Check `Program.cs` — as of the last review it does NOT call `AddOpenApi()`. Add .NET 10's built-in support:
   - `builder.Services.AddOpenApi();` + `app.MapOpenApi();` (gate the endpoint to `app.Environment.IsDevelopment()` — the private API doesn't need a public schema).
   - Verify controller actions produce accurate schemas: the codebase returns anonymous objects in places (`Ok(new { id = route.Id })`, `Ok(new { ok = true })`, `NotFound(new { error = ... })`) which will surface as untyped — acceptable for v1; the DTO-returning endpoints are the valuable part. Consider `[ProducesResponseType]` annotations only where the generator gets it wrong; don't blanket-annotate.

2. **Generate at build time, commit the output.** Use `Microsoft.Extensions.ApiDescription.Server` (`<OpenApiDocumentsDirectory>`) so `dotnet build` emits `backend/Acordanza.Api/obj/.../Acordanza.Api.json` → configure it to write to a checked-in path, e.g. `backend/Acordanza.Api/openapi/v1.json`. Committing it means the frontend build never needs a running backend (CI-friendly) and diffs show contract changes in review.

3. **Frontend: generate types.** `bun add -d openapi-typescript`; script in `frontend/package.json`: `"generate-api": "openapi-typescript ../backend/Acordanza.Api/openapi/v1.json -o src/api/generated.ts"`. Do NOT wire it into `predev`/`prebuild` (those already build the design system; regeneration is only needed when the backend changes) — run it manually/when the JSON diff changes, and add a CI check that regenerating produces no diff.

4. **Migrate `types.ts` incrementally.** Re-export generated schema types (`components['schemas']['RouteDto']` etc.) under the existing names from `types.ts` so call sites don't change; delete hand-written interfaces as they're replaced. Keep hand-written: `orientationLabels`/`gearLabels`/`roleLabels` maps (display concerns, not contract), `emptyRouteFilters`, and any narrowed literal-union types the generator renders as plain `string` (backend enums-as-strings like status fields — check what the generator emits; add manual narrowing wrappers where the runtime contract is a known closed set).

5. **Permissions stay manual for now.** Generating `permissions.ts` from `Permissions.cs` needs either a dev-only endpoint returning `Permissions.All` or a source-generation step — disproportionate. Keep the hand-sync note in frontend/CLAUDE.md; revisit if permissions churn.

## Steps

1. Backend: AddOpenApi/MapOpenApi (dev-gated) + build-time document emission to the checked-in path; `dotnet build`; eyeball the JSON for the main DTOs (RouteDto, AreaDetailDto, paged responses).
2. Frontend: add generator + script; generate; wire `types.ts` re-exports for 2–3 core types (RouteSummary/RouteDetail/AreaDto) as the pilot; `bun run build` must pass with zero call-site edits.
3. Migrate the rest of `types.ts` in batches; delete replaced interfaces.
4. CI: add a job step that runs the generator and fails on `git diff --exit-code` (catches "backend changed, types not regenerated").
5. Update `frontend/CLAUDE.md` (the "mirrors the backend DTOs — keep in sync by hand" bullet) and `backend/CLAUDE.md` (new OpenAPI surface + where the JSON lives).

## Risks / gotchas

- ASP.NET serializes camelCase; openapi-typescript emits property names as they appear in the schema — confirm the document reflects camelCase (it follows `JsonSerializerOptions`), else configure naming policy for the document.
- Nullable reference types map to optional/nullable schema properties; expect some `| null` unions appearing where the hand-written types said optional — fix call sites honestly rather than loosening the generated types.
- The anonymous-object endpoints (`{ ok: true }`, `{ id }`) stay hand-typed in `api/*.ts`; that's fine.
