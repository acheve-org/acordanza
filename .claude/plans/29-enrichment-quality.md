# Enrichment quality: ground every link in a retrieved URL, and make the topo findable at all

Status: §5 (the model swap) landed ahead of the rest; §1–§4, §6–§7 not started
Issue: not filed yet — file one and link it here before the `plan:` commit
Depends on: [18](18-route-enrichment-worker.md) — this plan is that feature retuned after its first real use, and every decision below either amends or inherits one of its decisions; [22](22-worker-project-split.md) for `Acordanza.Worker`, the project all the backend changes land in. One migration (`Link.Source`, §6). No frontend change except the badge that column exists to drive (§6). No new Terraform except the Foundry deployment swap in §5, which is a value change, not a new resource.

## Problem

Plan 18 shipped, the button works, and the results are bad: the links it appends are frequently about some other route, and `topoImageUrl` has never once produced an imported topo. Plan 18 anticipated exactly this ("If experience shows the model finds junk, the escape hatch is a review-first flow") and deliberately deferred the response. That trigger has now fired, and the diagnosis is more specific — and more fixable — than "the model isn't good enough". There are four separate causes, and only one of them is about the model.

**The topo cannot work as written.** The request declares the web search tool and nothing else (`Acordanza.Worker/Services/IRouteEnrichmentProvider.cs:175`). Web search returns page *text* — titles, snippets, extracted prose. It never returns an `<img src>`. So when the prompt asks for "its direct image URL as topoImageUrl" (`:124-125`), the model is being asked for a fact that was never in its context; whatever it answers is a reconstruction. That URL 404s, `TryImportTopoAsync` logs `download failed` and returns false (`RouteEnrichmentService.cs:94-99`), and the run reports no topo. Not "rarely finds one" — structurally, close to never.

**Nothing ties a returned link to a page the model actually retrieved.** `FindAsync` reads only `TextBlock`s out of the response (`:143-146`) and discards the `web_search_tool_result` blocks that carry the real result URLs. So the only thing standing between a URL the model half-remembered and `route.Links` is `LinkRules.Resolve`, which checks scheme, length, and duplicates — nothing about whether the page exists or is about this route. A plausible-looking URL on a real climbing domain passes every gate we have. This is the "links not related to the route" symptom, and it is fixable without touching the model at all.

**The pinned model is a generation behind the tool it's being handed.** `Model = "claude-sonnet-4-5"` (`:92`) is paired with `WebSearchTool20260209`. That tool version's headline feature is dynamic filtering — server-side code execution that filters search results *before* they enter the context window — and it requires Opus 4.6+ or Sonnet 4.6+. On Sonnet 4.5 the pairing is outside its supported set: at best the results arrive raw and unfiltered, at a model that is also, being pre-5-family, not thinking. The same pin is why `effort` never executes: `ClaudeModelCapabilities.SupportsEffort("claude-sonnet-4-5")` is false, so the line plan 18 called "the single most important line in this section" is dead code in production. The TEMPORARY comment at `:86-88` is load-bearing in a way its author did not intend.

**The schema gives the model nowhere to express doubt.** `links` is `string[]` (`:102`). The prompt says to return an empty list rather than tangential results, but a bare array of strings makes filling it the path of least resistance and costs nothing to get wrong. Meanwhile the context it gets is three fields — name, grade, area breadcrumb — while the route row holds the one field that would actually disambiguate a route called "La Loca": **the first ascensionists' names**, which are never passed.

Two smaller correctness gaps fall out of the same reading. `FindAsync` never inspects `stop_reason`: a server-side tool loop that hits its iteration cap returns `pause_turn` with no final text block, which parses as `([], null)` and sets `EnrichedAt` — a silent no-op that looks like "nothing found". And `MaxTokens = 4096` against a richer schema risks `max_tokens`, whose truncated JSON throws out of `Deserialize` and burns all five queue redeliveries on an error that will recur identically each time.

## Design

Six sections, in the order they should land. §1 and §2 are the bulk of the quality win and are independent of everything else; §3 is nearly free; §4 is what makes the topo possible; §5 is an infra ask; §6–§7 are how we find out whether any of it worked.

Cost moves up, and that is intended. Plan 18 priced a run at $0.20–0.50 and treated that ceiling as load-bearing. This plan raises `max_uses`, adds a fetch pass, and asks for a model that thinks — call it $0.40–0.90 per run. The bound that matters was never the per-run price; it was the trigger decision (a human pressing a button on a route they are looking at), and that is untouched. A run that costs twice as much and returns two correct links instead of five wrong ones is unambiguously the better purchase.

