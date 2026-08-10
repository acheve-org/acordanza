# The French grade scale: a GradeValidator, and grade pills coloured by band

Status: done
Issue: [#152](https://github.com/acheve-org/acordanza/issues/152)
Depends on: nothing. Touches all three packages — one new backend rules class + write-side validation, one new pure function and three colour tokens in `design-system/`, and the form/display sites in `frontend/`. No DTO shape changes, so **no OpenAPI regeneration and no `generated.ts` diff** (`Grade` stays a `string` on the wire; what changes is which strings are legal). Interacts with plan [16](16-route-new-fields.md) — see §7.

## Problem

`ClimbingRoute.Grade` is free text today, deliberately so: `RoutesController` comments it as such, `GET /routes/grades` exists precisely because there's no fixed scale to hardcode, the form placeholder offers `V+, 6a, E2 5b…`, and `README.md` advertises "grade (any grading system)". Issue #152 closes that: the app uses the **French scale only**, `[1-9][a-c]` optionally followed by `+` — `5a`, `6b`, `7c+`, `9b+` are grades; `V+`, `E2 5b`, `6d`, `10a`, `6` are not.

Two consequences, and they're of different kinds:

1. **A write-side rule.** Nothing rejects a bad grade today, on either side. The issue names the artefact — a `GradeValidator` — so the grammar gets one home per side of the wire rather than being inlined at each call site.
2. **A presentation rule.** `GradePill` renders every grade in the same teal (`--accent-2`); the issue wants four bands — 1–5 teal (unchanged), 6 orange, 7 red, 8–9 purple.

Three things make this bigger than "add a regex":

**Free text is already stored.** Whatever is in the deployed database was written under the old rule. Existing rows can be `V+`. This plan enforces on *write* only (§2), which means an existing non-conforming route can still be read and displayed but cannot be saved again until its grade is fixed — see §6 for the pre-merge check that turns this from a surprise into a known list.

**`--accent-2` is the whole point of the teal.** DESIGN.md is explicit: "Keep the accent palette narrow — violet for the primary CTA, teal for grades/success, red for danger. No third accent." This issue adds two hues. That's a deliberate amendment to a written rule, not an oversight, so DESIGN.md's Do's and Don'ts get edited in the same PR (§4) — otherwise the next change reads the old rule and reverts this one.

**Case.** `Grade` backs an exact-match filter (`r.Grade == grade`) and a `Distinct()` list (`GET /routes/grades`). If `6A` and `6a` both store, the filter dropdown grows duplicate entries that each match half the routes. So the validator normalizes before it validates, and the normalized form is what's stored (§1).

## Design

### 1. `GradeValidator` — the backend rule

`backend/Acordanza.Api/Models/GradeValidator.cs`, alongside `Validation.cs`, which is the existing precedent for "a static class in `Models/` holding one cross-entity rule with no EF dependency":

```csharp
using System.Text.RegularExpressions;

namespace Acordanza.Api.Models;

// The French sport-grade scale, and the only grading system the app accepts (issue #152):
// a number 1–9, a letter a–c, and an optional '+'. Mirrored client-side by
// design-system/src/components/GradePill/gradeBand.ts — keep the two in step by hand.
public static partial class GradeValidator
{
    [GeneratedRegex(@"^[1-9][a-c]\+?$")]
    private static partial Regex Pattern { get; }

    /// Trim + lowercase. Grade is an exact-match filter key and a Distinct() list, so the
    /// stored form has to be canonical — "6A " and "6a" are the same grade, not two.
    public static string Normalize(string? grade) => (grade ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string? grade) => Pattern.IsMatch(Normalize(grade));
}
```

`Normalize` is public and separate from `IsValid` because the controller needs both: it validates the request, then stores the normalized string, and §3's change detection compares normalized-to-normalized. A single `TryNormalize(out …)` would work equally well; two functions read better at three call sites.

**Only `a`–`c`, only `1`–`9`, only a trailing `+`** — no `9c+`-and-beyond escape hatch, no `6a/6b` ranges, no `~`/`?` uncertainty markers. The issue enumerates the grammar exactly and the app has no data that needs the extras. `Normalize` folds case; it does **not** strip inner whitespace (`6 a` stays invalid) — accepting a typo silently is worse than a 400 with a clear message.

### 2. Enforcing it on write

`RoutesController` gains one check, in both `CreateRoute` and `UpdateRoute`, immediately after the existing name/grade-present check and next to `ValidateOrientation`/`ValidateGear`, which is where this file already puts its field-shape rules:

```csharp
if (!GradeValidator.IsValid(request.Grade))
{
    return BadRequest(new { error = "el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)" });
}
```

Note `UpdateRoute` has **no** name/grade-present check today — only `CreateRoute` does. `IsValid` covers both cases there (`null`, `""` and `"   "` all normalize to `""`, which fails the pattern), so `UpdateRoute` needs only this one line and inherits the required-ness for free.

Both then store the normalized value rather than the raw one:

```csharp
Grade = GradeValidator.Normalize(request.Grade),   // CreateRoute
route.Grade = normalizedGrade;                      // UpdateRoute, hoisted to a local
```

**No `[RegularExpression]` data annotation on `UpsertRouteRequest`.** The rest of this controller's field validation is hand-rolled and returns `{ error = "…" }` in Spanish; an annotation would produce a `ValidationProblemDetails` in English with a different body shape, which the frontend's `ApiError` path doesn't read. Consistency with the surrounding code wins.

**No DB `CHECK` constraint and no migration.** A constraint would reject writes to pre-existing bad rows from *any* path including a hand-fix, and would need a data migration ahead of it. The controller is the only writer; enforcing there is enough for this app.

**`GET /routes/grades` keeps returning the grades actually in use**, not the 54 possible ones — a dropdown offering 27 grades nobody has climbed is worse UX, and the endpoint's `Distinct()` now returns clean values thanks to normalization. Its comment ("Grades are free text…") is now wrong and gets rewritten. Its `OrderBy(g => g)` stays: lexicographic order is *already* correct for this scale, since every grade is one digit then one letter then an optional `+`, `+` sorts before any letter, and the scale stops at 9 so there's no `10a` to sort before `2a`.

### 3. Change detection

`UpdateRoute`'s `changed` expression must compare normalized values:

```csharp
route.Grade != normalizedGrade ||
```

Without this, saving a form whose grade the user retyped as `6A` re-flags a validated route as pending for a no-op edit. Same class of trap as the string-normalization one in plan [16](16-route-new-fields.md) §4, and it gets its own test (§Testing).

### 4. The colour bands — design system

#### Tokens

Two new raw colours, and two reuses. DESIGN.md front-matter `colors:` block and `design-system/src/tokens.css`, in the existing colour group:

| Band | Hue | Raw token | Value | Contrast on `{colors.surface}` |
|---|---|---|---|---|
| 1–5 | teal | *(reuses)* `accent-teal` | `#007a63` | ≈ 5.0 |
| 6 | orange | **new** `grade-orange` | `#b45309` | ≈ 4.7 |
| 7 | red | *(reuses)* `danger` | `#d13a4a` | ≈ 4.5 |
| 8–9 | purple | **new** `grade-purple` | `#6b21a8` | ≈ 8.2 |

**Band 7 reuses `{colors.danger}` rather than introducing a second red.** Two reds a few degrees apart is the kind of thing that makes a palette look accidental, and the contexts don't collide in practice — a grade pill is mono-face, `{rounded.sm}`, and sits in a card header; a rejected `Chip` is Inter, `{rounded.full}`, and carries the word "Rechazado". Its ≈4.5 contrast is the tightest of the four but is the value `.chip-rejected` already ships as text — this plan doesn't make it worse and doesn't fix it either.

**Band 8–9's purple is deliberately not `{colors.primary}`** (`#3f3fe0`). The brand cobalt means "loudest action" and must not start also meaning "hard route"; `#6b21a8` is far enough round the wheel to read as a different thing.

Then the per-band trio in `tokens.css`, following the `--accent-2` / `--accent-2-bg` / `--accent-2-border` shape exactly (foreground at full, fill at 8%, border at 24%):

```css
--color-grade-orange: #b45309;
--color-grade-purple: #6b21a8;

/* Grade bands (issue #152). 1–5 keeps the original teal pill, so the base
   .grade-pill rule already covers it and it needs no band class. */
--grade-6: var(--color-grade-orange);
--grade-6-bg: rgba(180, 83, 9, 0.08);
--grade-6-border: rgba(180, 83, 9, 0.24);
--grade-7: var(--color-accent-danger);
--grade-7-bg: var(--danger-bg);
--grade-7-border: var(--danger-border);
--grade-8-9: var(--color-grade-purple);
--grade-8-9-bg: rgba(107, 33, 168, 0.08);
--grade-8-9-border: rgba(107, 33, 168, 0.24);
```

The `rgba()` literals repeat their hex's channels because CSS custom properties can't be composed into a colour function without `color-mix()`/relative colour syntax; the existing `--accent-2-bg`/`--danger-bg` do the same thing, so this matches rather than introduces the duplication.

#### `gradeBand` — the client-side parse

`design-system/src/components/GradePill/gradeBand.ts`:

```ts
/** The four colour bands of the French scale (issue #152). */
export type GradeBand = '1-5' | '6' | '7' | '8-9'

/**
 * The band a French-scale grade falls in, or `null` if it isn't one — which
 * doubles as the client-side validity check (the authority is the backend's
 * GradeValidator; this mirrors it).
 */
export function gradeBand(grade: string): GradeBand | null {
  const g = grade.trim().toLowerCase()
  if (!/^[1-9][a-c]\+?$/.test(g)) return null
  const n = Number(g[0])
  if (n <= 5) return '1-5'
  if (n >= 8) return '8-9'
  return String(n) as GradeBand
}
```

Exported from `design-system/src/index.ts` alongside `GradePill`. **`null` for "not a grade" is what makes this one function rather than two**: the pill needs a band, the form needs a yes/no, and a single predicate keeps the client from carrying two copies of the grammar that can drift apart. Two total definitions exist across the repo (this one and §1's C# regex), each with a test suite over the same valid/invalid table (§Testing).

#### `GradePill`

```tsx
export interface GradePillProps {
  children: string
}

export function GradePill({ children }: GradePillProps) {
  const band = gradeBand(children)
  const className = ['grade-pill', band && band !== '1-5' ? `grade-band-${band}` : undefined]
    .filter(Boolean)
    .join(' ')

  return <span className={className}>{children}</span>
}
```

Two things here:

**`children` narrows from `ReactNode` to `string`.** Both existing call sites (`RouteCard`, `TickedRouteCard`) already pass a plain string, so this costs nothing at the call sites and it's what lets the component derive its own presentation from its own input — the same shape `Badge` already has (`count: number` → its own "99+" rendering). The alternative, a `band` prop the caller computes, would push "where do the band boundaries sit" out of the design system, which is exactly the knowledge the issue asks to put *in* it.

**Band `1-5` and `null` both render bare `.grade-pill`.** The base rule is already the teal the issue calls "the current green pill", so the low band needs no modifier class and an unparseable legacy grade (`V+`, until someone fixes it) degrades to the current look instead of disappearing or throwing. Only three modifier classes exist.

`GradePill.css` gains three blocks, mirroring `Chip.css`'s `.chip-approved`/`.chip-rejected` shape:

```css
.grade-band-6   { color: var(--grade-6);   background: var(--grade-6-bg);   border-color: var(--grade-6-border); }
.grade-band-7   { color: var(--grade-7);   background: var(--grade-7-bg);   border-color: var(--grade-7-border); }
.grade-band-8-9 { color: var(--grade-8-9); background: var(--grade-8-9-bg); border-color: var(--grade-8-9-border); }
```

(`border-color`, not the `border` shorthand — the base rule's `1px solid` stays.)

#### DESIGN.md

The doc is the spec, so it changes with the code, per its own Iteration Guide:

- front-matter `colors:` — `grade-orange`, `grade-purple`;
- Colors §"Accents" — one bullet per new token, plus the band table above;
- Components §`GradePill` — it now takes a grade *string* and colours itself by band, with the mapping and the `null`-falls-back-to-teal rule;
- **Do's and Don'ts** — "Keep the accent palette narrow — … No third accent." is amended to carve out the grade scale explicitly: the four grade bands are a closed, data-driven set that isn't available for general use, and the no-third-accent rule still binds everything else. Without this edit the doc and the code disagree.

### 5. Using it — frontend

**`RouteFormPage`.** The grade input keeps `type="text"` (a `<select>` is out of scope, §7) and gains a submit-time check plus corrected copy:

- placeholder `V+, 6a, E2 5b…` → `6a, 7c+, 8a…` — it currently advertises two grades the backend will now reject;
- a `<span className="hint">(escala francesa)</span>` on the label, the same hint idiom the form already uses;
- in `handleSubmit`, before the API call: `if (gradeBand(form.grade) === null) { setError('el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)'); return; }` — the same string the backend returns, so the user sees one message regardless of which layer caught it;
- the submitted value is `form.grade.trim().toLowerCase()`, matching what §1 will store anyway, so the edit form doesn't come back showing a different string than the user typed.

No `pattern=` attribute: it would be a third copy of the grammar (see §4) and the native bubble can't be worded in the app's voice.

**`DetailModal`.** Line 77 renders `{route.grade} · {route.areaPath}` as plain text inside `.detail-meta` — the one place a route's grade is shown *without* a pill. It becomes `<GradePill>{route.grade}</GradePill>` followed by the area path, since "use it when showing the routes" is the issue's instruction and the detail modal is the primary route display. This is a small layout change (an inline-block pill inside a `<p>`), so `.detail-meta` gets `display: flex; align-items: center; gap: var(--space-sm);` in `styles.css` and the `·` separator goes — verify visually (§Testing).

**`RouteCard` and `TickedRouteCard` need no change at all** — they already render `<GradePill>{route.grade}</GradePill>` and pick up the colours for free. That's the payoff for putting the parse inside the component.

**`Filters`' grade `<select>` stays plain.** A native `<option>` can't carry the pill styling in any cross-browser way; replacing it with a `Combobox` to get coloured rows is a separate UX change nobody asked for.

**`PendingTasksPage`** interpolates the grade into a subtitle string (`${r.areaPath} · ${r.grade}`) — left as text. It's a compact review-queue row, not a route display.

### 6. Existing data

Enforcement is write-side, so nothing breaks on read, but a route with a legacy grade can't be saved until its grade is fixed. Before merging, run against the deployed database:

```sql
SELECT "Id", "Name", "Grade" FROM core."Routes" WHERE "Grade" !~ '^[1-9][a-c]\+?$';
```

and fix the offenders by hand (this is a six-person app; the list is expected to be short or empty). Also worth running the case check — `WHERE "Grade" <> lower(btrim("Grade"))` — since those rows are valid but not canonical, and will silently re-normalize on their next edit. Record the result in the PR description. If the list turns out to be long, that's the signal to add a one-off `UPDATE` to a migration instead, and this plan should be revised rather than worked around.

### 7. Not doing

- **A `<select>`/`Combobox` of the 54 legal grades in the route form.** Now that the scale is closed this is the obvious follow-up, but it's a UX change with its own decisions (grouped by number? `+` as a separate toggle?) and the issue asks for a validator, not a picker.
- **`ObligatoryGrade`** — plan [16](16-route-new-fields.md) §2 adds it as free text, reasoning explicitly that "the app supports any grading system". #152 retires that reasoning. Plan 16 is unimplemented, so whichever lands second owns the reconciliation: `ObligatoryGrade` is a grade and should run through `GradeValidator` (nullable, so `IsValid` applies only when non-blank) and render as a `GradePill`. Flagged here, not fixed here — this plan doesn't touch a column that doesn't exist yet.
- **A `CHECK` constraint, a data migration, or a backfill.** See §2 and §6.
- **Grade conversion between systems** (French ↔ YDS ↔ UIAA), and **boulder scales** (Fontainebleau `7A`, V-scale). The issue defines one scale; adding a second is a data-model change.
- **Colouring the filter dropdown, the pending-tasks subtitle, or the landing page's hero flags.** The landing page does get its one factual fix (below).
- **Re-styling `Chip`'s red** to disambiguate it from band 7. See §4.

### 8. Docs and the landing page

Four places state the old rule and are wrong the moment this merges:

- `README.md:7` — "grade (any grading system)" → the French scale.
- `backend/CLAUDE.md:36` — `GET /grades` "lists the distinct grades in use"; still true, but the surrounding free-text framing needs the `GradeValidator` mention.
- `frontend/CLAUDE.md`'s `components/` bullet — "the grade options come from `GET /routes/grades`, since grades are free text on the backend" — the *reason* changes even though the behaviour doesn't (§2).
- `landing/index.html:443` — the feature card's sample chips are `7a+`, `6c`, **`V+`**. `V+` is no longer a grade this app accepts; swap it for `8a`, which also demonstrates the new purple. The landing page keeps its own inline copy of the tokens (it has no build step), so this needs `--grade-*` values pasted into its `:root` block and a band class on each `.grade-chip` — a marketing page advertising the feature in the *old* single colour would be odd now that it's the headline change. Keep it to those three chips; no `gradeBand` logic in a static page.

## Files touched

| File | Change |
|---|---|
| `backend/Acordanza.Api/Models/GradeValidator.cs` | new (§1) |
| `backend/Acordanza.Api/Controllers/RoutesController.cs` | validity check ×2, normalize on store ×2, `changed` term, `GET /grades` comment (§2, §3) |
| `design-system/src/tokens.css` | 2 raw colours + 9 band tokens (§4) |
| `design-system/src/components/GradePill/gradeBand.ts` | new (§4) |
| `design-system/src/components/GradePill/GradePill.tsx` | `children: string`, band class (§4) |
| `design-system/src/components/GradePill/GradePill.css` | 3 band rules (§4) |
| `design-system/src/index.ts` | export `gradeBand`, `GradeBand` |
| `DESIGN.md` | tokens, `GradePill` entry, amended "no third accent" rule (§4) |
| `frontend/src/pages/RouteFormPage.tsx` | submit check, placeholder, hint, lowercase on submit (§5) |
| `frontend/src/components/DetailModal.tsx` | grade → `GradePill` (§5) |
| `frontend/src/styles.css` | `.detail-meta` flex row (§5) |
| `landing/index.html` | band tokens + sample chips (§8) |
| `README.md`, `backend/CLAUDE.md`, `frontend/CLAUDE.md` | free-text → French scale (§8) |
| `backend/Acordanza.Api.Tests/Models/GradeValidatorTests.cs` | new |
| `backend/Acordanza.Api.Tests/Controllers/RouteGradeTests.cs` | new |
| `backend/Acordanza.Api.Tests/Controllers/RouteValidationLifecycleTests.cs` | +1 case |
| `design-system/src/components/GradePill/GradePill.test.tsx` | new |
| `frontend/src/pages/RouteFormPage.test.tsx` | new (or extended, if plan 16 lands first) |

`backend/Acordanza.Api/openapi/Acordanza.Api.json` and `frontend/src/api/generated.ts`: **unchanged** — no DTO shape changes, so nothing to regenerate. Existing fixtures (`SeedData`'s `grade = "6a"`, `AuthorizationMatrixTests`' `"grade":"6a"`, `RouteFiltersTests`' `6a`/`6b`, `factories.ts`' `6a`) are all already valid French grades, so no test churn beyond the new files.

## Testing

**Backend, pure unit** — `Acordanza.Api.Tests/Models/GradeValidatorTests.cs`, no Docker, alongside `ValidationTests.cs`. One `[Theory]` per direction over the table that §4's design-system test duplicates verbatim — that duplication is the drift detector, so keep the two lists identical:

- valid: `1a`, `5c`, `6a`, `6a+`, `7c+`, `8b`, `9c+`, and the normalizing cases `6A`, `" 6a "`, `7C+`;
- invalid: `0a`, `10a`, `6d`, `6`, `a`, `+`, `6a++`, `6 a`, `6a+extra`, `V+`, `E2 5b`, `""`, `"   "`, `null`;
- `Normalize` round-trip: `"  7C+ "` → `"7c+"`, `null` → `""`.

**Backend, integration** — `Acordanza.Api.Tests/Controllers/RouteGradeTests.cs`, `IntegrationTestBase` + `TestUsers`, `[Trait("Category", "Integration")]`. Through real HTTP, so the 400 body is under test too:

- `POST /routes` with `V+`, `6d`, `10a`, `6` → 400 each, and the body's `error` is the Spanish message (asserting the *message*, not just the status — this is what the user sees);
- `POST /routes` with `6A` → 201, and a follow-up `GET` returns `6a`. This is the normalization contract, and it's the one a naive `IsValid`-only implementation fails;
- `PUT /routes/{id}` with an invalid grade → 400 (`UpdateRoute` has no name/grade-present check of its own, §2 — easy to add the line to `CreateRoute` only and ship a hole);
- `PUT /routes/{id}` with a blank/omitted grade → 400, same reason;
- `GET /routes?grade=6a` matches a route created as `6A` — the filter and the store agree after normalization, which is the actual reason normalization exists.

`RouteValidationLifecycleTests` gains one case next to `Changing_the_grade_clears_validation`: **a PUT that re-sends the same grade in different case keeps the validation** (§3). It fails against `route.Grade != request.Grade`.

No `AuthorizationMatrixTests` row — no new endpoint, no policy change.

**Design system** — `GradePill.test.tsx`, its first test (the package's Vitest setup already exists for `Modal`/`Combobox`/`Pagination`). Since internal class names aren't a public API, assert on the *computed* band via the rendered element's class list at the boundary values only, and keep the pure `gradeBand` table as the detailed test:

- `gradeBand`: `1a`/`5c+` → `1-5`; `6a`/`6c+` → `6`; `7a`/`7c+` → `7`; `8a`/`9c+` → `8-9`; every invalid string from the backend list → `null`;
- `GradePill` renders the grade text for all four bands and for `V+`;
- `GradePill` with `V+` renders no band class (the legacy fallback, §4).

**Frontend** — `RouteFormPage.test.tsx` (new here; plan 16 also creates this file, so whichever lands second extends rather than replaces it). `renderWithProviders` + MSW, remembering `onUnhandledRequest: 'error'` means the `AreaPicker`/`FirstAscensionistPicker` endpoints need handlers even when unused:

- typing `V+` and submitting shows the Spanish message and issues **no** POST (assert the handler wasn't called — the point of the client check is saving the round trip);
- typing `6A` and submitting POSTs `grade: "6a"` — assert the intercepted request body.

**Visual** — via the `run-acordanza` skill, since half this issue is colour: `/routes` with one route per band seeded, at desktop and ≤720px, checking the four pills side by side read as four distinct states and that band 7 doesn't read as an error next to a `Chip status="rejected"` in the same list; plus `DetailModal` open, for §5's `.detail-meta` reflow. Then `landing/index.html` opened directly (no build step) for its three chips.

`bun run test` and `bun run build` in both `design-system/` and `frontend/` — the build is what catches any remaining `<GradePill>` call site passing a non-string child. `dotnet test Acordanza.slnx` from the repo root, Docker up for the integration trait.

## Appendix — Spanish copy

Backend `{ error }`, lowercase-initial to match the file's existing strings, and reused verbatim by the frontend's client-side check:

- `el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)`

Form: label `Grado` + `<span class="hint">(escala francesa)</span>`, placeholder `6a, 7c+, 8a…`.
