# Move the enrichment worker onto Microsoft.Extensions.AI (`IChatClient`)

Status: Done — §1–§12 implemented. Five deviations, each forced by the code as it actually stands rather than as the plan pictured it:

1. **`MessageCreateParams.Model`/`MaxTokens` are `required` members**, so §4's fragment can't omit them. `WithAnthropicRequest` sets `Model = modelId` and reads the budget off `options.MaxOutputTokens` — and **throws** when that's unset, which turns §2's "worth a code-review line item" into an enforced invariant instead. Both are inert on the wire (the portable `ChatOptions` values are what ship), but the throw is the part that matters: with no default configured on the client, an omitted `max_tokens` would otherwise only fail at the first real Foundry call.
2. **The models and budgets in §5–§9 were stale.** `main` pins topo extraction and page extraction to `claude-opus-5` (not `claude-sonnet-5`) at 4096 and 8192 tokens (not 1024 and 4096) — plans 30/31's sonnet deployment never existed on this subscription. This plan is a transport swap, so every existing model id and token budget was carried over verbatim; only §2's "three models, five budgets" framing changed (it's two models and four budgets).
3. **The result schemas changed type, not content.** `ChatResponseFormat.ForJsonSchema` takes a `JsonElement`, so each provider's `ResultSchema` is now deserialized as one rather than as `IReadOnlyDictionary<string, JsonElement>` (what `JsonOutputFormat.Schema` needed). Byte-identical JSON.
4. **`AddChatClient` is registered on the Foundry-configured branch only**, not unconditionally — building the client requires `Anthropic:FoundryResource`, which is exactly what that branch tests for, and the five `NoOp*` providers take no client.
5. **Two more test files needed the constructor change** than §11 listed: `EnrichmentGoldenSetTests` and `RouteImportGoldenSetTests` construct `Claude*` providers directly (they're the `Category=Enrichment` harnesses), and now pass `FoundryClient.CreateChatClient(configuration)`. `ClaudeModelCapabilitiesTests` also gained three cases beyond §11's theory: no-effort-asked-for, the missing-budget throw, and one `[MemberData]` theory over all five providers' real `BuildOptions()` asserting each pins its model id and token budget.

`dotnet test Acordanza.slnx --filter "Category!=Enrichment"` is green (62 + 225 + 209). §Testing's manual smoke run against real Foundry — all three model/tool combinations, plus the Grafana span check — is still outstanding and can only be done by a human with Foundry access.
Issue: none
Depends on: [22](22-worker-project-split.md) for the project shape this touches (`Acordanza.Worker/Services/`); [29](29-enrichment-quality.md), [30](30-route-from-topo.md) and [31](31-route-from-url.md), all landed on `main` since this plan was first drafted and all rewritten here to match — see §0. Worker only: no schema change, no API surface change, no frontend change, no Terraform change.

## 0. Why this plan was rewritten, not just renumbered

The first draft of this plan (this session, before §0) covered two provider classes: `ClaudeRouteEnrichmentProvider` and `ClaudeTopoVerificationProvider`. Three plans landed on `main` since — [29](29-enrichment-quality.md) (grounding, richer schema, the `claude-opus-5` re-pin), [30](30-route-from-topo.md) (create a route from a topo photo), [31](31-route-from-url.md) (create a route from a URL, which also generalised plan 30's table) — and between them they:

- Rewrote `ClaudeRouteEnrichmentProvider` almost entirely: a new model (`claude-opus-5`), a second tool (`WebFetchTool20260209` alongside `WebSearchTool20260209`), a richer object schema, explicit `stop_reason` handling, and — the part that matters most for this plan — **grounding logic that reads the raw response's `web_search_tool_result`/`web_fetch_tool_result` content blocks**, which didn't exist when this plan was first drafted.
- Added three more Claude call sites that didn't exist before: `ClaudeTopoImportGateProvider` (a binary vision gate with few-shot examples), `ClaudeTopoExtractionProvider` (vision extraction of a route from a topo photo), and `ClaudePageExtractionProvider` (text extraction of a route from a fetched web page).
- Added `ClaudeModelCapabilities.SupportsEffort(model)` — a runtime guard added in [#197](https://github.com/acheve-org/acordanza/pull/197) after `effort` 400'd in production on a model that doesn't support it. Every one of the five providers now calls it before setting `OutputConfig.Effort`.

So the worker now has **five** Claude call sites sharing three patterns (`FoundryClient` construction, the effort-capability guard, a `stop_reason` incompleteness check), not two sharing one. The plan below is a rewrite, not an update, because the design has to generalise across all five — and because a second spike (this session) found a mapping gap in `IChatClient`'s portable surface that the first draft never had reason to look for.

The plan number moves from 29 (this branch's original number) to 32, since `main` has since taken 29–31 for the features described above.

## Problem

The ask is unchanged from the first draft: move the worker's Claude calls onto `Microsoft.Extensions.AI`'s `IChatClient` so a future model or provider swap is a registration change, and pick up the middleware pipeline (telemetry, logging) along the way, while staying on Anthropic/Foundry as the only configured provider for now.

Two spikes (this session, request-capture harnesses against the real SDK) settled what's safe and what isn't. The first (unchanged from the original draft) found that the official `Anthropic` NuGet package ships `AsIChatClient()` in the box — no third-party dependency — and that `ChatOptions.Reasoning.Effort` and `HostedWebSearchTool` are silently wrong for Anthropic (legacy `thinking.budget_tokens`; a downgraded search tool with no `MaxUses`), while `ChatOptions.ResponseFormat` and `ChatOptions.RawRepresentationFactory` are correct and — critically — **merge** rather than one clobbering the other.

The second spike, run against the now-current code above, found a third gap that the first draft had no reason to look for, because grounding and multi-value `stop_reason` handling didn't exist yet:

| What the code needs | Portable `IChatClient` surface | Verdict |
|---|---|---|
| Distinguish `end_turn` from `pause_turn` (`IsIncomplete`, used by 4 of 5 providers) | `ChatResponse.FinishReason` | ❌ **collapses both to `"stop"`** — verified by feeding the adapter synthetic `end_turn` and `pause_turn` response bodies; both produced `FinishReason: stop`. A check written against the portable value would silently treat a tool-loop-exhausted response as a normal completion — exactly the "silent no-op that looks like nothing found" bug plan 29 §Problem diagnosed and fixed. `max_tokens` → `length` and `refusal` → `content_filter` *are* portable and distinguishable, but since one of the four values isn't, every provider has to read the raw string uniformly rather than trusting the portable value for three cases and falling back for one. |
| Read `web_search_tool_result`/`web_fetch_tool_result` content blocks (`RetrievedUrls`, route enrichment's grounding) | — no portable equivalent exists; these are Anthropic server-tool-result shapes, not part of the portable content-type set | ✅ but only via `ChatResponse.RawRepresentation` — verified populated as `Anthropic.Models.Messages.Message` on every response shape tested, **with all content blocks intact** (a synthetic 3-block response — `server_tool_use`, `web_search_tool_result`, `text` — came back with `RawRepresentation.Content.Count == 3`, each block still its real SDK type). `RetrievedUrls(response.Content)` needs no logic change, only a different source: `((Message)response.RawRepresentation).Content` instead of a raw client's `response.Content`. |
| Skip `effort` entirely on a model that 400s on it (`ClaudeModelCapabilities.SupportsEffort`), without losing the portable JSON-schema format already on the same request | `RawRepresentationFactory` fragment with `OutputConfig` deliberately unset | ✅ verified: a fragment carrying `Tools`/`Model`/`MaxTokens` but no `OutputConfig`, combined with a portable `ChatOptions.ResponseFormat`, produced `output_config.format` with no `effort` key — the portable format survives an *absent* raw `OutputConfig` exactly as it survived a *present* one carrying only effort+tools in the first spike. |

So: everything expressible portably still moves to `IChatClient` — messages, vision, JSON-schema output, model id, max tokens, DI, telemetry. But the raw-passthrough seam now has to cover **three** things, not two: the request-side effort/tools fragment from the first draft, plus a response-side raw read for `stop_reason` and for the search/fetch result blocks. All three are used by more than one provider now, so this plan builds them as shared helpers once, rather than per-provider.

## Design

### 1. Package changes — `Acordanza.Worker` only

Unchanged from the first draft. Add `Microsoft.Extensions.AI` 10.5.1 (not just `.Abstractions`, already pulled in transitively) for `AddChatClient`, `UseOpenTelemetry`, `UseLogging`. `Anthropic.Foundry` stays — `.AsIChatClient(...)` wraps the client `FoundryClient.Create` already builds, not a replacement for it. No change to `Acordanza.Api` or `Acordanza.Core`.

### 2. `FoundryClient` grows one method, keeps the other

Unchanged from the first draft:

```csharp
public static IChatClient CreateChatClient(IConfiguration configuration) =>
    Create(configuration).AsIChatClient(defaultModelId: null, defaultMaxOutputTokens: null);
```

Both defaults stay `null` — with five providers now spanning three models (`claude-opus-5`, `claude-sonnet-5`, `claude-haiku-4-5`) and five different `MaxTokens` values (8192, 4096, 1024, 512, 256), a shared default would be actively misleading. **Every `ChatOptions` built anywhere in this plan must set `ModelId` and `MaxOutputTokens` explicitly** — `max_tokens` is a required field on the wire, and with no default configured on the client, an omitted one either throws client-side or is sent absent, neither of which is caught by anything short of the smoke run in §Testing. Worth a code-review line item, not just a design note.

### 3. DI: one `IChatClient`, registered once, carrying the middleware pipeline

Unchanged from the first draft, except it now serves five providers instead of two:

```csharp
services.AddChatClient(sp => FoundryClient.CreateChatClient(sp.GetRequiredService<IConfiguration>()))
    .UseOpenTelemetry(sourceName: "acordanza-enrichment-worker")
    .UseLogging();
```

One `IChatClient`, one place the middleware pipeline is wired, five constructor-injected consumers. The tracer-provider setup in `AddEnrichmentWorker` needs `.AddSource("Experimental.Microsoft.Extensions.AI")` — `OpenTelemetryChatClient`'s activity source — alongside its existing `AddNpgsql`/`AddHttpClientInstrumentation` registrations, or every one of the five providers' spans is generated and silently dropped.

### 4. The raw-passthrough seam, generalised to three concerns and five callers

New file, `Acordanza.Worker/Services/AnthropicChatExtensions.cs`, replacing the single-purpose `WithEffort` helper the first draft proposed:

```csharp
// Three things below have no correct portable ChatOptions mapping today (see plan 32
// §Problem's table): setting output_config.effort (guarded per-model, since
// ChatOptions.Reasoning.Effort serializes as legacy thinking.budget_tokens, and the
// guard itself — ClaudeModelCapabilities — predates this plan and stays the single
// source of truth for which models accept the field); declaring the exact Anthropic
// tool variant, not HostedWebSearchTool's downgrade; and reading stop_reason /
// server-tool-result content blocks off the raw response, since
// ChatResponse.FinishReason collapses pause_turn into the same value as end_turn.
public static class AnthropicChatExtensions
{
    // Called by every provider, whether or not it ends up contributing anything — a
    // haiku-pinned provider's fragment carries no OutputConfig and no Tools, and is
    // effectively a no-op merge (verified: an absent raw OutputConfig doesn't clobber
    // a portable ResponseFormat already on the same options). Uniform call sites mean
    // a future model swap that changes the effort-support answer, or adds a tool to a
    // call that has none today, touches one call site instead of a bespoke one.
    public static ChatOptions WithAnthropicRequest(
        this ChatOptions options, string modelId, Effort? effort, params ToolUnion[] tools)
    {
        options.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Messages = [],
            OutputConfig = effort is { } e && ClaudeModelCapabilities.SupportsEffort(modelId)
                ? new OutputConfig { Effort = e }
                : null,
            Tools = tools.Length > 0 ? [.. tools] : null,
        };
        return options;
    }

    // The read-side half. Every provider with an IsIncomplete check (§5–§9) calls this
    // instead of a portable FinishReason comparison, because pause_turn and end_turn
    // are indistinguishable through FinishReason (verified: both produce "stop").
    public static string? RawStopReason(this ChatResponse response) =>
        (response.RawRepresentation as Message)?.StopReason?.Raw();

    // Route enrichment's grounding needs the full block list, not just the stop
    // reason — same raw object, a different projection of it.
    public static IReadOnlyList<ContentBlock> RawContent(this ChatResponse response) =>
        (response.RawRepresentation as Message)?.Content ?? [];
}
```

`ClaudeRouteEnrichmentProvider.IsIncomplete(string?)` — the existing allow-list-of-one check (`end_turn` is the only stop reason with a trustworthy payload) — stays exactly where it is and exactly as written; every provider keeps calling it, now as `IsIncomplete(response.RawStopReason())` instead of `IsIncomplete(response.StopReason?.Raw())`. No behavioural change, only a different way to reach the same string.

`RetrievedUrls(IEnumerable<ContentBlock> content)` — route enrichment's grounding — is untouched in every line of its own logic; the one call site changes from `RetrievedUrls(response.Content)` (the raw SDK response) to `RetrievedUrls(response.RawContent())` (the `IChatClient` response's raw projection). `EnrichmentGroundingTests` needs **no changes at all** — it feeds `RetrievedUrls` and `Ground` synthetic `ContentBlock` JSON directly and never touches a provider or a transport, so it's already decoupled from this migration.

### 5. `ClaudeRouteEnrichmentProvider`

- Constructor: `(IChatClient chatClient, ILogger<ClaudeRouteEnrichmentProvider> logger)` — drops `IConfiguration` and the `_client = FoundryClient.Create(configuration)` line.
- Messages: `[new(ChatRole.User, prompt)]` — `BuildPrompt` is untouched (it's pure string formatting, no SDK types).
- ```csharp
  var options = new ChatOptions
  {
      ModelId = Model, // "claude-opus-5", unchanged constant
      MaxOutputTokens = 8192,
      ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
  }.WithAnthropicRequest(Model, Effort.High,
      new WebSearchTool20260209 { MaxUses = 8, UserLocation = ..., BlockedDomains = BlockedSearchDomains },
      new WebFetchTool20260209 { MaxUses = 3, MaxContentTokens = 20000 });
  var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
  ```
- `stop_reason`: `if (IsIncomplete(response.RawStopReason())) { ...return Completed: false... }` — same branch, same log line, different source for the string (§4).
- Parsing: `JsonSerializer.Deserialize<EnrichmentPayload>(response.Text, JsonOptions)` replaces the `response.Content.Select(b => b.Value as TextBlock)...` concatenation.
- Grounding: `RetrievedUrls(response.RawContent())` replaces `RetrievedUrls(response.Content)` (§4) — everything downstream of that call (`Ground`, the `topoPageUrl` check, the logging line) is unchanged.
- `LogUsage`: `response.Usage.InputTokenCount`/`.OutputTokenCount` (portable) for the two token counts; `response.RawContent()`'s parent `Message`'s `Usage.ServerToolUse?.WebSearchRequests` has no portable equivalent, so this one field stays sourced from the raw response — cheapest as `(response.RawRepresentation as Message)?.Usage.ServerToolUse?.WebSearchRequests ?? 0`, inline, since it's used nowhere else.
- The `TEMPORARY`-turned-permanent model comment, `BlockedSearchDomains`, and `BuildPrompt` are untouched — this plan swaps transport, not content.

### 6. `ClaudeTopoVerificationProvider`

- Same constructor pattern; drops `IConfiguration`.
- Image content is portable: `new DataContent(context.ImageBytes, context.ContentType)` replaces the hand-built `ImageBlockParam`/`Base64ImageSource`.
- ```csharp
  ChatMessage message = new(ChatRole.User,
      [new DataContent(context.ImageBytes, context.ContentType), new TextContent(prompt)]);
  var options = new ChatOptions
  {
      ModelId = Model, // "claude-haiku-4-5"
      MaxOutputTokens = 256,
      ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
  }.WithAnthropicRequest(Model, Effort.Low); // no tools; effort silently skipped — haiku-4-5 isn't in ClaudeModelCapabilities' set
  ```
- **This provider does not check `stop_reason` today** (`IsIncomplete` isn't called in `IsTopoAsync` on `main`) — that's an existing inconsistency with the other four providers, not something this plan introduces or is asked to fix. Carried forward unchanged; a `pause_turn`/`refusal` response still falls through to `IsNullOrWhiteSpace(json)` returning `false`, exactly as today.
- Parse `TopoVerificationPayload` from `response.Text`, unchanged in shape.

### 7. `ClaudeTopoImportGateProvider`

- Same constructor pattern, plus the existing `TopoImportFewShotSet` dependency, unchanged.
- The interleaved few-shot content — alternating instructional text and labelled example images, then the image to classify — becomes an ordered `List<AIContent>` built the same way the raw `List<ContentBlockParam>` is built today, one `TextContent`/`DataContent` per entry instead of one `Text(...)`/`Image(...)` call. `ClaudeTopoImportGateProvider.Text(string)`/`.Image(byte[], string)` — the two static helpers this provider exposes today, reused by `ClaudeTopoExtractionProvider` (§8) — go away entirely: `new TextContent(...)`/`new DataContent(...)` are public `Microsoft.Extensions.AI` constructors, so there's nothing left for a local wrapper to do. One small deletion, not a rename.
- ```csharp
  var options = new ChatOptions
  {
      ModelId = Model, // "claude-haiku-4-5"
      MaxOutputTokens = 512,
      ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
  }.WithAnthropicRequest(Model, Effort.Low); // effort skipped, same as §6
  var response = await chatClient.GetResponseAsync(new ChatMessage(ChatRole.User, content), options, cancellationToken);
  ```
- `stop_reason`: `IsIncomplete(response.RawStopReason())` → `Completed: false`, same branch as today.
- `TopoImportFewShotSet` itself (the blob-storage loading, the once-per-process cache, `MaxPerLabel`) is untouched — it produces `TopoFewShotExample` records (bytes + content type + label), and only the call site that turns those into request content changes shape.

### 8. `ClaudeTopoExtractionProvider`

- Same constructor pattern.
- Content: `[new DataContent(context.ImageBytes, context.ContentType), new TextContent(BuildPrompt(context))]` — the cross-provider call into `ClaudeTopoImportGateProvider.Image`/`.Text` (§7) is gone; this is now self-contained.
- ```csharp
  var options = new ChatOptions
  {
      ModelId = Model, // "claude-sonnet-5"
      MaxOutputTokens = 1024,
      ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
  }.WithAnthropicRequest(Model, Effort.Low); // sonnet-5 supports effort — this one actually sets it
  ```
- `stop_reason`: `IsIncomplete(response.RawStopReason())` (calls `ClaudeRouteEnrichmentProvider.IsIncomplete` exactly as today) → `Completed: false`.
- Parse `TopoExtractionResult`/`ExtractedRoute` from `response.Text`, unchanged in shape.

### 9. `ClaudePageExtractionProvider`

The simplest of the five to port — no vision content, no tools, just a text prompt:

- Same constructor pattern.
- ```csharp
  var options = new ChatOptions
  {
      ModelId = Model, // "claude-sonnet-5"
      MaxOutputTokens = 4096,
      ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
  }.WithAnthropicRequest(Model, Effort.Low); // no tools — "we did the fetching", unchanged rationale
  var response = await chatClient.GetResponseAsync(BuildPrompt(context), options, cancellationToken);
  ```
- `stop_reason`: `IsIncomplete(response.RawStopReason())` → `Completed: false`, `max_tokens`/`refusal` → `Failed` downstream in `WebPageReader`, unchanged.
- Parse `PageExtractionResult`/`ExtractedPageRoute` from `response.Text`, unchanged in shape. `BuildPrompt`, including the prompt-injection framing around `<page-text>`, is untouched — pure string formatting.

### 10. No-op providers — untouched

`NoOpRouteEnrichmentProvider`, `NoOpTopoVerificationProvider`, `NoOpTopoExtractionProvider`, `NoOpTopoImportGateProvider`, `NoOpPageExtractionProvider` take no client today and need none after this — all five stay exactly as written.

### 11. Test surface: one consolidation, two files untouched

- **`ClaudeModelCapabilitiesTests` is rewritten, not just updated.** Today it calls two providers' `internal static MessageCreateParams BuildRequestParams(...)` directly and inspects `.OutputConfig` for the `effort` key — a pattern that only ever covered `ClaudeRouteEnrichmentProvider` and `ClaudeTopoVerificationProvider`, leaving the three newer providers' effort guard untested by this file. Since all five now build their request through the one shared `WithAnthropicRequest` (§4), the test collapses to one parameterised case against the helper itself:

  ```csharp
  [Theory]
  [InlineData("claude-opus-5", true)]
  [InlineData("claude-sonnet-5", true)]
  [InlineData("claude-haiku-4-5", false)]
  public void WithAnthropicRequest_never_sets_effort_on_a_model_that_rejects_it(string model, bool expectEffort)
  {
      var options = new ChatOptions().WithAnthropicRequest(model, Effort.Medium);
      var request = (MessageCreateParams)options.RawRepresentationFactory!(null!)!;
      var hasEffort = JsonDocument.Parse(JsonSerializer.Serialize(request.OutputConfig))
          .RootElement.TryGetProperty("effort", out _);
      hasEffort.ShouldBe(expectEffort);
  }
  ```

  This is a net gain, not just a port: one test now covers all five call sites' effort behaviour instead of two, because they all go through the same helper. The `SupportsEffort_matches_the_known_table` theory (asserting `ClaudeModelCapabilities` itself) is untouched — that table isn't part of this migration.
- **`EnrichmentGroundingTests` needs no changes** (§4) — it exercises `Canonical`/`Ground`/`RetrievedUrls` against synthetic `ContentBlock` JSON, never a provider or a transport.
- **`WorkerConfigurationTests`** resolves the whole `AddEnrichmentWorker` DI graph and asserts it doesn't throw; it needs no content change, but is the fastest signal if the `AddChatClient`/`UseOpenTelemetry` registration (§3) is missing a dependency.

### 12. What does not change

- `IRouteEnrichmentProvider`, `ITopoVerificationProvider`, `ITopoImportGateProvider`, `ITopoExtractionProvider`, `IPageExtractionProvider` — every consumer (`RouteEnrichmentService`, `TopoImageReader`, `WebPageReader`, and every test fake) depends only on these interfaces and never constructs a `Claude*` implementation directly.
- `FoundryClient.Scopes`, `AnthropicFoundryIdentityTokenCredentials`, the managed-identity-client-id reasoning — untouched; only what's built on top of the resulting `IAnthropicClient` changes.
- `ClaudeModelCapabilities` and its table — reused as the single source of truth for the effort guard, not duplicated or reimplemented.
- Every prompt string (`BuildPrompt` in all five providers), every JSON schema, every model id, every `MaxTokens` value — this plan is a transport swap.
- `TopoImportFewShotSet`'s blob-loading and caching logic (§7).
- `Acordanza.Api`, `Acordanza.Core`, Terraform, Docker, CI — no Claude calls outside the worker, no new config keys or secrets.

## Files touched

| File | Change |
|---|---|
| `backend/Acordanza.Worker/Acordanza.Worker.csproj` | `+ PackageReference Microsoft.Extensions.AI 10.5.1` |
| `backend/Acordanza.Worker/Services/IRouteEnrichmentProvider.cs` | `FoundryClient.CreateChatClient` added; `ClaudeRouteEnrichmentProvider` (§5) and `ClaudeTopoVerificationProvider` (§6) rewritten onto `IChatClient` |
| `backend/Acordanza.Worker/Services/ITopoImportGateProvider.cs` | `ClaudeTopoImportGateProvider` rewritten (§7); `Text`/`Image` static helpers deleted |
| `backend/Acordanza.Worker/Services/ITopoExtractionProvider.cs` | `ClaudeTopoExtractionProvider` rewritten (§8), no longer calling into `ITopoImportGateProvider.cs` |
| `backend/Acordanza.Worker/Services/IPageExtractionProvider.cs` | `ClaudePageExtractionProvider` rewritten (§9) |
| `backend/Acordanza.Worker/Services/AnthropicChatExtensions.cs` | new — `WithAnthropicRequest`, `RawStopReason`, `RawContent` (§4), replacing the first draft's single-purpose `WithEffort` |
| `backend/Acordanza.Worker/Services/ServiceCollectionExtensions.cs` | `AddChatClient(...).UseOpenTelemetry(...).UseLogging()` registration + tracer-provider `AddSource` (§3) |
| `backend/Acordanza.Worker.Tests/Services/ClaudeModelCapabilitiesTests.cs` | rewritten against `WithAnthropicRequest` (§11) — now covers all five models/providers, not two |
| `backend/CLAUDE.md` | Route enrichment section: describe the transport as `IChatClient` + the shared `AnthropicChatExtensions` seam, not five independent raw `Messages.Create` call sites |

## Steps

1. Add the `Microsoft.Extensions.AI` package reference.
2. Add `FoundryClient.CreateChatClient`, the `AddChatClient`/`UseOpenTelemetry`/`UseLogging` registration, and the tracer-provider `AddSource` call.
3. Add `AnthropicChatExtensions` (`WithAnthropicRequest`, `RawStopReason`, `RawContent`).
4. Rewrite `ClaudeRouteEnrichmentProvider` (§5) — the largest single change, since it's the only one carrying tools and grounding. Confirm `EnrichmentGroundingTests` passes unmodified.
5. Rewrite `ClaudeTopoVerificationProvider` (§6).
6. Rewrite `ClaudeTopoImportGateProvider` (§7), deleting its now-unused `Text`/`Image` helpers.
7. Rewrite `ClaudeTopoExtractionProvider` (§8).
8. Rewrite `ClaudePageExtractionProvider` (§9).
9. Rewrite `ClaudeModelCapabilitiesTests` (§11).
10. `dotnet build` — should be clean; every interface and every caller of them is unchanged, so this is contained inside the five provider classes, the new helper file, and one test file.
11. `dotnet test Acordanza.Worker.Tests` (both tiers, Docker up for integration) — expect no failures outside the intentional `ClaudeModelCapabilitiesTests` rewrite.
12. One real smoke run against Foundry per provider family — see §Testing, since this repo's own precedent (plans 29–31) is that none of these five call shapes has ever shipped without one.

## Testing

- `dotnet test Acordanza.Worker.Tests` — the acceptance-predicate and golden-set tests (`RouteEnrichmentServiceTests`, `RouteImportServiceTests`, `UrlImportServiceTests`, `RouteImportAcceptanceTests`, `WebPageAcceptanceTests`, `*GoldenSetTests`) all construct their subject against a **fake** `IRouteEnrichmentProvider`/`ITopoExtractionProvider`/`ITopoImportGateProvider`/`IPageExtractionProvider`, never a `Claude*` implementation — none of them should need a code change. A failure in one of these means something in §5–§9 leaked a provider-internal detail past the interface it's supposed to hide behind.
- **Manual smoke run is load-bearing, as it was in the first draft, now across three model/tool combinations instead of one:**
  1. **Route enrichment** (`claude-opus-5`, both tools) — trigger via `enqueue-enrichment`. Confirm: `output_config.effort` present (not `thinking.budget_tokens`); both `web_search_20260209` and `web_fetch_20260209` present with the right `max_uses`/`user_location`/`blocked_domains`/`max_content_tokens`; grounding still drops an ungrounded link (check the `enrichment grounding` log line's `returned`/`grounded` counts move the same way they did before this change); a `pause_turn` or `refusal` response (hard to force on demand, but worth watching for over a few real runs) still produces `Completed: false` and leaves `EnrichedAt` null rather than silently completing.
  2. **Topo import** (`claude-haiku-4-5` gate, `claude-sonnet-5` extraction) — submit a real topo photo via `/my-acordanza/my-imports`. Confirm the gate's few-shot examples still load and the interleaved content order is preserved (a shuffled few-shot block would still "work" in the sense of not throwing, but would silently degrade the classifier — compare the gate's verdict on a known example against its verdict before this change).
  3. **URL import** (`claude-sonnet-5`, no tools) — submit a real page. Confirm no tools are sent at all (the whole point of §9's design — the model must not be able to return a URL it wasn't handed), and that `stop_reason` handling still routes a bad page to `Discarded` and a pipeline failure to `Failed`.
  4. Confirm `response.Usage.InputTokenCount`/`OutputTokenCount` in the logs match what the previous raw-SDK logging reported for a comparable run, and that route enrichment's `WebSearchRequests` count is still present (the one field that stays sourced from `RawRepresentation` in §5).
  5. OpenTelemetry spans for all three call shapes arrive in Grafana Cloud tagged `service.name = acordanza-enrichment-worker` (§3).

## Later, not in this plan

- Actually configuring a second, non-Anthropic provider — this plan is the enabling step; swapping or A/B-ing a model across any of the five providers is deliberately left as a follow-up now that it's a registration edit instead of five provider rewrites.
- Fixing `ClaudeTopoVerificationProvider`'s missing `stop_reason` check (§6) — an existing inconsistency with the other four providers, out of scope for a transport migration. Worth its own small PR if it's ever going to be fixed.
- `UseDistributedCache`/`UseFunctionInvocation` middleware — no current use case across any of the five providers (no client-executed tools anywhere in this worker; every prompt is per-route/per-image/per-page, so caching identical requests isn't meaningful).
- Touching `Acordanza.Api` or `Acordanza.Core` — neither calls Claude.
