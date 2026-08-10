# New information for a ClimbingRoute: length, obligatory grade, gear notes, links — and no more tags

Status: planned (not started)
Issue: [#151](https://github.com/acheve-org/acordanza/issues/151)
Depends on: nothing. Backend-led: one new entity, four new columns, one dropped column, one migration, a regenerated OpenAPI document and `generated.ts`. The frontend half is deliberately small — the create/edit form gains the new fields, and everything that referenced `Tags` is removed because it no longer typechecks. Route *display* is out of scope by the issue's own instruction ("route information will be revamped in a following issue"). The test harness from [08](08-backend-integration-tests.md)/[09](09-frontend-component-tests.md) is in `main`, so both halves get real tests.

## Problem

`ClimbingRoute` today carries `Name`, `AreaId`, `Grade`, `Orientation`, `Gear`, `Description`, `Tags`, plus its first-ascensionist and topo collections. Issue #151 changes the shape of that record:

| | change |
|---|---|
| `Length` | **new, required.** Positive integer, `> 5` and `< 3000`. Existing rows backfill to `100` |
| `ObligatoryGrade` | new, optional, free-text string |
| `RecommendedGear` | new, optional, free-text string |
| `OptionalGear` | new, optional, free-text string |
| `Tags` | **removed** |
| `Links` | **new related collection.** A `Link` is `Id` + FK + `Url`, and the URL must be valid |

Two of those carry more weight than they look.

**Dropping `Tags` is not a one-column change.** It's a stored column, a DTO field on `RouteDto`/`RouteDetailDto`/`MyTickDto`/`UpsertRouteRequest`, a query parameter on *two* endpoints (`GET /routes?tag=`, `GET /ticks/mine?tag=`), a `Validation.InvalidateIfChanged` term, a filter input in `components/Filters.tsx` **and** in `pages/MyTicksPage.tsx`, a chip row in `components/DetailModal.tsx`, a form field in `pages/RouteFormPage.tsx`, two `RouteFilters`/`TickFilters` client types, a `SeedData` parameter, and assertions in three test files. The issue's "no more frontend work must be done" caps what we *add* to the UI; it can't cap the removal, because `tsc -b` fails otherwise. §5 is the full sweep.

**`RecommendedGear`/`OptionalGear` collide, by name, with the `Gear` column that already exists** — and that one is a closed enum (`bolted`/`removable`/`pitons`/`aiders`, labelled *Equipamiento*, backing a dropdown filter and `gearLabels`). The two new fields are free text about what to carry. Nothing in the issue says to replace `Gear`, and replacing it would take out a working filter, so this plan keeps all three and separates them in the UI copy (§3). If the intent was actually to *replace* `Gear`, say so before implementation — it's a materially different change (filter removal, `gearLabels` deletion, a data migration for four enum values) and this plan doesn't cover it.

## Design

### 1. `Length` — the required one

`ClimbingRoute.Length`, `int`, non-nullable. Metres; the issue doesn't say, but a climbing route's length has no other plausible unit and the range (6–2999) only makes sense in metres.

**Range: `> 5` and `< 3000`, exclusive on both ends**, exactly as written — so the accepted integers are 6 to 2999. Rejected with `la longitud debe ser mayor que 5 y menor que 3000` from both `CreateRoute` and `UpdateRoute`, next to the existing orientation/gear checks.

Non-nullable `int` in `UpsertRouteRequest` is doing deliberate work: a request that omits `length` binds to `0`, which fails the range check with the same message. There is no way to write a route without a length, which is what "required" has to mean.

**The migration backfills `100` and then drops the default.** Two statements, hand-edited into the generated migration:

```csharp
migrationBuilder.AddColumn<int>(
    name: "Length", schema: "core", table: "Routes",
    type: "integer", nullable: false, defaultValue: 100);
migrationBuilder.Sql(@"ALTER TABLE core.""Routes"" ALTER COLUMN ""Length"" DROP DEFAULT;");
```

The repo's precedent for this (`ApplicationUser.Name`, `Area.Slug`) keeps a `HasDefaultValue` on the model, but those keep it because `Program.cs` backfills them separately at startup and the default is only there to make `AddColumn` apply. Here the default *is* the backfill, and leaving it in place would turn every future "forgot to set `Length`" bug into a silent `100` instead of a loud one — EF's store default fires whenever the property holds the CLR default. So the model gets no `HasDefaultValue`, and the column carries no default past this migration.

### 2. `ObligatoryGrade`, `RecommendedGear`, `OptionalGear`

Three optional free-text `string?` columns on `ClimbingRoute` and on both route DTOs, nullable in `UpsertRouteRequest`.

`string?`, not `string = ""` like `Description`/`Tags`: the app already has both conventions, and for a field with no display yet, one representation of "unspecified" is better than two. Write-side normalization makes that stick — `string.IsNullOrWhiteSpace(x) ? null : x.Trim()`, applied in a small local helper used by `CreateRoute` and `UpdateRoute` alike, so a form that posts `""` and a form that posts `null` land identically. Without it the change-detection in §4 flip-flops a route between validated and pending on every save.

Column lengths: `ObligatoryGrade` `HasMaxLength(20)` (it's a grade — `Grade` itself is unbounded today, but there's no reason to open a new unbounded column), the two gear fields `HasMaxLength(500)` (a sentence about what to bring, not a description). Over-length input is a 400 with `el texto es demasiado largo` rather than an EF truncation error — check it alongside the range check.

No validation of *content*: `ObligatoryGrade` is free text for the same reason `Grade` is (the app supports any grading system — see `GET /routes/grades`, which offers the grades actually in use rather than a hardcoded scale).

### 3. Frontend form fields (`pages/RouteFormPage.tsx`)

The issue asks for these in the create route form and nowhere else. `RouteFormState` gains `length: number`, `obligatoryGrade`, `recommendedGear`, `optionalGear`, `links: string[]`, and loses `tags`; `emptyForm` gains `length: 0` (which the required-and-min-6 input won't let through) so the "add" form starts blank rather than pre-filled with a number the user didn't choose.

Layout, inserted into the existing `settings-section` form:

```
[ Nombre            ] [ Grado             ]   ← existing .grid-2
[ Sector                                   ]   ← existing AreaPicker
[ Longitud (m)      ] [ Grado obligatorio  ]   ← new .grid-2
[ Orientación       ] [ Equipamiento       ]   ← existing .grid-2
[ Material recomendado                     ]   ← new
[ Material opcional                        ]   ← new
[ Descripción                              ]   ← existing textarea
[ Enlaces                                  ]   ← new (§4)
[ Apertura                                 ]   ← existing picker
```

`Longitud` sits next to `Grado obligatorio` because both are short; the two gear-note fields are full-width single-line inputs. Labels are deliberately *Material recomendado* / *Material opcional* rather than anything with "equipamiento" in it, so they don't read as variants of the `Equipamiento` dropdown two rows up (see the Problem section's naming note).

`Longitud` is `type="number"` with `required min={6} max={2999} step={1}` and a `<span className="hint">(metros)</span>` on the label, mirroring how `Etiquetas` labelled its own hint. The browser check is a convenience only — §1's server check is the rule.

On submit, the three text fields are trimmed and sent as-is (`''` is fine; §2 normalizes it server-side), and `length` is sent as `Number(form.length)`.

`gear`/`orientation`/`description` handling is untouched.

### 4. `Link` — the new collection

```csharp
// backend/Acordanza.Api/Models/Link.cs
public class Link
{
    public int Id { get; set; }
    public int RouteId { get; set; }
    public ClimbingRoute Route { get; set; } = null!;
    public required string Url { get; set; }
}
```

**On the FK name.** The issue writes `ClimbingRouteId`; this plan uses `RouteId`, because `Topo.RouteId` and `Tick.RouteId` — the two existing children of `ClimbingRoute` — both do, the DbSet is `db.Routes`, the table is `core."Routes"`, and every existing query reads `t.RouteId`. A third spelling for the same relationship is a paper cut on every future join. If the literal name is wanted, it's a one-word change in three places; nothing else in this plan depends on it.

`AppDbContext` mapping, the `Topo` block's shape minus the uploader:

```csharp
builder.Entity<Link>(entity =>
{
    entity.ToTable("Links", "core");
    entity.HasIndex(l => l.RouteId);
    entity.Property(l => l.Url).HasMaxLength(2048);

    entity.HasOne(l => l.Route)
        .WithMany(r => r.Links)
        .HasForeignKey(l => l.RouteId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Plus `public DbSet<Link> Links => Set<Link>();` and `public List<Link> Links { get; set; } = [];` on `ClimbingRoute`.

**No unique index on `(RouteId, Url)`.** §4's write path clears and re-adds the collection, and EF gives no ordering guarantee between the DELETEs and the INSERTs inside one `SaveChangesAsync` — a unique index would turn "saved the same route twice" into a constraint violation. Deduplication happens in the controller instead, where it can produce a Spanish message.

#### Write path: replace-the-whole-set on `UpsertRouteRequest`

`UpsertRouteRequest` gains `List<string>? Links`. No dedicated `POST /routes/{id}/links` endpoint: topos need one because they're multipart uploads with blob side effects, and links are three fields in a form the user is already submitting. Same shape as `FirstAscensionistIds` — the client sends the full intended set, the server makes the row set match.

A shared `ResolveLinksAsync`-style helper (a pure `(List<string>?) -> (List<string>? Urls, string? Error)`, mirroring `ResolveFirstAscensionistsAsync`'s tuple return) does, in order:

1. trim each entry, drop the blank ones;
2. **cap at 10** → `no puedes añadir más de 10 enlaces`. Unbounded child collections on an unbounded request body are how a 4 MB POST turns into 40 000 rows;
3. reject anything over 2048 characters → `algún enlace es demasiado largo`;
4. validate each: `Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)` → `algún enlace no es válido: debe empezar por http:// o https://`;
5. dedupe, case-sensitively on the trimmed string (`.Distinct()`), preserving order.

