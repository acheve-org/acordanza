# Only sector and name are required, and a route can be graded `6c/A1`

Status: done
Depends on / amends: [17](17-grade-scale-and-pills.md), whose French-scale-only rule this narrows rather than reverts, and [16](16-route-new-fields.md), whose fields this makes optional. Touches the route-import pipeline from [30](30-route-from-topo.md)/[31](31-route-from-url.md) (`PrintedGrade`, both `Accept` predicates, `RouteImportService`). **Needs a migration** (two columns to nullable, one new column, one data backfill) and therefore an OpenAPI regeneration plus a `generated.ts` regeneration in the same PR — `openapi-contract-check` fails otherwise.

## Problem

Creating a route requires four fields, not two:

| Field | Enforced by |
|---|---|
| `Name` | `RoutesController.CreateRoute` (`RoutesController.cs:267`), `required` on the input (`RouteFormPage.tsx:175`) |
| `AreaId` | `db.Areas.AnyAsync` (`RoutesController.cs:272`), `disabled={!form.areaId}` on submit (`RouteFormPage.tsx:283`) |
| `Grade` | `GradeValidator.IsValid` on both `CreateRoute` and `UpdateRoute` (`RoutesController.cs:277`, `:355`), `gradeBand(...) === null` client-side (`RouteFormPage.tsx:138`) |
| `Length` | `ValidateLength` — `> 5 and < 3000` (`RoutesController.cs:629`), `required min={6}` (`RouteFormPage.tsx:213`) |

Everything else — orientation, gear, description, obligatory grade, recommended/optional gear, links, first ascensionists — is already optional. So "only sector and name" is, on the required-ness axis, a narrow change: `Grade` and `Length` stop being mandatory.

