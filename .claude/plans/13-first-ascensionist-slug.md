# First ascensionists get a slug, and the "Sigue" section moves to `/fa`

Status: not started
Issue: [#137](https://github.com/acheve-org/acordanza/issues/137)
Depends on: nothing. `Area.Slug` (`Services/Slugify.cs`, `AreasController.GenerateUniqueAreaSlugAsync`, migration `20260804110308_AddAreaSiblingSlugUniqueness`) is the working precedent this copies, and the test harness from [07](07-test-harness.md)/[08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is in `main`, so this gets tests on both sides.

## Context

An aperturista's page is reached today at `/first-ascensionists/42` — `App.tsx:161` binds `first-ascensionists/:id` to `FirstAscensionistPage`, which does `Number(useParams().id)` and calls `GET /api/first-ascensionists/42`. The number is opaque: a link pasted into the group chat says nothing about who it points at, and it leaks a database id into a URL people share.

Areas already solved this — `Area.Slug` is an ASCII, path-safe form of the name, and `/areas/pena-rua` resolves through `GET /areas/by-slug-path/{*path}`. Issue #137 asks for the same on `FirstAscensionist`, with the page at `/fa/hugo-biarge`.

Two decisions were taken up front, and they shape the plan:

- **The whole "Sigue" section moves to `/fa`** — directory at `/fa`, one opener at `/fa/:slug`. Not just the detail page, so the section keeps a single prefix.
- **The slug is regenerated when the name changes.** Area freezes its slug at creation because it is also the topo blob-storage prefix (`AreaHierarchy.SlugPath`), so renaming would orphan uploaded images. Nothing is keyed on a first ascensionist's slug except the URL, so a corrected name should not leave a permanently wrong URL behind. Old slugs 404 — acceptable in a private app for a handful of friends.

One structural difference from Area matters throughout: **first-ascensionist names are not unique.** `AppDbContext` gives `Area` a unique `(ParentAreaId, Name)` index but gives `FirstAscensionist` only a plain `HasIndex(o => o.Name)`. So the `-2`/`-3` suffix loop is the *primary* disambiguator here (two real climbers named "José García" get `jose-garcia` and `jose-garcia-2`), not a diacritics edge case.

## Design

### 1. `Slugify.ToSlug` takes a fallback

`Services/Slugify.cs` currently hardcodes an Area-flavoured fallback for input that slugifies to nothing (`"!!!"`, `"   "`):

```csharp
return string.IsNullOrEmpty(slug) ? "sector" : slug;
```

Make it a defaulted parameter — `ToSlug(string input, string fallback = "sector")` — so existing callers (`AreasController`, `RoutesController`'s blob-path helper) and `SlugifyTests` are untouched, and the first-ascensionist path passes `"aperturista"`.

### 2. `FirstAscensionist.Slug`

```csharp
// ASCII, path-safe form of Name — the record's public URL identity (/fa/{slug}).
// Unlike Area.Slug this is regenerated whenever Name changes: nothing else is keyed
// on it (Area's doubles as the topo blob-storage prefix), so a corrected name should
// not leave a stale URL behind.
public string Slug { get; set; } = "";
```

EF config in `Data/AppDbContext.cs`, in the existing `builder.Entity<FirstAscensionist>` block next to `HasIndex(o => o.Name)`:

```csharp
entity.Property(o => o.Slug).HasMaxLength(280).HasDefaultValue("");
entity.HasIndex(o => o.Slug).IsUnique();
```

Globally unique, not scoped like Area's `(ParentAreaId, Slug)` — the entity is flat, and `/fa/{slug}` has to resolve without context.

### 3. Uniqueness helper

Mirror `AreasController.GenerateUniqueAreaSlugAsync` (`Controllers/AreasController.cs:397`), with one addition — an `excludeId`, needed because this slug is regenerated on update and a record must not collide with itself:

```csharp
private async Task<string> GenerateUniqueSlugAsync(string name, int? excludeId = null)
{
    var baseSlug = Slugify.ToSlug(name, "aperturista");
    var slug = baseSlug;
    var suffix = 2;
    while (await db.FirstAscensionists.AnyAsync(o => o.Slug == slug && o.Id != excludeId))
    {
        slug = $"{baseSlug}-{suffix}";
        suffix++;
    }
    return slug;
}
```

The unique index is the race backstop. `CreateFirstAscensionist` and `UpdateFirstAscensionist` should catch `DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }` and return `Conflict(new { error = "ya existe un aperturista con esa dirección" })` — note `AreasController`'s equivalent catch (lines 152-158) reports a *name* collision message for what may be a slug-index violation; don't copy that bug.

### 4. Migration `AddFirstAscensionistSlug` — column, backfill, dedupe, index

Existing rows must all get a slug in the same migration that creates the unique index, so the backfill is raw SQL. `20260804110308_AddAreaSiblingSlugUniqueness` is the precedent: dedupe *then* index, in the same order the runtime loop uses.

1. `AddColumn<string>("Slug", "FirstAscensionists", schema: "core", maxLength: 280, nullable: false, defaultValue: "")`.
2. Backfill — lowercase, strip Spanish/Latin-1 accents with `translate()`, collapse non-alphanumerics, trim `-`:
   ```sql
   UPDATE core."FirstAscensionists"
   SET "Slug" = COALESCE(NULLIF(trim(both '-' from regexp_replace(
       lower(translate("Name",
           'áàäâãéèëêíìïîóòöôõúùüûñçÁÀÄÂÃÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇ',
           'aaaaaeeeeiiiiooooouuuuncAAAAAEEEEIIIIOOOOOUUUUNC')),
       '[^a-z0-9]+', '-', 'g')), ''), 'aperturista');
   ```
3. Dedupe with the same `ROW_NUMBER() OVER (PARTITION BY "Slug" ORDER BY "Id")` CTE the area migration uses, appending `-2`, `-3`… to `rn > 1`.
4. `CreateIndex(..., unique: true)`. `Down` drops the index and the column.

`translate()` covers Latin-1 accents rather than all of Unicode, so a name in another script could backfill to `aperturista`/`aperturista-2` where `ToSlug` would have produced something else. Acceptable: the dataset is a few dozen Spanish names, and renaming the record regenerates the slug properly.

### 5. Controller: read by slug, and drop the read by id

`Controllers/FirstAscensionistsController.cs`:

- `CreateFirstAscensionist` — `Slug = await GenerateUniqueSlugAsync(request.Name.Trim())` on the new entity.
- `UpdateFirstAscensionist` — inside the existing `if (changed)` logic, `firstAscensionist.Slug = await GenerateUniqueSlugAsync(newName, firstAscensionist.Id)`. Unchanged names keep their slug (that's what `excludeId` buys), so a no-op save can't bump `hugo-biarge` to `hugo-biarge-2`.
- Replace `[HttpGet("{id:int}")] GetFirstAscensionist` with:
  ```csharp
  [HttpGet("by-slug/{slug}")]
  public async Task<ActionResult<FirstAscensionistProfileDto>> GetFirstAscensionistBySlug(string slug)
  ```
  Same body, matching on `o.Slug == slug`, 404 `new { error = "el aperturista no existe" }` (the wording `UpdateFirstAscensionist` already uses). `by-slug/` rather than a bare `{slug}` so it can't shadow the literal `profiles`/`pending` segments — same shape as `AreasController`'s `by-slug-path`. Nothing else in the repo calls the by-id action, so it goes rather than lingering as a second way to read the same record.

`OpeningsByFirstAscensionistAsync(id)`, the profile projection, and the validate/reject/delete actions are untouched — they stay keyed on the numeric id, which is correct: those are id-addressed operations, not shareable URLs.

### 6. DTO surface: profile only

Add `string Slug` to `FirstAscensionistProfileDto` (`Dtos/FirstAscensionistDtos.cs`) — it backs both the directory cards and the detail page, which is everywhere a link is built.

**Not** on `FirstAscensionistDto`. Its own comment says it is embedded in every route payload "where the extra aggregate columns would be dead weight", and no route payload links to an aperturista's page today. Adding it there is the follow-up if route detail ever grows those links. Not on `FirstAscensionistPendingDto` either — the review queue doesn't navigate.

Slug is server-derived, never client-supplied: `CreateFirstAscensionistRequest`/`UpdateFirstAscensionistRequest` are unchanged, exactly like `CreateAreaRequest`.

### 7. Frontend: `/fa` and `/fa/:slug`

- **`api/firstAscensionists.ts`** — `getFirstAscensionist(id)` becomes `getFirstAscensionistBySlug(slug)` hitting `/first-ascensionists/by-slug/${slug}` (the *API* path keeps its full name; only the browser URL shortens). No `encodeURIComponent` — slugs are already URL-safe, same as the comment on `api/areas.ts:48`.
- **`App.tsx`** — `<Route path="fa" element={<FirstAscensionistsDirectoryPage />} />` and `<Route path="fa/:slug" element={<FirstAscensionistPage />} />`; the top-nav `NavLink` (line 62) points at `/fa`. Add a legacy redirect beside the existing `/map` → `/explore` one (line 163): `<Route path="first-ascensionists/*" element={<Navigate to="/fa" replace />} />`, with a comment that an old numeric link can only land on the directory since the id can't be mapped to a slug without a fetch.
- **`pages/FirstAscensionistPage.tsx`** — `useParams().slug` instead of `Number(useParams().id)`; the `shownId` stale-response guard becomes `shownSlug`. The routes fetch currently fires in parallel with the profile fetch using the URL's id; now the id only arrives *with* the profile, so `refreshRoutes(fa.id)` moves inside the profile `.then`, still guarded on `shownSlug.current === slug`. Back-button and breadcrumb (lines 108, 121) point at `/fa`.
- **`pages/FirstAscensionistsDirectoryPage.tsx:45`** — `to={`/fa/${fa.slug}`}`.
- **`pages/HomePage.tsx:42`** — the "Sigue" card's `to: '/fa'`.

### 8. Docs

- Root `CLAUDE.md`: the route-slug list (line 43) and the four-section table's "Sigue" row (line 51) both become `/fa`.
- `frontend/CLAUDE.md`: lines 25 and 33 — `/fa`(`/:slug`), and the note that `/first-ascensionists*` redirects to it.
- `backend/CLAUDE.md`: the `FirstAscensionistsController` bullet (`GET /by-slug/{slug}` replaces `GET /{id}`) and the `FirstAscensionist` model bullet (`Slug`, regenerated on rename — explicitly contrasted with `Area.Slug`, which is frozen because it is a blob prefix).

## Steps

1. `Services/Slugify.cs` — the `fallback` parameter (§1), plus a `SlugifyTests` case asserting a custom fallback is honoured.
2. `Models/FirstAscensionist.cs` + `Data/AppDbContext.cs` — the property and the EF config (§2).
3. `dotnet ef migrations add AddFirstAscensionistSlug` from `backend/Acordanza.Api/` (needs Postgres up), then hand-write the backfill/dedupe SQL into it (§4) — the generated migration only adds the column and the index.
4. `Controllers/FirstAscensionistsController.cs` — `GenerateUniqueSlugAsync`, create/update wiring, `by-slug` action replacing the by-id one, unique-violation catch (§3, §5). `Dtos/FirstAscensionistDtos.cs` — `Slug` on the profile DTO (§6).
5. **Regenerate the contract.** `ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true` from `backend/Acordanza.Api/` (Postgres + Azurite up, `Jwt:Key` configured), then `bun run generate-api` in `frontend/`. Commit both diffs — CI's `openapi-contract-check` fails otherwise. `api/types.ts` needs no edit; the profile type re-exports the generated schema.
6. Frontend changes (§7): `api/firstAscensionists.ts`, `App.tsx`, `FirstAscensionistPage.tsx`, `FirstAscensionistsDirectoryPage.tsx`, `HomePage.tsx`.
7. **`backend/Acordanza.Api.Tests/Infrastructure/SeedData.cs`** — `CreateFirstAscensionistAsync` (line 33) must set `Slug = Slugify.ToSlug(name, "aperturista")`, mirroring what it already does for areas (line 18). Without this, the second seeded aperturista in any test violates the new unique index on `""` — this will break existing integration tests before it breaks anything else, so do it in the same commit.
8. **Backend tests** — new `Controllers/FirstAscensionistSlugTests.cs`, integration (`[Trait("Category", "Integration")]`), as a moderator so creates are auto-validated:
   - creating two aperturistas with the same name yields `jose-garcia` and `jose-garcia-2`;
   - `GET /first-ascensionists/by-slug/{slug}` returns the right profile for each, and 404s for an unknown slug;
   - renaming regenerates: the new slug resolves, the old one 404s;
   - saving the same name twice keeps the slug (no `-2` drift);
   - accents/punctuation round-trip (`"Peña Rúa"` → `pena-rua`), covering the `ToSlug` call site rather than `ToSlug` itself.

   `Authorization/AuthorizationMatrixTests.cs`: swap the `GET /api/first-ascensionists/{id}` row for the `by-slug` path if one exists there (it's `[Authorize]`-only, so behaviour is unchanged).
9. **Frontend tests** — add `makeFirstAscensionistProfile` to `src/test/factories.ts` (typed off `components['schemas']['FirstAscensionistProfileDto']`, `slug: 'alex-aperturista'`; there is no profile factory today, only `makeFirstAscensionist`). Then:
   - `pages/FirstAscensionistsDirectoryPage.test.tsx` — with an MSW handler for `GET /first-ascensionists/profiles`, assert each card's `href` is `/fa/{slug}`;
   - `pages/FirstAscensionistPage.test.tsx` — `renderWithProviders(..., { initialEntries: ['/fa/hugo-biarge'] })` with handlers for `by-slug/hugo-biarge` and `GET /routes?firstAscensionistId=`, asserting the name renders and the routes list is fetched with the id from the profile (i.e. the sequencing change in §7 works).
10. Docs (§8).

## Verification

- `dotnet test Acordanza.slnx` from the repo root (Docker up for the integration tier) and `bun run test` in `frontend/`.
- Run the stack with the `run-acordanza` skill and check end to end: `/fa` lists the aperturistas; clicking one lands on `/fa/<name-slug>` with their routes; the breadcrumb and back button return to `/fa`; a hand-typed `/first-ascensionists/3` redirects to `/fa`; an unknown `/fa/nadie` shows the not-found state.
- As a moderator, create two aperturistas with the same name and confirm the second one's URL ends in `-2`; rename one from `/manage/first-ascensionists` and confirm its URL follows the new name while the old one 404s.
- Confirm the migration backfilled real data: `select "Id", "Name", "Slug" from core."FirstAscensionists"` in the local Postgres should show no empty slugs and no duplicates.

## Decisions already taken

- **Both the directory and the detail page live under `/fa`** — not just the detail page. `/first-ascensionists*` redirects there.
- **The slug regenerates on rename** (§2), unlike `Area.Slug`. Old links 404.
- **The API path keeps its full name** (`/api/first-ascensionists/by-slug/{slug}`); only the browser URL shortens to `/fa`.
- **The by-id read endpoint is removed**, not kept alongside — nothing else consumes it.
- **`Slug` goes on the profile DTO only** (§6), not on the `FirstAscensionistDto` embedded in every route payload.