**The scheme allowlist is the load-bearing part of step 4, not `TryCreate`.** `Uri.TryCreate(…, Absolute, …)` happily accepts `javascript:alert(1)` and `data:text/html,…`. These strings are stored now and rendered as `<a href>` by the revamp in the following issue, at which point a stored `javascript:` URL is stored XSS. Allowlisting the two schemes here means that revamp can render them without re-deriving this. Do not relax it to "any absolute URI".

`CreateRoute` sets `Links = urls.Select(u => new Link { Url = u }).ToList()`.

`UpdateRoute` must add `.Include(r => r.Links)` next to the existing `.Include(r => r.FirstAscensionists)` — without it the `Clear()` below is a no-op on an unloaded collection — then `route.Links.Clear()` followed by `AddRange(...)`, which EF turns into DELETEs of the orphans (required FK + cascade) and INSERTs of the new set. Ids churn on every edit; nothing exposes them, so nothing notices (see the read path below).

#### Read path

`RouteDto` and `RouteDetailDto` each gain `List<string> Links`, and the list query gains `.Include(r => r.Links)` (it is already `AsSplitQuery`, so this costs one more round trip, not a fan-out).

`List<string>`, not `List<LinkDto>`: nothing addresses a single link — there's no per-link endpoint and the write path replaces the whole set — so an `Id` in the response would be a handle the API doesn't honour. `Link.Id` exists because EF needs a key. If per-link endpoints ever arrive, that's the moment to widen the DTO.