### 1. Ground every link in a URL the model actually retrieved

The single highest-leverage change, and it is deterministic C# — no prompt, no model, no cost.

The Messages response carries the server-side tool traffic as content blocks alongside the text: each `web_search_tool_result` block holds the list of results the search actually returned, each with its URL and title. Collect that set, and **drop any link in the structured output that isn't in it**. A hallucinated URL becomes zero links instead of one bad link, which is the failure mode we want.

```csharp
// In ClaudeRouteEnrichmentProvider.FindAsync, before parsing the payload:
var retrieved = RetrievedUrls(response.Content);   // from web_search / web_fetch result blocks
...
var grounded = parsed.Links.Where(l => retrieved.Contains(Canonical(l.Url))).ToList();
_logger.LogInformation(
    "enrichment grounding route={RouteName} returned={Returned} retrieved={Retrieved} grounded={Grounded}",
    context.Name, parsed.Links.Count, retrieved.Count, grounded.Count);
```

Three details this depends on:

- **`Canonical` normalises before comparing** — lowercase scheme and host, drop the fragment, drop a trailing slash on the path, and drop known tracking parameters (`utm_*`, `fbclid`, `gclid`). Without it a link that differs from its search result only by `?utm_source=` is dropped as ungrounded, which trades one failure mode for another. Redirect chains are *not* followed for this comparison: the reachability check in §1b already resolves them, and doing it twice doubles the request count for no extra signal.
- **The filter lives inside the provider, not in `RouteEnrichmentService`.** The provider is the only thing holding the raw response, and pushing the retrieved-URL set out through `EnrichmentResult` would force every fake in `RouteEnrichmentServiceTests` to model a search transcript it doesn't care about. The seam stays "here are links we stand behind"; how they earned that is the provider's business.
- **Log the drop count at Information.** With `usage` (already logged) this is the whole telemetry story for §7: `returned` vs `grounded` is a direct, free measure of how much the model is inventing.

**1b. Then check the survivors resolve.** A grounded URL can still be dead, paywalled, or a redirect to a homepage. Before appending, `GET` each candidate with `HttpCompletionOption.ResponseHeadersRead`, a 5-second timeout, and cancellation after the headers — the same shape `TryImportTopoAsync` already uses, so it is a lift of an existing pattern rather than a new one. Drop non-2xx and any final content type that isn't `text/html`. This is one cheap request per candidate on a path that already spends dollars.

**1c. Cap by host, not just by count.** `LinkRules.MaxLinks` is 10, and `RouteEnrichmentService.cs:66-69` will happily fill all ten from one site. Take at most one link per host, and at most **four** per run regardless — the useful outcome for an obscure sport route is one good blog post, not a page of results. Both caps live in the worker: `LinkRules` is the *human-entry* contract shared with `RoutesController`, and tightening it would silently constrain the edit form too.

### 2. Say more, and demand evidence back

**More context in.** `RouteEnrichmentContext` grows from three fields to the ones that actually disambiguate:

```csharp
public record RouteEnrichmentContext(
    string Name,
    string Grade,
    string AreaBreadcrumb,
    IReadOnlyList<string> FirstAscensionists,   // the strongest search key we have, currently unused
    int Length,
    string? Orientation,
    string? Gear,
    double? Lat,                                 // Area.CentroidLat/Lng, when the sector has a polygon
    double? Lng);
```

The first ascensionists are the point. "La Loca" is a hopeless query; «La Loca» plus an aperturista's name is a precise one, and we already have the names on `route.FirstAscensionists` — `RouteEnrichmentService.EnrichAsync` need only `.Include` them. Length and gear are weaker but nearly free as *rejection* signals: a page describing a 300 m multi-pitch is not about our 22 m single-pitch of the same name. The centroid gives the model a geographic anchor when the breadcrumb alone is ambiguous (Spain has more than one "Peña Rubia").

**Evidence out.** The schema becomes an array of objects, and every field on it exists to make a wrong answer harder to produce than a blank one:

```json
{
  "links": [{
    "url":        "the exact URL of a page you opened",
    "title":      "the page's own title",
    "kind":       "first-ascent | video | guidebook | topo | sector | other",
    "evidence":   "a short quote from the page naming BOTH this route and its sector or crag",
    "confidence": "high | medium | low"
  }],
  "topoPageUrl":  "a page publishing a topo for this route, or null",
  "topoImageUrl": "the direct image URL of that topo if you saw one on the page, else null"
}
```