The grade *grammar* is the substantive half, and it is a deliberate decision being amended. Plan 17 (issue #152) made `^[1-9][a-c]\+?$` the only legal grade on purpose, replacing free text, and four things now sit on that grammar: `GradePill`'s band colours, `RouteCard`'s accent border, `GET /routes/grades` (the filter dropdown, which only works because the stored values are canonical), and the worker's `PrintedGrade.Parse`. That grammar cannot express what a real route is often graded at:

- **A route that isn't free.** `6c/A1` is one route with two grades on two different scales — the hardest free move and the artificial difficulty. `PrintedGrade.cs` names this case explicitly as an intended failure ("Not `'/'`: «6a/6b» and «6c/A1» stay one token, which this does not recognise, which makes them a discard"), so a topo printing `6c/A1` currently produces *nothing at all* from an import, and a human typing it into the grade box gets a 400.
- **A route nobody has graded.** A new opening, a line read off a sector photo, a route someone is logging from memory at the crag. Today it cannot be recorded at all until a grade is invented for it.

The two problems have to be solved together, because "make grade optional" alone would leave `6c/A1` with nowhere to go, and "add an aid grade" alone would leave the ungraded route unrecordable.

`Length` has a third, quieter problem of its own: the column is a non-nullable `int` and the import worker already writes `0` to mean "the source didn't say" (`RouteImportService.AcceptableLength`). `0` is *below* `ValidateLength`'s floor, so a reviewer opening a worker-created route in the edit form cannot save it without inventing a length. `backend/CLAUDE.md` documents that as intended. It stops being intended the moment length is optional.

## Design

### 1. The required set becomes `{ AreaId, Name }`

`RoutesController.CreateRoute`'s opening check loses its grade half:

```csharp
if (string.IsNullOrWhiteSpace(request.Name))
{
    return BadRequest(new { error = "el nombre es obligatorio" });
}
```

and the area check's message stops being about existence only, since `areaId: 0` is now the ordinary "user didn't pick one" case rather than a malformed request:

```csharp
if (request.AreaId == 0) return BadRequest(new { error = "el sector es obligatorio" });
if (!await db.Areas.AnyAsync(a => a.Id == request.AreaId))
{
    return BadRequest(new { error = "el sector no existe" });
}
```

**`UpdateRoute` needs a name check it does not have today.** Plan 17 §2 noted that `UpdateRoute` had no name/grade-present check and that `GradeValidator.IsValid` covered required-ness "for free" — but only for the grade. `request.Name` is never checked on `PUT`: `route.Name = request.Name` (`RoutesController.cs:406`) and `GenerateUniqueRouteSlugAsync(request.AreaId, request.Name)` both take it as given, so a `PUT` with `""` already blanks a route's name and hands it an empty slug. That is a pre-existing hole, but this plan is the change that removes the accidental guard nearby, so it closes it in the same PR:

```csharp
if (string.IsNullOrWhiteSpace(request.Name))
{
    return BadRequest(new { error = "el nombre es obligatorio" });
}
```

### 2. `ClimbingRoute`: `Grade` nullable, `Length` nullable, `AidGrade` new

```csharp
public required string Name { get; set; }
public int AreaId { get; set; }

// The free-climbing grade, French scale (GradeValidator) — null when nobody has graded
// the route, or when it isn't free-climbed at all and only AidGrade applies.
public string? Grade { get; set; }

// The artificial (aid) grade, A0–A5 / C0–C5 (AidGradeValidator). Independent of Grade,
// not a replacement for it: «6c/A1» is a route with both, «A2» is a route with only
// this one, and most routes in this log have neither.
public string? AidGrade { get; set; }

// Null when unknown. Was a non-nullable int whose 0 the import worker used as an
// "unknown" sentinel — see the migration in §3, which turns those 0s into nulls.
public int? Length { get; set; }
```

`Grade` and `AidGrade` are two columns rather than one combined string, and that is the load-bearing choice in this plan. One column holding `"6c/A1"` would mean: `gradeBand` has to parse a substring out at render time in the design system; `GET /routes/grades` returns `6c` and `6c/A1` as unrelated dropdown entries that don't match each other; and the exact-match filter `r.Grade == grade` silently stops finding aid routes at their free grade. Two columns keep every one of those working unchanged, and make "show me the aid routes" a question the app can answer (§5).

### 3. Migration `OptionalRouteGradeAndLength`

Scaffolded, then **hand-edited** — it joins `AddUniqueEmailAndPasskeys`, `ValidationRejectionAndContributors` and `BackfillValidationStatusApproved` on `backend/CLAUDE.md`'s list of migrations with a hand-inserted `migrationBuilder.Sql(...)`, and that list gets this one appended (§12).

The scaffolded half is three operations on `core."Routes"`: `AlterColumn<string>("Grade", nullable: true, oldNullable: false)`, `AlterColumn<int?>("Length", nullable: true, oldNullable: false)`, `AddColumn<string>("AidGrade", nullable: true)`. All widening, so every existing row survives untouched and there is no down-migration data loss to reason about beyond the usual.

The hand-inserted half runs **after** those, at the end of `Up`:

```csharp
// Length was a non-nullable int, and the import worker wrote 0 to mean "the source
// didn't state one" (RouteImportService.AcceptableLength, plan 30). Now that the column
// can say "unknown" properly, the sentinel has to go — otherwise 0 and NULL both mean
// unknown forever and every reader has to know about both. 0 is not a real length: the
// only writer that could produce it is that sentinel, since ValidateLength's floor has
// always been > 5.
migrationBuilder.Sql("""UPDATE core."Routes" SET "Length" = NULL WHERE "Length" = 0;""");
```

No `CHECK` constraint requiring at least one of `Grade`/`AidGrade` — a route with neither is exactly the state this plan exists to allow. The existing index on `Grade` stays; a partial-null column indexes fine in Postgres and the filter still wants it.

### 4. `AidGradeValidator`

`backend/Acordanza.Core/Models/AidGradeValidator.cs`, a sibling of `GradeValidator.cs` rather than two methods on it — the two grammars are independent, and `GradeValidator`'s doc comment is specifically about the French free scale:

```csharp
using System.Text.RegularExpressions;

namespace Acordanza.Core.Models;

// The artificial (aid) grade scale: A0–A5 for aid that places gear which can damage the
// rock, C0–C5 for clean aid, each optionally '+'. Independent of GradeValidator's French
// free scale — a route can carry one, both («6c/A1») or neither. Mirrored client-side by
// design-system/src/components/GradePill/aidGrade.ts — keep the two in step by hand.
public static partial class AidGradeValidator
{
    [GeneratedRegex(@"^[AC][0-5]\+?$")]
    private static partial Regex Pattern { get; }

    /// Trim + UPPERCASE — note the asymmetry with GradeValidator.Normalize, which
    /// lowercases. Both are canonicalising to how climbers actually write the scale: «6a»
    /// is lowercase, «A1» is not. Same reason either way: this is an exact-match filter
    /// key and a Distinct() list, so «a1» and «A1» must not be two grades.
    public static string Normalize(string? grade) => (grade ?? "").Trim().ToUpperInvariant();

    public static bool IsValid(string? grade) => Pattern.IsMatch(Normalize(grade));
}
```

The uppercase/lowercase asymmetry is called out in the doc comment because it is precisely the kind of thing a later reader "fixes" into consistency and thereby breaks both filters.

`[0-5]` with no escape hatch, matching plan 17's stance: A5 is the top of the New Wave scale, A6 appears in a handful of big-wall sources worldwide and in none of this log's data. If it ever does, widening a character class is a one-line change with no migration.

### 5. Validating and filtering, backend

**Write side.** Both `CreateRoute` and `UpdateRoute` validate each grade only when it is present, and store the normalized form or `null`:

```csharp
// Absent is legal now (plan 34); present-but-wrong still isn't.
if (!string.IsNullOrWhiteSpace(request.Grade) && !GradeValidator.IsValid(request.Grade))
{
    return BadRequest(new { error = "el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)" });
}
if (!string.IsNullOrWhiteSpace(request.AidGrade) && !AidGradeValidator.IsValid(request.AidGrade))
{
    return BadRequest(new { error = "el grado artificial debe ser A0–A5 o C0–C5 (por ejemplo A1, C2, A3+)" });
}
```

with `NormalizeOptionalGrade`/`NormalizeOptionalAidGrade` helpers alongside the existing `NormalizeOptionalText`, so blank-vs-null collapses in exactly one place and `UpdateRoute`'s change-detection compares normalized-to-normalized as it already does for `Grade`.

`ValidateLength` becomes nullable-aware, and its Spanish message keeps naming only the real bound:

```csharp
private static string? ValidateLength(int? length) =>
    length is null or (> 5 and < 3000) ? null : "la longitud debe ser mayor que 5 y menor que 3000";
```

**Read side.** `GET /routes` gains `[FromQuery] string? aidGrade`, matched exactly like `grade`, and both handle "no grade" through one sentinel:

```csharp
if (!string.IsNullOrWhiteSpace(grade))
{
    // "none" is the "Sin grado" dropdown option (§9). Safe as a sentinel rather than a
    // separate query param because no legal French grade is spellable as "none" — and a
    // separate boolean param would be a second control that can contradict this one
    // (grade=6a AND ungraded=true has no meaning).
    query = grade == "none"
        ? query.Where(r => r.Grade == null)
        : query.Where(r => r.Grade == grade);
}
if (!string.IsNullOrWhiteSpace(aidGrade))
{
    query = query.Where(r => r.AidGrade == aidGrade);
}
```

The aid dropdown gets no "sin grado artificial" option: nearly every route in the log has no aid grade, so that filter selects almost everything and answers nothing.

`GET /routes/grades` must now exclude nulls, or the dropdown grows a blank entry:

```csharp
.Where(ValidationVisibility.Routes(userId))
.Where(r => r.Grade != null)
.Select(r => r.Grade!)
```

and `GET /routes/aid-grades` is its exact mirror over `AidGrade` — same visibility filter, same `Distinct().OrderBy(g => g)`. Lexicographic order is correct for this scale too (one letter, one digit, an optional `+` that sorts before nothing at that position matters only within a grade, which can't collide).

### 6. DTOs and the generated contract

Five records change; every one of them is a widening of an existing field plus, in three cases, one addition:

| Record | Change |
|---|---|
| `RouteDto` | `Grade` → `string?`, `Length` → `int?`, `+ string? AidGrade` |
| `RouteDetailDto` | same three |
| `UpsertRouteRequest` | `Grade` → `string?`, `Length` → `int?`, `+ string? AidGrade` |
| `RoutePendingDto` | `Grade` → `string?` |
| `MyTickDto` | `Grade` → `string?`, `Length` → `int?`, `+ string? AidGrade` |

`MyTickDto` gains `AidGrade` even though nothing filters ticks on it: `TickedRouteCard` renders a `GradePill`, and without it a ticked aid route would read `Sin grado` in the history while reading `A2` everywhere else in the app.

Then, in the same PR: regenerate `backend/Acordanza.Api/openapi/Acordanza.Api.json` (`ASPNETCORE_ENVIRONMENT=Development dotnet build -p:OpenApiGenerateDocumentsOnBuild=true`, Postgres + Azurite up) and `frontend/src/api/generated.ts` (`bun run generate-api`), and commit both. `api/types.ts` needs no edit for these — `RouteSummary`/`RouteDetail`/`UpsertRouteInput`/`TickedRoute` are all straight re-exports of `components['schemas']`, so the nullability arrives on its own and TypeScript then points at every call site that has to change. That is the intended mechanism, not a chore: `factories.ts` is typed off the same schemas, so `bun run build` fails at the mocks too.

### 7. `GradePill` and `gradeBand` (design-system)

`gradeBand`'s signature widens to accept the absent case, which is otherwise repeated at four call sites:

```ts
export function gradeBand(grade: string | null | undefined): GradeBand | null {
  const g = (grade ?? '').trim().toLowerCase()
  ...
}
```

A new `aidGrade.ts` next to it mirrors `AidGradeValidator` the way `gradeBand.ts` mirrors `GradeValidator` — `isAidGrade(value: string): boolean`, exported from `design-system/src/index.ts`, used by the form's client-side check (§10). It returns a boolean, not a band: aid grades get no colour (§7's band rule below).

`GradePill` composes up to four facts into one pill:

```tsx
export interface GradePillProps {
  grade: string | null
  aidGrade?: string | null
  obligatoryGrade?: string | null
}
```

| `grade` | `aidGrade` | `obligatoryGrade` | renders |
|---|---|---|---|
| `6c` | — | — | `6c` |
| `6c` | `A1` | — | `6c/A1` |
| — | `A2` | — | `A2` |
| `6c` | `A1` | `6a+` | `6c/A1 (6a+)` |
| — | — | — | `Sin grado` |

**The band comes from the free grade only**, so an aid-only route keeps the default teal — the four bands mean French difficulty and nothing else, and inventing a fifth hue for aid would be a second amendment to DESIGN.md's "keep the accent palette narrow" rule for a case that carries no ordering the reader already knows.

**The gradeless case renders a muted `Sin grado` pill**, not nothing. A card whose grade slot is simply absent reads as a rendering gap; "nobody has graded this yet" is now a legitimate, actionable state, and the person who should act on it — a reviewer in the pending queue, anyone with `route.edit` — is exactly the person looking at these cards. It needs one new class (`grade-pill-empty`: `{colors.muted}` foreground, no fill, no border tint) and one new row in DESIGN.md's band table (§12).

### 8. Display call sites

- **`RouteCard`** — `gradeBand(route.grade)` needs no change beyond the widened signature; the pill gains `aidGrade={route.aidGrade}`.
- **`TickedRouteCard`** — same two changes against `tick.grade`/`tick.aidGrade`.
- **`RouteMeta`** — `length: number | null`; the existing `{length ? ... : null}` guard already renders nothing for both `0` and `null`, so only the prop type moves.
- **`RouteDetailModal`** — the head's `{route.length > 0 && ...}` becomes `{route.length != null && ...}`, and its `GradePill` gains `aidGrade`. No new stat block: the aid grade belongs *in* the pill, beside the grade it modifies, not in a separate row where the reader has to reassemble `6c` and `A1` themselves.
- **`PendingTasksPage`** — `subtitle={`${r.areaPath} · ${r.grade}`}` (`PendingTasksPage.tsx:142`) becomes `subtitle={[r.areaPath, r.grade].filter(Boolean).join(' · ')}`, so a gradeless pending route doesn't render a trailing `· undefined` in the one queue where it is most likely to appear.

### 9. `Filters`

`RouteFilters` gains `aidGrade: string` (and `emptyRouteFilters` its `''`) — the single construction site, so every page picks it up. `listRoutes` gains `if (filters.aidGrade) params.set('aidGrade', filters.aidGrade)`. `queryKeys.routes(filters)` needs no edit; it keys on the whole object.

`Filters.tsx` gains one option and one control:

```tsx
<label className="field-label">Grado</label>
<select value={filters.grade} onChange={(e) => set('grade', e.target.value)}>
  <option value="">Cualquiera</option>
  <option value="none">Sin grado</option>
  {grades.map(...)}
</select>

<label className="field-label">Grado artificial</label>
<select value={filters.aidGrade} onChange={(e) => set('aidGrade', e.target.value)}>
  <option value="">Cualquiera</option>
  {aidGrades.map(...)}
</select>
```

`aidGrades` comes from a second `routesApi.listAidGrades()` call in the same `useEffect`. The aid `<select>` renders **only when `aidGrades.length > 0`** — a log with no aid routes yet shouldn't grow a permanently empty filter, and the endpoint answering `[]` is exactly the signal for that. The stale comment above the grade state ("Grades are free text on the backend") gets rewritten; it has been wrong since plan 17.

### 10. `RouteFormPage`: two fields, then "Más detalles"

The structural change, and the one that makes the optionality visible rather than merely permitted:

```
┌─ Nueva vía ──────────────────────────────────┐
│  ¿Tienes una foto o un enlace? Crea la vía…  │
│                                              │
│  Sector    [ AreaPicker            ]         │
│  Nombre    [                       ]         │
│                                              │
│  ▸ Más detalles                              │
│                                              │
│              [ Cancelar ]  [ Guardar vía ]   │
└──────────────────────────────────────────────┘
```

Everything else — grade, aid grade, obligatory grade, length, orientation, gear, recommended/optional gear, description, first ascensionists, links — moves inside the disclosure, keeping its current grouping and `grid-2`/`grid-3` layout.

**A native `<details>`/`<summary>`, not a new design-system component.** There is one use site; `<details>` gives keyboard operation, the disclosure ARIA role and the open/close state for free, and what needs writing is the CSS that makes the marker match the app — which is form/app-shell styling, exactly what `styles.css` covers. Promoting a `Disclosure` component is the right call the moment a second page wants one, and not before.

**It opens by default when editing a route that has any of those fields set.** Otherwise an editor opens `/routes/:id/edit`, sees two fields, and concludes the data is gone:

```tsx
const [detailsOpen, setDetailsOpen] = useState(false);
// ...set alongside setForm in the edit-load effect:
setDetailsOpen(Boolean(r.grade || r.aidGrade || r.length || r.orientation || r.gear ||
  r.description || r.obligatoryGrade || r.recommendedGear || r.optionalGear ||
  r.links.length || r.firstAscensionists.length));
```

**Form state holds `length` as a string.** It is `number` today, initialised to `0`, with `required min={6}` — which cannot represent "empty" at all, since `Number('')` is `0` and `0` is a legal number. So `RouteFormState.length: string`, the input drops `required`, and submit sends `length: form.length === '' ? null : Number(form.length)`. Same shape for `grade`/`aidGrade`: the form holds `''`, submit sends `null`. `RouteFormState`'s `Omit<UpsertRouteInput, ...>` list grows `grade | aidGrade | length` for this reason, and its header comment — which already explains exactly this "the form never holds null" convention — extends to cover them.

Client-side validation becomes present-only, mirroring §5:

```tsx
if (form.grade.trim() && gradeBand(form.grade) === null) { setError('el grado debe…'); return; }
if (form.aidGrade.trim() && !isAidGrade(form.aidGrade)) { setError('el grado artificial debe…'); return; }
```

Two smaller fixes while restructuring, both pre-existing: the links block currently renders *after* `ModalActions` (`RouteFormPage.tsx:279-307`), so the submit buttons sit in the middle of the form — inside the disclosure it goes above the actions with everything else; and the aid-grade input gets the same `placeholder="A1, C2, A3+…"` treatment the other free-text fields have.

### 11. Route import: `PrintedGrade` learns the split, and an unreadable grade stops being fatal

Two changes, and they are separable in review but not in behaviour.

**`ParsedGrade` gains an aid grade**, and `/` becomes a separator — but only in the one shape that states two different things:

```csharp
public record ParsedGrade(string? Grade, string? AidGrade, string? ObligatoryGrade);
```

`Parse` splits on `/` when it produces exactly two tokens, one resolving to a French grade and the other to an aid grade, in either order. `6c/A1` → `(6c, A1, null)`. `A2` alone → `(null, A2, null)`. **`6a/6b` stays a discard** — two French grades is a range, which still states no single grade, exactly as `PrintedGrade`'s current comment argues. `C1` and `A3+` parse. Everything the strict outside-the-parentheses reader already recognises (alpine difficulty, seriousness ratings, obligatory markers) keeps working unchanged, so `MD+ 6c/A1 (6a+ obl)` resolves to all three facts.

`Parse` returning null keeps meaning "this string states something I cannot account for" — `V+`, `5.11a`, `E2 5b`, `6a/6b`. What changes is **what the readers do with that**:

```csharp
// Was: return (null, RouteImportOutcomes.UnreadableGrade) — the whole import discarded.
// Now: a grade we can't read is a gap, not a failure. A human may save a route with no
// grade (plan 34 §1), so a photo of one must not be thrown away either; the raw string
// goes in the outcome so the reviewer fixes it in one edit instead of re-importing.
var parsed = PrintedGrade.Parse(route.Grade);
var gradeNote = parsed is null && !string.IsNullOrWhiteSpace(route.Grade)
    ? RouteImportOutcomes.GradeNotUnderstood(route.Grade)
    : null;
```

`RouteCandidate` gains `AidGrade` and `GradeNote`; both readers pass `parsed?.Grade`, `parsed?.AidGrade`, `parsed?.ObligatoryGrade` and the note through. `RouteImportOutcomes.UnreadableGrade`/`UnreadableGradeOnPage` are replaced by one formatter beside the consts, next to the existing interpolating `GradeDisagreement`:

```csharp
// The only outcome that interpolates raw model output. Truncated because the model was
// told to copy what's printed and a page can print anything — same defensive clamp
// GearText.Truncate applies for the same reason.
public static string GradeNotUnderstood(string raw) =>
    $"No se pudo interpretar el grado «{Clamp(raw, 30)}»; la vía se ha creado sin grado";
```

**`AcceptableLength` returns `int?`.** `internal static int? AcceptableLength(int? length) => length is > 5 and < 3000 ? length : null;` — and its comment, which currently documents the 0-sentinel and the "a reviewer has to supply a length before saving" consequence, is rewritten: neither is true after §3.

**`CreateRouteAsync`** writes `Grade = candidate.Grade`, `AidGrade = candidate.AidGrade`, `Length = candidate.Length`, all nullable, and its log line's `({Grade})` tolerates null.

**A matched route gets its gaps filled.** `GradeDisagreement` currently compares two strings and notes a mismatch; with nulls in play it splits into three cases, and the new one is the valuable half:

| route | candidate | behaviour |
|---|---|---|
| `6a` | `6b+` | note the disagreement, change nothing — unchanged, and still the right default (§ plan 30) |
| `6a` | null | nothing |
| null | `6b+` | **fill it**, and say so in the outcome |

Filling an empty grade is the same argument `RecommendedGear` already makes in `RouteImportService.cs:152-159` — an empty field on a matched route is unanswered, not a human's answer a photo could contradict. It is noted rather than silent (unlike `RecommendedGear`) because a grade is the most consequential fact on the row. Same rule for `AidGrade` and `Length`. The concrete win: someone logs a name-only route from the crag under the new form, then imports the topo, and the route completes itself.

**Neither extraction prompt changes, and neither schema gains an `aidGrade` field.** Both prompts already say copy the grade exactly as printed and never convert — which is what delivers `6c/A1` intact. Adding a schema field would ask the model to perform the split that §11 just wrote a deterministic, unit-tested parser for, on the one axis where a wrong answer is a wrong grade in the catalogue.

### 12. Documentation

- **`backend/CLAUDE.md`** — four passages: `RoutesController`'s "`POST`/`PUT` reject a grade that isn't a valid French-scale value"; the `PrintedGrade` paragraph in Route import; the "`Length` is left at the column default (0) … a reviewer editing such a route has to supply a length before saving; that's intended" sentence (now false in both halves); and the hand-edited-migrations list in `Acordanza.Core/Migrations/`, which gains `OptionalRouteGradeAndLength`.
- **`frontend/CLAUDE.md`** — the `Filters` description (grade sentinel + the aid dropdown), and `RouteFormPage`'s entry (the disclosure).
- **`DESIGN.md`** — `GradePill`'s band table gains the `Sin grado` muted row, and the component's description gains the aid-grade composition. The four-band rule itself is unchanged, and saying so explicitly is worth a sentence: aid deliberately gets no fifth hue.
- **`README.md:7`** — "grade (French scale)" becomes "grade (French scale, optional) and artificial grade (A0–A5 / C0–C5)".
- **Plan 17** gets a one-line `Status:` amendment pointing here, since a reader arriving at it will otherwise implement the grammar it describes.

## Not doing

- **No other grading scales.** UIAA (`V+`), YDS (`5.11a`), British (`E2 5b`) and alpine difficulty (`MD+`) remain unstorable as grades. A source printing one now produces a gradeless route with the raw string in the outcome, which is an honest "we didn't understand this" a human resolves in one edit — strictly better than today's total discard, and far cheaper than a `GradeScale` enum with conversion tables the app has no data to validate.
- **No grade ranges.** `6a/6b` stays a rejection on write and a gradeless import.
- **No completeness badge, and no blocking auto-approval of incomplete routes.** The existing review queue is the quality gate: a gradeless route lands `Pending` like any other and a reviewer approves it or rejects it with a reason. Routes created by someone who can self-validate still auto-approve, incomplete or not — unchanged.
- **No "sin grado artificial" filter option** (§5), and no aid-grade band colours (§7).
- **No sorting by grade** — the app has none today, and nullable columns don't change that.
- **No backfill of aid grades out of existing `Description`/`ObligatoryGrade` text.** Guessing structure out of prose is what the strict half of `PrintedGrade` exists to refuse.
- **No `Disclosure` design-system component** (§10).

## Files touched

**`backend/Acordanza.Core`** — `Models/ClimbingRoute.cs`, `Models/AidGradeValidator.cs` (new), `Migrations/*_OptionalRouteGradeAndLength.cs` (+ designer + snapshot).

**`backend/Acordanza.Api`** — `Controllers/RoutesController.cs` (required-set, both validators, `ValidateLength`, the two grade filters, `GET /grades` null filter, `GET /aid-grades` new, three DTO projections), `Controllers/TicksController.cs` (`MyTickDto` projection), `Dtos/RouteDtos.cs`, `Dtos/TickDtos.cs`, `openapi/Acordanza.Api.json` (regenerated).

**`backend/Acordanza.Worker`** — `Services/PrintedGrade.cs`, `Services/IImportReader.cs` (`RouteCandidate`), `Services/TopoImageReader.cs` (`Accept`), `Services/WebPageReader.cs` (`Accept`), `Services/RouteImportService.cs` (`RouteImportOutcomes`, `AcceptableLength`, `CreateRouteAsync`, `GradeDisagreement` → gap-filling).

**`design-system`** — `src/components/GradePill/GradePill.tsx`, `GradePill.css`, `gradeBand.ts`, `aidGrade.ts` (new), `src/index.ts`.

**`frontend`** — `src/api/generated.ts` (regenerated), `src/api/types.ts` (`RouteFilters`, `emptyRouteFilters`), `src/api/routes.ts` (`aidGrade` param, `listAidGrades`), `src/components/Filters.tsx`, `RouteCard.tsx`, `TickedRouteCard.tsx`, `RouteMeta.tsx`, `RouteDetailModal.tsx`, `src/pages/RouteFormPage.tsx`, `src/pages/PendingTasksPage.tsx`, `src/styles.css` (disclosure).

**Docs** — `backend/CLAUDE.md`, `frontend/CLAUDE.md`, `DESIGN.md`, `README.md`, `.claude/plans/17-grade-scale-and-pills.md`.

## Testing

**`Acordanza.Core.Tests`** — new `Models/AidGradeValidatorTests`: `A0`…`A5`, `C0`…`C5`, `A3+`, case folding (`a1` → `A1`), and the rejections (`A6`, `B2`, `A`, `6a`, `""`). `GradeValidatorTests` is untouched — the French grammar itself doesn't change.

**`Acordanza.Api.Tests`** —
- `RouteGradeTests`: two existing tests **invert** — `PUT_with_a_blank_grade_is_rejected` and the blank half of the POST theory now expect success with `grade: null` in the follow-up `GET`. A non-blank invalid grade still 400s with the same Spanish message. New: aid grade accepted/normalized (`a1` → `A1`), aid grade rejected (`A6`), and both grades on one route round-tripping as `6c` + `A1`.
- New `RouteRequiredFieldsTests` (or rows on `RouteFieldsTests`): `POST` with only `{ name, areaId }` returns 200 and the route reads back with null grade/aidGrade/length; `POST` with a blank name 400s; `POST` with `areaId: 0` 400s with the new message; **`PUT` with a blank name 400s** (the §1 hole — this is the regression test that hole never had).
- `RouteFieldsTests`: `length: null` accepted; `length: 3` still 400s.
- `RouteFiltersTests`: `?grade=none` returns only gradeless routes; `?aidGrade=A1` filters; `GET /routes/grades` omits nulls; `GET /routes/aid-grades` returns the aid grades in use and `[]` when there are none. Both new endpoints get their `AuthorizationMatrixTests` row.

**`Acordanza.Worker.Tests`** —
- `PrintedGradeTests`: `6c/A1` → `(6c, A1)`; `A1/6c` → same; `A2` → `(null, A2)`; `6a/6b` → null; `MD+ 6c/A1 (6a+ obl)` → all three; every existing case still passes unchanged (this is the file that proves the split is additive).
- `RouteImportAcceptanceTests` / `WebPageAcceptanceTests`: the unreadable-grade cases **invert** — `V+` now yields an accepted candidate with null grade and a `GradeNotUnderstood` note, not a discard. The other discards (`MultipleRoutes`, `LowConfidence`, `NoRouteName`, `NotAboutARoute`) are unchanged, and asserting that explicitly is the point: this plan loosens exactly one gate.
- `RouteImportServiceTests`: a matched route with a null grade gets it filled and the outcome says so; a matched route with a *different* grade still only gets a note; a created route carries `AidGrade`; `AcceptableLength(null)`/`(3)` → null.
- The two `Category=Enrichment` golden sets are hand-run and not gated by CI, but `RouteImportGoldenSet.json`'s expectations for any aid-graded entry change — noted for whoever next runs it, not a blocker.

**`design-system`** — `GradePill.test.tsx`: the five rows of §7's table, and that an aid-only pill carries no band class.

**`frontend`** — `RouteFormPage.test.tsx`: saving with only sector + name posts nulls for grade/aidGrade/length; the disclosure is closed on `/routes/new` and open when editing a route with any detail set; a bad aid grade shows the Spanish error and doesn't submit. `Filters.test.tsx`: "Sin grado" sets `grade: 'none'`; the aid `<select>` is absent when `GET /routes/aid-grades` returns `[]` and present when it doesn't. `RouteCard`/`TickedRouteCard`/`RouteDetailModal` tests get a gradeless-route render. `routes.test.ts`: `aidGrade` reaches the query string.

**Ordering.** One PR. The migration, both DTO sets, the regenerated OpenAPI document and the regenerated `generated.ts` have to land together or `openapi-contract-check` fails; the frontend cannot compile against the old types once the schemas widen.