Both DTOs carry it (rather than detail-only) because they're otherwise field-for-field parallel and `RouteCard`'s eventual "has links" affordance would need it anyway.

#### Change detection

Every new field joins `UpdateRoute`'s `changed` expression, so editing any of them clears a prior validation decision exactly like editing the description does today:

```csharp
route.Length != request.Length ||
(route.ObligatoryGrade ?? "") != (normalizedObligatoryGrade ?? "") ||
(route.RecommendedGear ?? "") != (normalizedRecommendedGear ?? "") ||
(route.OptionalGear ?? "") != (normalizedOptionalGear ?? "") ||
!route.Links.Select(l => l.Url).ToHashSet().SetEquals(newUrls)
```

Note both halves: the three strings compare **normalized** values (§2) on both sides, or `""` vs `null` re-invalidates a route on a no-op save; and links compare as a **set**, matching the order-insensitive `SetEquals` already used for `FirstAscensionists` — reordering the same links is not an edit. The `route.Tags != (request.Tags ?? "")` term goes.

### 5. Removing `Tags` — the full sweep

Backend:

- `ClimbingRoute.Tags` — gone; `DropColumn` in the same migration as §1/§4.
- `RouteDto.Tags`, `RouteDetailDto.Tags`, `UpsertRouteRequest.Tags`, `MyTickDto.Tags` — gone, along with their projection sites in `RoutesController` (×2) and `TicksController`.
- `GET /routes?tag=` — the `tag` parameter and its `r.Tags.Contains(tag)` clause go.
- `GET /ticks/mine?tag=` — same.
- `SeedData.CreateRouteAsync`'s `string tags = ""` parameter and its two callers' arguments.