`evidence` is doing the real work: a model that must quote the page naming both the route *and* the sector cannot cheaply return a page about a different route with the same name — the quote it would have to fabricate is a much bigger step than the URL was. `RouteEnrichmentService` then keeps only `confidence == "high"` with non-empty `evidence`, and orders the rest `topo` → `guidebook` → `first-ascent` → `video` → `sector` before applying §1c's caps.

**And a prompt that states the negative contract.** The current prompt (`:118-126`) asks for relevant pages and mentions that most sources are Spanish. What it never says is what a *miss* looks like, and that missing is the expected outcome. Concretely, it should add: that a page about a different route sharing this name, in a different area, is a miss and not a partial hit; that most routes in this app are obscure and returning nothing is the correct and common answer, not a failure; that no URL may be returned that wasn't opened; and that the evidence quote must be copied, not paraphrased. The route's own metadata (length, gear, openers) is given explicitly as rejection criteria, not just as search terms.

`MaxTokens` goes to 8192 to fit four evidence-carrying objects with headroom, and `FindAsync` gains an explicit `stop_reason` check: `pause_turn` and `max_tokens` are logged as warnings and return an empty result rather than silently parsing to `([], null)` or throwing a `JsonException` into five identical queue retries.

### 3. Tell the search tool where it is

Three parameters on `WebSearchTool20260209`, all verified present in `Anthropic` 12.31.0, all effectively free:

- **`UserLocation = new UserLocation { Country = "ES", Timezone = "Europe/Madrid" }`** (`Anthropic.Models.Messages.UserLocation`). Search ranking is location-sensitive, every one of these queries is Spanish-language and Spain-geographic, and today the tool is told nothing. Cost: zero.
- **`MaxUses` 5 → 8.** Searches are $0.01 each; three more is three cents against a run costing dollars. Recall is the binding constraint, not search fees — plan 18's cost model already showed the fees were "the small half".
- **`BlockedDomains`, not `AllowedDomains`.** Block the hosts that generate confident-looking noise — Pinterest, Facebook, Instagram, X, TikTok, and the SEO/AI content farms that restate route databases. Deliberately *not* an allowlist: the best sources for a Spanish first-ascent story are long-tail personal blogs on blogspot and wordpress, and any allowlist we could write would exclude exactly those while admitting the aggregators we're trying to avoid. The two lists are mutually exclusive in the API anyway, so this is a real choice.

Prompt caching is considered and rejected: the cacheable prefix here is a few hundred tokens against a 1024-token minimum on the pinned model, so a breakpoint would silently never cache.

### 4. The topo: let the model open the page

The fix follows directly from the diagnosis — the model needs to have *seen* an image URL before it can return one. Declare `WebFetchTool20260209` alongside the search tool (`MaxUses = 3`, `MaxContentTokens` bounded so a heavy page can't dominate the context). Web fetch can only retrieve URLs already present in the conversation, which makes the pairing exactly right: search puts candidate pages in context, fetch opens the promising ones. This also strengthens §1 and §2 for free — a model that has opened a page can quote it accurately, and its URLs land in the retrieved set that §1 grounds against.

The schema change in §2 splits the outcome into two, in descending order of confidence:

1. **`topoImageUrl` set** → download, run the existing `claude-haiku-4-5` vision check, import. Unchanged from plan 18 §5, including the "skip if the route already has a topo" rule — that path was always correct, it was just never fed a real candidate.
2. **`topoPageUrl` set, `topoImageUrl` null** → append the page as a link (kind `topo`, top of the ordering from §2). A link to the page that publishes the topo is a genuinely good outcome, and plan 18 §5 already anticipated it for the rejected-image case.

The vision prompt tightens with it. Today it asks whether the image is "a climbing topo plausibly matching" the route (`:221-225`) — "plausibly" invites a yes for any topo of any route on the crag. It should ask whether the image is a topo **on which this route's line or name is identifiably marked**, and say that a topo of the sector that doesn't identify this route is a no.

What this does **not** do is parse HTML ourselves. Extracting `og:image` and article `<img>` tags server-side would remove the dependency on the fetch tool's text conversion preserving image links — but it costs a new HTML-parser dependency on the worker (a third Trivy target's worth of supply chain), a second fetch path we own, and per-candidate filtering heuristics that duplicate what the vision check already decides. Path 2 above means the feature still delivers something useful if the conversion does strip images, and the smoke run in §Testing will tell us which it is. If it turns out images are consistently stripped, the extractor is the follow-up — with evidence behind it rather than ahead of it.

