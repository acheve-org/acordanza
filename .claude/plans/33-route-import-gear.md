# Read gear off an imported topo or page, into `RecommendedGear`

Status: Proposed
Issue: not filed yet — file one and link it here before the `plan:` commit
Depends on: [30](30-route-from-topo.md) and [31](31-route-from-url.md) for the whole route-import pipeline this extends — `TopoImageReader`/`WebPageReader`, their extraction providers, `RouteCandidate`, `RouteImportService`'s match-or-create; [29](29-enrichment-quality.md) for the evidence/confidence schema pattern this reuses; [32](32-worker-microsoft-extensions-ai.md) for the `IChatClient`/`WithAnthropicRequest` transport both extraction providers already use. No new dependency, no migration (`ClimbingRoute.RecommendedGear` already exists — `AddClimbingRouteSlug`), no new Foundry deployment, no new queue message. Worker-only except for the golden-set fixtures.

## Problem

`ClimbingRoute.RecommendedGear` is a free-text field (`RoutesController`'s create/edit form has always had it) that a topo photo or a route write-up frequently states outright — a quickdraw count printed next to the line, "llevar 12 exprés y cuerda de 70 m" in a blog post. Today the import pipeline (plans 30/31) reads the route's name, grade, length and (Url kind only) description and openers off the same artifact, but throws the gear line away: neither `ExtractedRoute` (Image kind) nor `ExtractedPageRoute` (Url kind) asks for it, so it never reaches `RouteCandidate` and a reviewer has to go back to the photo or the page to fill it in by hand.

This is a small, additive extension to both extraction schemas and to `RouteImportService`'s match-or-create — no new call, no new provider, no new UI beyond the existing route detail view already rendering `RecommendedGear`.

## Design

### 1. One new nullable field on both extraction schemas

`ExtractedRoute` (`ITopoExtractionProvider.cs`) and `ExtractedPageRoute` (`IPageExtractionProvider.cs`) each gain `string? Gear`, following the exact shape their `Evidence`/`Confidence` fields already have — a plain nullable string, unvalidated, with the acceptance rules enforced downstream in C# rather than in the schema (`pattern`/`minLength` aren't available to structured outputs, same reason the grade and name fields are validated in `Accept`, not in JSON Schema).

```csharp
public record ExtractedRoute(string? Name, string? Grade, int? Length, string? Gear, string? Evidence, string? Confidence);
public record ExtractedPageRoute(
    string? Name, string? Grade, int? Length, string? Gear,
    IReadOnlyList<string>? FirstAscensionists, string? Description, string? Evidence, string? Confidence);
```

Both `ResultSchema`s (`ClaudeTopoExtractionProvider`, `ClaudePageExtractionProvider`) add `"gear": { "type": ["string", "null"] }` to their `route` object's `properties` and `required` arrays — a scalar nullable union, not an object one, so it needs none of the `anyOf` ceremony §5's long comment on the `route` subschema itself already documents (that trap is specific to nullable *objects*; a nullable string is unaffected, same as `reason`/`sectorMentioned` today).

**Independent of the confidence/evidence gate.** `Gear` carries no confidence of its own and is never asked to justify itself with a quote — `Evidence` stays scoped to name+grade, as it is today. A route that clears the existing high-confidence bar for its name and grade gets its gear looked at; a route that doesn't clear that bar is discarded entirely, same as today, and the question of its gear never arises. This is what "best-effort" means here in practice: there's no separate accept/reject decision for gear, only "did the model find some" or not.

### 2. The prompt

Both `BuildPrompt` methods (`ClaudeTopoExtractionProvider`, `ClaudePageExtractionProvider`) gain one instruction alongside the existing `grade`/`length` guidance, keeping the same "copy what's there, never invent" register the grade instruction already sets:

> `gear`: any climbing gear the source states is needed or recommended for this route — quickdraw/express count, rope length, specific protection like friends or slings, bolt spacing if stated as a warning. Copy it close to as written, in a short line or list, not a paragraph — a few words to one short sentence. Null if the source says nothing about gear; do not infer it from the grade or the route's apparent style (e.g. do not write "sport climbing gear" just because the route is bolted).

The "do not infer from the grade/style" clause is load-bearing: unlike name/grade, there's no `Confidence` gate protecting this field, so the prompt itself has to carry the weight of keeping it to transcription rather than invention — a bolted 6a with no printed gear line stays null, not a guessed "12 exprés".

### 3. `RouteCandidate` gains `Gear`, both readers populate it

```csharp
public record RouteCandidate(
    string Name,
    string Grade,
    int Length,
    string Evidence,
    string Description = "",
    IReadOnlyList<string>? FirstAscensionists = null,
    string? SectorMentioned = null,
    string? ObligatoryGrade = null,
    string? Gear = null);
```

`TopoImageReader.Accept` and `WebPageReader.Accept` each add one line: trim `route.Gear`, treat whitespace-only as absent, and truncate defensively to `RoutesController`'s existing 500-character cap on `RecommendedGear` (`Services/GearText.cs` — one static, `GearText.Truncate(string?, maxLength: 500)`, shared by both readers rather than duplicated, mirroring how `PrintedGrade`/`RouteImportService.AcceptableLength` already centralise their own defensive clamps). The prompt asks for brevity so this should be rare in practice; the truncation is the same "don't trust the model, clamp in C#" backstop `AcceptableLength` already applies to `Length`. No `MaxLength` in either JSON schema, for the same reason `pattern`/`minLength` aren't used anywhere else in these two providers.

Neither `Accept` predicate's overall discard logic changes — a route with no `Gear` in the response is exactly as acceptable as one is today; only the field is new.

### 4. `RouteImportService`: create fills it, match fills only if empty

`CreateRouteAsync` gains one line, alongside `Length`/`ObligatoryGrade`:

```csharp
RecommendedGear = candidate.Gear,
```

For a match, the existing method has no per-field fill-in path today — `ObligatoryGrade` is deliberately create-only, and nothing else on a matched route is ever touched. `RecommendedGear` gets its own path, one step more permissive than `ObligatoryGrade`'s, because unlike a grade a free-text gear line has no "human already answered this, a second answer disagrees" shape — an empty field is simply unanswered, and filling it in from a photo the human just submitted is pure upside with nothing to silently overwrite:

```csharp
if (!created && string.IsNullOrWhiteSpace(route.RecommendedGear) && candidate.Gear is { } gear)
{
    route.RecommendedGear = gear;
}
```

Placed in `ImportAsync` right after the existing topo-attach/link-append block, before `GradeDisagreement` is checked. **No note in `Outcome` either way** — not when it fills the field (that's the normal, silent case `ObligatoryGrade`-on-create already sets precedent for: a machine-populated field isn't flagged just for being machine-populated) and not when it's skipped because the field was already set (unlike `GradeDisagreement`, there's nothing to disagree about: the existing value isn't wrong, the extracted one is just redundant information that loses cleanly). This is the one place this plan's write policy is *more* permissive than `ObligatoryGrade`'s "create-only" precedent, and the reason is exactly that asymmetry: `ObligatoryGrade` only makes sense as a companion to a grade the create path is already writing, while `RecommendedGear` on a matched route is filling a gap a human left, not contesting a human's answer.

### 5. Not doing

- **`OptionalGear`.** No basis in the source material for a topo or a page to reliably distinguish "recommended" from "optional" gear — that would be the model inventing a taxonomy the artifact itself doesn't state, which is exactly the kind of guess the grade-copying discipline (§2) exists to avoid elsewhere. If a real source ever states the distinction explicitly, that's a follow-up with its own evidence for why it's reliable.
- **The fixed `Gear` enum** (`bolted`/`removable`/`pitons`/`aiders`). A harder, different inference — a route is often both bolted *and* needs removable protection, so "pick one of four categories" doesn't map cleanly onto what a topo actually shows, and there's no natural acceptance predicate for it the way `PrintedGrade.Parse` gives grade extraction one.
- **A disagreement note when a matched route already has `RecommendedGear` set.** Free text isn't a single fact two sources can disagree about the way a grade is; §4 explains why silently skipping is right here where it wouldn't be for `Grade`.
- **Any change to golden-set *scoring* logic or `Accept`'s discard behaviour.** Gear is additive and never gates acceptance (§1); the existing pass/fail shape of both readers is untouched.

## Files touched

| File | Change |
| --- | --- |
| `backend/Acordanza.Worker/Services/ITopoExtractionProvider.cs` | `ExtractedRoute` + `Gear`; `ResultSchema` + `gear` property; `BuildPrompt` + gear instruction (§1–§2) |
| `backend/Acordanza.Worker/Services/IPageExtractionProvider.cs` | `ExtractedPageRoute` + `Gear`; `ResultSchema` + `gear` property; `BuildPrompt` + gear instruction (§1–§2) |
| `backend/Acordanza.Worker/Services/IImportReader.cs` | `RouteCandidate` + `Gear` (§3) |
| `backend/Acordanza.Worker/Services/GearText.cs` | new — the shared trim/truncate helper (§3) |
| `backend/Acordanza.Worker/Services/TopoImageReader.cs` | `Accept` populates `Gear` via `GearText.Truncate` (§3) |
| `backend/Acordanza.Worker/Services/WebPageReader.cs` | `Accept` populates `Gear` via `GearText.Truncate` (§3) |
| `backend/Acordanza.Worker/Services/RouteImportService.cs` | `CreateRouteAsync` writes `RecommendedGear`; `ImportAsync` fills it on a match only if currently empty (§4) |
| `backend/Acordanza.Worker.Tests/Services/RouteImportGoldenSet.json` | `_comment` + each image entry gains `expectedGear` (§Testing) |
| `backend/Acordanza.Worker.Tests/Services/RouteImportGoldenSetTests.cs` | reads/reports `expectedGear` alongside `expectedName`/`expectedGrade` (§Testing) |

No `AppDbContext.cs` change, no migration, no OpenAPI regen (nothing on the API or DTOs changes — `RecommendedGear` was already read/written by `RoutesController` and returned in `RouteDto`), no frontend change (the detail view and edit form already render/edit `RecommendedGear`; a machine-filled value looks exactly like a human-typed one, which is intentional — see §4).

## Steps

1. **§1–§2** — add `Gear` to both `ExtractedRoute`/`ExtractedPageRoute`, both schemas, both prompts. No behavioural change yet: nothing downstream reads the new field.
2. **§3** — `GearText`, `RouteCandidate.Gear`, both `Accept` predicates populate it.
3. **§4** — `RouteImportService` writes it on create and conditionally on match.
4. **§Testing's golden-set schema extension**, so the next hand-run pass measures gear extraction from day one.

Each slice is independently shippable and there's no reason not to land all four together — this is a small, single-PR plan, unlike 30/31.

## Testing

**Unit** (`Acordanza.Worker.Tests`, no Docker, no network):

- `GearText.Truncate`: null/whitespace → null; a normal short string passed through unchanged; a string over 500 chars truncated to exactly 500.
- `TopoImageReader.Accept` / `WebPageReader.Accept`: a response with `gear` set produces a `RouteCandidate` carrying it, trimmed; a response with `gear: null` produces a candidate with `Gear: null` and is otherwise unaffected — in particular, `gear` alone never triggers a discard, confidence/evidence checks are unaffected by its presence or absence.
- `RouteImportService`: on create, `candidate.Gear` lands on the new route's `RecommendedGear` (including the null case: a route created with no stated gear has `RecommendedGear == null`, not empty string). On a match where the existing route's `RecommendedGear` is null/empty, an extracted `candidate.Gear` fills it. On a match where the existing route already has a `RecommendedGear` value, an extracted `candidate.Gear` is dropped and the existing value is unchanged, with no note added to `Outcome`.

**Integration** (`Category=Integration`, `WorkerFixture`, fake providers): extend the existing create/match test cases in `RouteImportServiceTests` with a candidate carrying `Gear`, asserting the three cases above end-to-end through `ImportAsync` against a real (Testcontainers) database, for both the Image and Url kinds.

**Golden set.** Extend `RouteImportGoldenSet.json`'s schema (both the image-only entries and, once filled, whatever URL-kind fixture plan 31's testing note describes) with `expectedGear: string | null` alongside `expectedName`/`expectedGrade`, and `RouteImportGoldenSetTests` reports a gear-match rate the same way it reports name/grade accuracy today — a human re-fills the affected entries by checking each source for a stated gear line. This is `[Trait("Category", "Enrichment")]`, hand-run only, real Foundry calls, excluded from both CI test jobs exactly as today.

`dotnet test Acordanza.slnx --filter "Category!=Enrichment"` from the repo root with Docker up is the bar for this PR; the golden-set run is separate and manual, as it already is for plans 29/30/31.