Frontend:

- `api/types.ts` — `tag` off `RouteFilters`, `emptyRouteFilters` and `TickFilters`. `queryKeys.routes(filters)` needs no edit; it keys on whatever shape `RouteFilters` is.
- `api/routes.ts` / `api/ticks.ts` — the `params.set('tag', …)` lines.
- `components/Filters.tsx` — the `Etiqueta` label + input.
- `pages/MyTicksPage.tsx` — the `Etiqueta` label + input, and `tag` out of `emptyFilters`.
- `components/DetailModal.tsx` — the `const tags = route.tags.split(',')…` derivation, the `<div className="tag-row">` chip row, **and the now-unused `Chip` import** (`noUnusedLocals` will fail the build otherwise — `Chip` has no other use in this file).
- `pages/RouteFormPage.tsx` — the `Etiquetas` field, and `tags` off `RouteFormState`/`emptyForm`/the submit payload.
- `test/factories.ts` — `tags` off `makeRoute`/`makeRouteDetail` (they'd stop typechecking against the regenerated `generated.ts` — which is exactly the mechanism `frontend/CLAUDE.md` describes).

**`.tag-row` in `styles.css` stays.** Despite the name it's a generic chip row, still used by `FirstAscensionistPicker`, `FirstAscensionistPage` and `FirstAscensionistsDirectoryPage`. Renaming it is out of scope here.

Docs that name the tag filter and must be corrected in the same PR: root `CLAUDE.md` (the four-sections table's "Busca" row), `frontend/CLAUDE.md` (the `Filters` bullet, and `MyTicksPage`'s "q/areaId/gear/tag shape"), `backend/CLAUDE.md` (`RoutesController`'s filter list), and root `README.md` lines 7–8.

### 6. OpenAPI + generated client

Four DTOs change shape, so regenerate the checked-in document and `generated.ts` and commit both, or CI's `openapi-contract-check` fails the build:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true
```

then `bun run generate-api` from `frontend/`. Both need Postgres + Azurite up and `Jwt:Key` configured.

### 7. Not doing

- **Rendering any of the new fields on the route detail, card, or area pages.** The issue rules this out explicitly; the revamp issue owns it. `DetailModal` only *loses* markup here.
- **A `length` / `hasLinks` search filter.** Adding filters is a search-UX decision, not a data-model one, and the issue asks for neither.
- **Replacing the existing `Gear` enum column.** See the Problem section — if that's wanted, it's a separate change.
- **Per-link endpoints, link titles, or link-type classification** (topo PDF vs. video vs. blog post). `Url` is the whole model the issue describes.
- **Backfilling `ObligatoryGrade`/`RecommendedGear`/`OptionalGear` from the free-text `Description` or the dropped `Tags`.** Existing routes get nulls; only `Length` has a stated backfill.

## Files touched

| File | Change |
|---|---|
| `backend/Acordanza.Api/Models/ClimbingRoute.cs` | `Length`, `ObligatoryGrade`, `RecommendedGear`, `OptionalGear`, `Links`; `Tags` removed |
| `backend/Acordanza.Api/Models/Link.cs` | new (§4) |
| `backend/Acordanza.Api/Data/AppDbContext.cs` | `DbSet<Link>`, the `Link` entity block, `Length`/max-length config |
| `backend/Acordanza.Api/Migrations/*` | one migration: add four columns (`Length` backfilled to 100 then default dropped), create `core."Links"`, drop `Tags` |
| `backend/Acordanza.Api/Dtos/RouteDtos.cs` | new fields on both DTOs + `UpsertRouteRequest`; `Tags` removed |
| `backend/Acordanza.Api/Dtos/TickDtos.cs` | `MyTickDto.Tags` removed |
| `backend/Acordanza.Api/Controllers/RoutesController.cs` | length/text/link validation, link resolve + write, `Include(r => r.Links)` ×2, projections, `changed` terms; `tag` filter removed |
| `backend/Acordanza.Api/Controllers/TicksController.cs` | `tag` filter + `Tags` projection removed |
| `backend/Acordanza.Api/openapi/Acordanza.Api.json` | regenerated (§6) |
| `frontend/src/api/generated.ts` | regenerated (§6) |
| `frontend/src/api/types.ts` | `tag` off `RouteFilters`/`emptyRouteFilters`/`TickFilters` |
| `frontend/src/api/routes.ts`, `frontend/src/api/ticks.ts` | `tag` query param removed |
| `frontend/src/pages/RouteFormPage.tsx` | five new fields (§3, §4); `Etiquetas` removed |
| `frontend/src/components/Filters.tsx` | `Etiqueta` filter removed |
| `frontend/src/pages/MyTicksPage.tsx` | `Etiqueta` filter removed |
| `frontend/src/components/DetailModal.tsx` | tag chips + `Chip` import removed |
| `frontend/src/test/factories.ts` | `tags` removed, new fields added |
| `frontend/src/api/routes.test.ts` | `tag` assertions removed |
| `frontend/src/pages/RouteFormPage.test.tsx` | new |
| `backend/Acordanza.Api.Tests/Infrastructure/SeedData.cs` | `tags` param removed, `length` param added |
| `backend/Acordanza.Api.Tests/Controllers/RouteFiltersTests.cs` | tag-filter tests removed, `tags:` args dropped |
| `backend/Acordanza.Api.Tests/Controllers/RouteValidationLifecycleTests.cs` | `Tags` test replaced by one per new field (§Testing) |
| `backend/Acordanza.Api.Tests/Controllers/RouteFieldsTests.cs` | new (§Testing) |
| `CLAUDE.md`, `backend/CLAUDE.md`, `frontend/CLAUDE.md`, `README.md` | tag-filter references removed (§5) |

## Testing

**Backend** — `Acordanza.Api.Tests/Controllers/RouteFieldsTests.cs` (new), `IntegrationTestBase` + `TestUsers`, `[Trait("Category", "Integration")]` like the rest. Everything here goes through real HTTP, so the DTO round trip is under test too, not just the entity:

- `POST /routes` with `length` of `5`, `3000`, `0` and a negative → 400 each (the boundaries are the point: 5 and 3000 are *out*, 6 and 2999 are in — assert those two succeed);
- `POST /routes` omitting `length` from the JSON body entirely → 400, not a silently-stored `0` (this is what makes `int` rather than `int?` the right choice);
- a route created without the three optional strings comes back with them `null`, and one created with `"  "` in each also comes back `null` (§2's normalization);
- over-length `obligatoryGrade`/`recommendedGear` → 400, not a 500 from Postgres;
- links: a valid `https://` set round-trips in order; `javascript:alert(1)`, `data:text/html,x`, `ftp://x/y` and `not a url` each → 400 (**the scheme allowlist has its own test — this is the one that stops a stored-XSS regression when the revamp starts rendering these**); 11 links → 400; duplicates collapse; blanks are dropped;
- `PUT /routes/{id}` with a shorter link set actually deletes the orphan rows — assert via a follow-up `GET`, and assert `db.Links` has no rows left for that route (the `Include` in `UpdateRoute` is easy to forget and the symptom is silent);
- deleting a route deletes its links (cascade);
- the migration's backfill: not directly testable through HTTP (Respawn starts every test from empty), so verify it by hand against a dev database with pre-existing routes — `SELECT "Length" FROM core."Routes"` should be all `100` after `dotnet ef database update`. Note this in the PR description.

`Acordanza.Api.Tests/Controllers/RouteValidationLifecycleTests.cs` — `Changing_the_tags_clears_validation` is replaced by four: `Changing_the_length_…`, `Changing_the_obligatory_grade_…`, `Changing_the_recommended_gear_…`, `Changing_the_links_…`. Plus two the existing suite's shape invites and the new fields make necessary:

- a no-op PUT that sends `null` for a string field the route stores as `null` **keeps** validation (the normalization trap in §4 — this fails against a naive `route.X != request.X`);
- a PUT that sends the same links **in a different order** keeps validation (the `SetEquals` rule, exactly parallel to the existing first-ascensionist reorder case).

`RouteFiltersTests` loses `Tag_filters_by_substring_match` and the `tag=classic` half of `Filters_combine_with_AND_semantics` (keep the test, swap the combination for `grade` + `areaId`, which it already exercises above).

No `AuthorizationMatrixTests` row: no new endpoint, no policy change.

**Frontend** — `pages/RouteFormPage.test.tsx` (new; this page has no test today, and it's the only page this issue touches with logic in it). `renderWithProviders` + MSW per `frontend/CLAUDE.md`, remembering that `onUnhandledRequest: 'error'` means the `AreaPicker`/`FirstAscensionistPicker` endpoints must be registered even when the test doesn't touch them:

- the create form renders the five new fields and **no** `Etiquetas` field;
- filling name/grade/area/length and submitting POSTs a body carrying `length` as a number and the three strings — assert on the intercepted request body, which is the only place the wire shape is checked client-side;
- adding two links then removing one posts exactly the remaining URL;
- the edit form seeds every new field from `GET /routes/{id}` (a `makeRouteDetail` with all of them populated).

`api/routes.test.ts` drops its two `tag` lines.

Then the eyeball check via the `run-acordanza` skill: `/routes/new` and `/routes/:id/edit` at desktop width and at ≤720px (the new `.grid-2` row collapses under the existing `720px` rule — no new CSS, so confirm rather than build), submitting one route with everything filled and one with only the required fields.

`bun run lint` and `bun run build` from `frontend/` before opening the PR — the build is what catches the leftover `Chip` import and any factory still declaring `tags`. `dotnet test Acordanza.slnx` from the repo root (Docker up for the integration trait).

## Appendix — Spanish copy

Form labels: `Longitud` + `<span class="hint">(metros)</span>`, `Grado obligatorio`, `Material recomendado`, `Material opcional`, `Enlaces`.

Placeholders: `Grado obligatorio` → `V+, 6a, E2 5b…` (same as `Grado`); `Material recomendado` → `12 cintas, cuerda de 60 m…`; `Material opcional` → `friends medianos, empotradores…`; a link row → `https://…`.

Links editor: `+ Añadir enlace` (ghost `Button`), each row a `type="url"` input with a `RemoveButton` beside it, laid out with the existing `.picker-stack` the way `FirstAscensionistPicker` does — no new CSS.

Backend `{ error }` strings, lowercase-initial to match the file's existing ones:

- `la longitud debe ser mayor que 5 y menor que 3000`
- `el texto es demasiado largo`
- `no puedes añadir más de 10 enlaces`
- `algún enlace es demasiado largo`
- `algún enlace no es válido: debe empezar por http:// o https://`