**One thing to decide, not to implement.** Auto-importing a third-party topo scan and attributing it to `route.AddedByUserId` (`RouteEnrichmentService.cs:170-172`) is a copyright exposure that plan 18 named as its highest-risk write. Path 2 — link the page, don't import the scan — is not only the fallback for a missing image URL; it may be the better default outright. This plan keeps path 1 because it is what the feature was asked to do, but the switch is one branch, and it is worth an explicit decision before the smoke run rather than after.

### 5. Unpin the model — **done, ahead of the rest of this plan**

Everything above works on the old deployment. All of it works *better* on one that supports the tool it is being handed, and the quota that forced the `claude-sonnet-4-5` pin turned out not to bind: **`claude-opus-5` quota was available, so that is what the Foundry account now deploys.** Landed already, since it is independent of §1–§4 and unblocks judging them.

The swap resolves three things at once: `web_search_20260209`'s dynamic filtering becomes actually available, `effort` stops being skipped by the `ClaudeModelCapabilities` guard, and adaptive thinking gives a search-and-extract job deliberation it was previously doing without. Three deliberate consequences, none of them mechanical:

- **`effort` goes to `High`, not the `Medium` plan 18 chose.** That value was picked for `claude-sonnet-5` on a cost argument ("`medium` on Sonnet 5 is roughly Sonnet 4.6 at `high`") that says nothing about Opus 5, and it was never once sent. The failure this plan exists to fix is judgment — is this page about *this* route — which is what effort buys. It is also the first line to dial back if spend bites; `Medium` and `Low` are known to punch above their weight on this model.
- **`MaxTokens` 4096 → 8192, and this one is not optional.** The request omits `thinking`; on sonnet-4-5 that meant *no thinking*, on Opus 5 it means adaptive thinking is **on**, and `max_tokens` bounds thinking and response text together. A budget sized for a non-thinking model truncates the JSON mid-object, and a truncated payload throws out of `Deserialize` into five identical queue redeliveries. §2 raises it for the richer schema anyway; the model swap made it load-bearing sooner.
- **Cost roughly triples per run**, to about $0.60–1.50: $5/$25 per MTok against sonnet-4-5's $3/$15, times thinking now happening at all, times `high` effort. Plan 18's per-run figure was never the real bound — the hand-pressed button is — but the number should be checked against §7's `usage` logs rather than believed.

The vision call stays on `claude-haiku-4-5`. Plan 18's reasoning holds unchanged: it is a binary image classification with a one-field answer, and the frontier tier buys nothing.

One thing this now exposes that §2 must handle: Opus 5 ships elevated safety classifiers and can return `stop_reason: "refusal"` on an HTTP 200. Route enrichment is about as benign as a prompt gets, so this is a tail risk rather than an expected path — but it lands in the same unchecked `stop_reason` that already swallows `pause_turn`, so §2's guard covers all three cases at once.

### 6. Mark what the machine wrote

`Link` gains one column:

```csharp
public LinkSource Source { get; set; } = LinkSource.Human;   // Human | Enrichment, string-converted like ReviewStatus
```

One migration, defaulting existing rows to `Human` (they are — enrichment has appended a handful and mislabelling those as human-entered is a rounding error against not having the column at all). `RouteEnrichmentService` writes `Enrichment`; `RoutesController` writes `Human` on both create and update paths.

This is the minimum viable version of the review-first flow plan 18 held in reserve, and it is deliberately much less than that flow. It does not gate anything, add a queue, or touch `ValidationStatus` — the write stays direct, which keeps §4 of plan 18 intact. What it buys is that a bad link is *identifiable*: the edit form can badge enrichment links so a reviewer knows what they're looking at, a bad batch can be deleted with one `WHERE Source = 'Enrichment'`, and §7's precision measurement has something to count. A full suggestion/approval UI remains out of scope (§7 of "Not doing" below) — this is the cheap 80%.

**The DTO change is read-only, deliberately asymmetric.** `Links` is `List<string>` on both read DTOs (`RouteDtos.cs:19`, `:48`) and `List<string>?` on the write DTO (`:73`). Only the read side becomes objects (`{ url, source }`); the write side stays a bare string array, because anything arriving over HTTP is human-entered *by definition* — a request body has no business asserting its own provenance, and accepting one would make `Source` forgeable. The two display sites absorb it locally: `RouteFormPage.tsx:74` maps `r.links.map(l => l.url)` into the form's existing `links: string[]` (`:27`) so the editing model is untouched, and `RouteDetailModal.tsx:182-186` renders the badge.

`EnrichedAt` also stops being set unconditionally. Today it is written whether the run found four links or nothing (`RouteEnrichmentService.cs:78`), so a run that failed for an environmental reason — a `pause_turn`, a Foundry timeout, a bad prompt we then fixed — permanently marks the route as done and needs `force=true` to retry. Set it only when the provider returned a well-formed response; leave it null when the run ended on a warning path from §2, so a re-press of the button just works.

### 7. Make the quality measurable

Two things, because right now the only evidence about link quality is someone's impression of it.

**Structured logging of the whole decision chain.** `usage` is already logged (`:180-183`). Add, as one line per run: the raw model JSON, the retrieved-URL set size, and the counts at each filter stage — `returned → grounded (§1) → reachable (§1b) → high-confidence (§2) → appended (§1c)`. Those five numbers say *which* stage is doing the work, which is the difference between tuning the prompt and tuning the filter. OpenTelemetry already ships worker logs to Grafana Cloud under `acordanza-enrichment-worker`, so there is nothing to wire up.

**A golden set, run by hand.** Twelve to fifteen real routes from the actual database, spanning the hard cases — a generic name, a famous route, a route with a known blog write-up, a route with a published topo, and two or three that genuinely have nothing on the web — each with a hand-checked expected outcome (specific URLs, or explicitly "nothing"). It lands as `Acordanza.Worker.Tests/Services/EnrichmentGoldenSetTests.cs` behind a `[Trait("Category", "Enrichment")]` trait. It is run deliberately, before and after a prompt change, and it reports precision (of what was appended, how much was right) and the "correctly returned nothing" rate, which is the metric this plan cares most about.

**Excluding it from CI is a real change, not a free one.** The integration job runs a bare `dotnet test Acordanza.slnx` (`ci-cd.yml:72`) — integration tests aren't opted *in* by a filter, they simply aren't filtered *out*, so a new trait runs in CI by default. That job needs `--filter "Category!=Enrichment"` added, or every push spends real money on live Foundry and network calls. The unit job (`:64`, `Category!=Integration`) needs the same exclusion, since these tests are neither. Getting this wrong is the one way this section can cost more than it saves.

Precision is the target, not recall. Two correct links beat five links of which three are wrong, and returning nothing on an obscure route is a success.

### 8. Not doing

- **A suggestion/approval queue for links or topos.** §6's provenance column is the cheap version. If precision after all of the above is still poor enough to need a human gate, that is the next plan, with numbers from §7 behind it.
- **Server-side HTML parsing for topo image extraction.** Deferred behind the §4 smoke result, deliberately.
- **Any automatic trigger.** Plan 18's trigger decision is untouched and is still what bounds spend.
- **Batch or bulk enrichment**, including a "re-enrich everything with the new prompt" script. The golden set (§7) is how a prompt change gets validated; a backfill is how it gets expensive.
- **Enriching anything but routes.** No area enrichment, no first-ascensionist enrichment.
- **Changing `LinkRules`.** It is the human-entry contract shared with `RoutesController`; every tightening in this plan is worker-side, per §1c.

## Files touched

| File | Change |
| --- | --- |
| `backend/Acordanza.Worker/Services/IRouteEnrichmentProvider.cs` | the bulk of it: `RouteEnrichmentContext` grows (§2), `EnrichmentResult` becomes `EnrichmentLink` objects (§2), the object schema, the rewritten prompt, `stop_reason` handling, `MaxTokens` 8192, grounding filter + `Canonical` (§1), `UserLocation`/`MaxUses`/`BlockedDomains` (§3), `WebFetchTool20260209` (§4), tightened vision prompt (§4) |
| `backend/Acordanza.Worker/Services/RouteEnrichmentService.cs` | `.Include(r => r.FirstAscensionists)` and the richer context (§2); confidence/evidence filter, kind ordering, per-host and per-run caps (§1c); reachability check (§1b); `topoPageUrl` fallback path (§4); `Source = Enrichment` (§6); conditional `EnrichedAt` (§6); stage-count logging (§7) |
| `backend/Acordanza.Core/Models/Link.cs` | + `Source` (`LinkSource`, string-converted) (§6) |
| `backend/Acordanza.Core/Data/AppDbContext.cs` | `LinkSource` conversion (§6) |
| `backend/Acordanza.Core/Migrations/*` | one migration: add `Link.Source`, default existing rows to `Human` (§6) |
| `backend/Acordanza.Api/Controllers/RoutesController.cs` | `Source = Human` on the create/update link paths (§6) |
| `backend/Acordanza.Api/Dtos/RouteDtos.cs` | read DTOs' `List<string> Links` (`:19`, `:48`) → `List<RouteLinkDto>`; write DTO (`:73`) unchanged (§6) — the DTO change that forces the OpenAPI regen |
| `backend/Acordanza.Api/openapi/Acordanza.Api.json`, `frontend/src/api/generated.ts` | regenerated, or `openapi-contract-check` fails |
| `frontend/src/components/RouteDetailModal.tsx` | muted badge on enrichment-sourced links (`:182-186`) (§6) |
| `frontend/src/pages/RouteFormPage.tsx` | map `r.links` to `string[]` at load (`:74`); form state unchanged (§6) |
| `frontend/src/pages/RouteFormPage.test.tsx` | MSW route fixtures carry the new link shape |
| `backend/Acordanza.Worker.Tests/Services/RouteEnrichmentServiceTests.cs` | fakes updated for the new result shape; new cases for the filters (§Testing) |
| `backend/Acordanza.Worker.Tests/Services/EnrichmentGoldenSetTests.cs` | new — `Category=Enrichment` (§7) |
| `.github/workflows/ci-cd.yml` | **both** test jobs exclude `Category=Enrichment` (`:64`, `:72`) — §7 |
| `infra/terraform/ai_foundry.tf` | ~~deployment swapped to `claude-opus-5`~~ — landed (§5) |

## Steps

Each slice is independently shippable and independently improves the result, so they can land as separate PRs against the same issue.

1. **§1 + §1b + §1c** — grounding, reachability, caps. Pure C#, no prompt or schema change, no model change. Biggest single quality win and the cheapest to verify.
2. **§2** — richer context, object schema, rewritten prompt, `stop_reason` handling. Touches the provider contract, so `RouteEnrichmentServiceTests`' fakes move with it.
3. **§3** — the three tool parameters. Small enough to ride with slice 2 if convenient.
4. **§4** — `web_fetch`, the `topoPageUrl` path, the tightened vision prompt. First slice where a topo can actually be imported.
5. **§6** — `Link.Source`, the migration, the DTO/OpenAPI regen, the frontend badge.
6. **§7** — the golden set and the stage logging. Last in landing order, first in usefulness for whatever comes after this plan.
0. **§5** — the model swap. Done; it was independent of everything else and is what the rest gets judged against.

## Testing

Unit-tier, in `Acordanza.Worker.Tests` (no Docker, no network — these are the parts worth pinning):

- `Canonical` (§1): tracking parameters stripped, fragment dropped, trailing slash normalised, host lowercased, and two URLs differing only in those ways comparing equal.
- The grounding filter: a link absent from the retrieved set is dropped; one present survives; an empty retrieved set drops everything.
- The confidence/evidence filter and kind ordering (§2), and the per-host and per-run caps (§1c) — including the case that made this plan necessary, five results from one host collapsing to one.
- `stop_reason` handling (§2): `pause_turn` and `max_tokens` produce an empty result and a warning, never an exception and never a set `EnrichedAt`.

Integration-tier (`Category=Integration`, existing `WorkerFixture`), driven by a fake provider as today:

- `Source = Enrichment` on appended links, `Human` untouched on pre-existing ones (§6).
- `topoPageUrl` with a null `topoImageUrl` appends a link and imports no topo (§4).
- A route that already has a topo still skips the import path (unchanged plan 18 §5 behaviour, now with two ways in — worth re-pinning).
- `EnrichedAt` set on a clean run, left null on a warning-path run (§6).

Manual smoke, against real Foundry, before merging slice 4 — this is the step that decides §4's open question. Pick three routes with a known published topo and run enrichment on each; inspect whether the fetch results carry image URLs at all. If they do, path 1 works and the HTML extractor stays out of scope. If they consistently don't, path 2 is the shipping behaviour and the extractor becomes the follow-up issue.

Then the golden set (§7) as the acceptance gate for the whole plan: run it before slice 1 to get a baseline of the current mess, and after slice 6. The number to move is precision, and "correctly returned nothing" counts as a win.
