using System.Globalization;
using System.Text.Json;
using Acordanza.Core.Services;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Foundry;
using Anthropic.Models.Messages;
using Azure.Identity;
using Microsoft.Extensions.AI;

namespace Acordanza.Worker.Services;

// Everything about the route that either narrows the search or lets a candidate page be
// *rejected* (plan 29 §2). The first ascensionists are the point: «La Loca» is a hopeless
// query, «La Loca» plus an aperturista's name is a precise one, and the names were sitting
// on the row unused. Length/orientation/gear are weak search terms but cheap rejection
// signals — a page about a 300 m multi-pitch is not about our 22 m single-pitch of the
// same name. Lat/Lng come from Area.Centroid* when the sector has a polygon drawn, and
// disambiguate the breadcrumb (Spain has more than one "Peña Rubia").
public record RouteEnrichmentContext(
    string Name,
    string Grade,
    string AreaBreadcrumb,
    IReadOnlyList<string> FirstAscensionists,
    int Length,
    string? Orientation,
    string? Gear,
    double? Lat,
    double? Lng);

// One candidate page. Every field past Url exists to make a wrong answer more expensive to
// produce than a blank one (plan 29 §2) — in particular Evidence, a quote naming both the
// route and its sector, which a model can't cheaply fabricate for a page about a different
// route that happens to share the name. RouteEnrichmentService is what enforces them.
public record EnrichmentLink(string Url, string? Title, string? Kind, string? Evidence, string? Confidence);

// Completed=false means the run ended on a warning path (a stop_reason that isn't end_turn,
// i.e. pause_turn/max_tokens/refusal) rather than on a real answer. RouteEnrichmentService
// leaves EnrichedAt null in that case, so re-pressing the button just works instead of
// needing force=true to get past a failure that was environmental.
public record EnrichmentResult(
    List<EnrichmentLink> Links,
    string? TopoPageUrl,
    string? TopoImageUrl,
    bool Completed = true);

// The test seam (see RouteEnrichmentTests): everything network- and Claude-shaped hides
// behind this so integration tests fake it and never touch the real API.
public interface IRouteEnrichmentProvider
{
    Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken);
}

public record TopoVerificationContext(string Name, string Grade, string AreaBreadcrumb, byte[] ImageBytes, string ContentType);

public interface ITopoVerificationProvider
{
    Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken);
}

// No Anthropic:FoundryResource configured — enrichment is skipped entirely rather than
// failing, same philosophy as ConsoleEmailSender: the plumbing (queue, endpoint, button)
// stays exercisable locally without an Azure AI Foundry resource or `az login` session.
public class NoOpRouteEnrichmentProvider(ILogger<NoOpRouteEnrichmentProvider> logger) : IRouteEnrichmentProvider
{
    public Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("enrichment skipped: no Anthropic:FoundryResource configured");
        return Task.FromResult(new EnrichmentResult([], null, null));
    }
}

public class NoOpTopoVerificationProvider : ITopoVerificationProvider
{
    public Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

// All five Claude providers in this worker reach Foundry the same way — one construction, in
// one place, because getting either half of it wrong fails only at the first real call, in
// production, and both halves once were wrong:
//
//   - Scopes must be non-empty. AnthropicFoundryIdentityTokenCredentials' scopes parameter
//     defaults to null and only *then* does the SDK substitute Foundry's audience; an empty
//     array is stored verbatim, so the credential asks for a token with no scopes and MSAL
//     throws "Value cannot be null. (Parameter 'resource')" on every enrichment. Passing the
//     audience explicitly (rather than relying on the null default) also makes it assertable
//     from a test — see WorkerConfigurationTests.
//   - The managed identity's client id is not optional in production. The Container Apps Job
//     carries a user-assigned identity only (infra/terraform/container_app_job.tf), so without
//     the client id ManagedIdentityCredential asks IMDS for a system-assigned identity that
//     doesn't exist. It's absent locally, where DefaultAzureCredential falls through to the
//     contributor's own az login session — which is why it's read, not required.
public static class FoundryClient
{
    // The audience Foundry's Anthropic endpoint issues tokens for — the same value the SDK
    // would default to, written here so it can't silently become the empty set again.
    public static readonly string[] Scopes = ["https://ai.azure.com/.default"];

    public static IAnthropicClient Create(IConfiguration configuration)
    {
        var resource = configuration["Anthropic:FoundryResource"]
            ?? throw new InvalidOperationException("Missing Anthropic:FoundryResource configuration");
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = configuration["Anthropic:ManagedIdentityClientId"]
        });
        return new AnthropicFoundryClient(
            new AnthropicFoundryIdentityTokenCredentials(credential, resource, Scopes));
    }

    // What the five providers actually inject (plan 32): the same Foundry client above,
    // wrapped in Microsoft.Extensions.AI's portable IChatClient so a future model or
    // provider swap is a registration change — and so the middleware pipeline
    // (AddChatClient(...).UseOpenTelemetry().UseLogging(), see ServiceCollectionExtensions)
    // wraps every call site at once.
    //
    // Both defaults stay null on purpose. The five providers span two models and four
    // different token budgets, so a shared default would be actively misleading rather than
    // convenient; every ChatOptions built in this worker sets ModelId and MaxOutputTokens
    // explicitly, and AnthropicChatExtensions.WithAnthropicRequest throws if the latter is
    // missing rather than letting a request go out without a max_tokens the wire requires.
    public static IChatClient CreateChatClient(IConfiguration configuration) =>
        Create(configuration).AsIChatClient(defaultModelId: null, defaultMaxOutputTokens: null);
}

// Search-and-extract: one chat call with the server-side web search tool and structured
// outputs, reached through Azure AI Foundry (Hosted on Anthropic — structured outputs and
// server-side tools 400 on Azure-hosted deployments) so the worker authenticates with its
// managed identity instead of a stored API key. The transport is Microsoft.Extensions.AI's
// IChatClient (plan 32); what can't be said portably — the tool variants, effort, and the
// raw stop_reason/tool-result reads below — goes through AnthropicChatExtensions.
public class ClaudeRouteEnrichmentProvider : IRouteEnrichmentProvider
{
    // Must equal the Foundry deployment name in infra/terraform/ai_foundry.tf —
    // the SDK sends this string through as the model id, so the two move
    // together or every request 404s. A named constant (rather than inlining
    // the literal below) so ClaudeModelCapabilitiesTests can assert this exact
    // model's effort support without duplicating the string.
    //
    // Was claude-sonnet-4-5, for a quota reason that still holds (this subscription
    // has no GlobalStandard quota for the sonnet family at all — which is also why
    // the route-import extractions land on this same deployment rather than on
    // plans 30/31's claude-sonnet-5) but no longer binds, opus-5 quota being
    // available. Two
    // capabilities this call is built around only exist from here: the
    // WebSearchTool20260209 declared below ran without its dynamic filtering
    // (server-side filtering of results before they reach the context window,
    // 4.6-and-later only), and output_config.effort was skipped entirely by
    // the ClaudeModelCapabilities guard, sonnet-4-5 being a model that 400s
    // on it. Both apply now.
    internal const string Model = "claude-opus-5";

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeRouteEnrichmentProvider> _logger;

    // An array of objects rather than plan 18's bare string array (plan 29 §2): a bare
    // array makes filling it the path of least resistance and costs nothing to get wrong.
    // Every field here is a toll on a wrong answer — `evidence` above all, since quoting a
    // page that names both the route and its sector is a much bigger fabrication step than
    // inventing a plausible URL was.
    private static readonly JsonElement ResultSchema = JsonSerializer
        .Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "links": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "url": { "type": "string" },
                  "title": { "type": "string" },
                  "kind": { "type": "string", "enum": ["first-ascent", "video", "guidebook", "topo", "sector", "other"] },
                  "evidence": { "type": "string" },
                  "confidence": { "type": "string", "enum": ["high", "medium", "low"] }
                },
                "required": ["url", "title", "kind", "evidence", "confidence"],
                "additionalProperties": false
              }
            },
            "topoPageUrl": { "type": ["string", "null"] },
            "topoImageUrl": { "type": ["string", "null"] }
          },
          "required": ["links", "topoPageUrl", "topoImageUrl"],
          "additionalProperties": false
        }
        """);

    // Hosts that generate confident-looking noise: social aggregators that restate route
    // databases without adding anything checkable. Deliberately a *blocklist*, not an
    // allowlist — the best source for a Spanish first-ascent story is a long-tail personal
    // blog on blogspot/wordpress, and any allowlist we could write would exclude exactly
    // those while admitting the aggregators. The API treats the two as mutually exclusive,
    // so this is a real choice, not a default.
    private static readonly List<string> BlockedSearchDomains =
    [
        "pinterest.com", "pinterest.es", "facebook.com", "instagram.com",
        "x.com", "twitter.com", "tiktok.com"
    ];

    public ClaudeRouteEnrichmentProvider(IChatClient chatClient, ILogger<ClaudeRouteEnrichmentProvider> logger)
    {
        _logger = logger;
        _chatClient = chatClient;
    }

    public async Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(context);

        _logger.LogInformation("enrichment calling the chat client for route={RouteName}", context.Name);
        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)], BuildOptions(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "enrichment chat call failed for route={RouteName}", context.Name);
            throw;
        }
        _logger.LogInformation("enrichment chat call returned for route={RouteName}", context.Name);

        LogUsage(response, context.Name);

        // Anything but end_turn means there is no trustworthy final payload: a server-side
        // tool loop that hit its iteration cap returns pause_turn with no text block (which
        // used to parse to an empty result and still mark the route enriched), max_tokens
        // returns truncated JSON (which used to throw out of Deserialize and burn all five
        // queue redeliveries on an error that recurs identically), and Opus 5's elevated
        // safety classifiers can return refusal on an HTTP 200. All three are the same
        // warning path, and all three leave EnrichedAt null so re-pressing the button works.
        //
        // Read off the raw response, not ChatResponse.FinishReason: the portable value
        // collapses pause_turn and end_turn into the same "stop", which would turn the
        // tool-loop-exhausted case back into the silent no-op plan 29 fixed. See
        // AnthropicChatExtensions.
        var stopReason = response.RawStopReason();
        if (IsIncomplete(stopReason))
        {
            _logger.LogWarning(
                "enrichment run incomplete route={RouteName} stopReason={StopReason}: no links applied",
                context.Name, stopReason);
            return new EnrichmentResult([], null, null, Completed: false);
        }

        var json = response.Text;

        if (string.IsNullOrWhiteSpace(json))
        {
            return new EnrichmentResult([], null, null);
        }

        _logger.LogInformation("enrichment raw response route={RouteName} json={Json}", context.Name, json);

        var parsed = JsonSerializer.Deserialize<EnrichmentPayload>(json, JsonOptions);
        var returned = (parsed?.Links ?? [])
            .Where(l => l.Url is not null)
            .Select(l => new EnrichmentLink(l.Url!, l.Title, l.Kind, l.Evidence, l.Confidence))
            .ToList();

        // §1, the highest-leverage change in plan 29 and the only one that is pure
        // deterministic C#: the response carries the server-side tool traffic as content
        // blocks alongside the text, so we know exactly which pages the model actually saw.
        // A URL that isn't among them was reconstructed from memory, and a hallucinated URL
        // becoming zero links is the failure mode we want.
        // The blocks come off the raw response for the same reason as the stop reason above:
        // web_search_tool_result/web_fetch_tool_result are Anthropic server-tool shapes with
        // no portable equivalent. RetrievedUrls itself is unchanged.
        var retrieved = RetrievedUrls(response.RawContent());
        var grounded = Ground(returned, retrieved);

        // topoPageUrl is grounded the same way — it's a page, so the model must have seen
        // it. topoImageUrl deliberately is *not*: an <img src> spotted inside a fetched page
        // is by construction not itself a retrieved URL, and the download + vision check
        // downstream is what filters that one.
        var topoPageUrl = parsed?.TopoPageUrl is { } page && retrieved.Contains(Canonical(page)) ? page : null;

        _logger.LogInformation(
            "enrichment grounding route={RouteName} returned={Returned} retrieved={Retrieved} grounded={Grounded} topoPageGrounded={TopoPage}",
            context.Name, returned.Count, retrieved.Count, grounded.Count, topoPageUrl is not null);

        return new EnrichmentResult(grounded, topoPageUrl, parsed?.TopoImageUrl);
    }

    // Allow-list of one: end_turn is the only stop reason that comes with a payload worth
    // parsing. A missing stop_reason (streaming-shaped responses) is not treated as a
    // failure — the payload check below covers it.
    internal static bool IsIncomplete(string? stopReason) => stopReason is not null && stopReason != "end_turn";

    internal static List<EnrichmentLink> Ground(IEnumerable<EnrichmentLink> returned, HashSet<string> retrieved) =>
        returned.Where(l => retrieved.Contains(Canonical(l.Url))).ToList();

    private static string BuildPrompt(RouteEnrichmentContext context)
    {
        var openers = context.FirstAscensionists.Count > 0
            ? string.Join(", ", context.FirstAscensionists)
            : "unknown";
        var coordinates = context is { Lat: { } lat, Lng: { } lng }
            ? $"approximately {lat.ToString("F4", CultureInfo.InvariantCulture)}, {lng.ToString("F4", CultureInfo.InvariantCulture)}"
            : "unknown";

        // The negative contract is most of this prompt, and is the part plan 18's never
        // stated: what a miss looks like, and that missing is the *expected* outcome.
        return $"""
            You're researching one specific climbing route so a private climbing log can link
            to pages about it. Everything known about the route:

            - Name: {context.Name}
            - Grade: {context.Grade}
            - Sector (outermost area first): {context.AreaBreadcrumb}
            - Approximate coordinates: {coordinates}
            - First ascensionists (aperturistas): {openers}
            - Length: {context.Length} m
            - Orientation: {context.Orientation ?? "unknown"}
            - Gear / style: {context.Gear ?? "unknown"}

            Search for pages specifically about THIS route — first-ascent stories, videos,
            guidebook pages, topo publications — or, failing that, about this exact sector.
            Most sources are in Spanish, so search in Spanish; the aperturistas' names are
            usually the strongest query term you have. Open the promising results with the
            web fetch tool before deciding: a page you have read is the only kind you may
            return.

            Rejecting is the job here, not finding:

            - Most routes in this log are obscure and have nothing written about them. An
              empty links list is the correct and common answer, not a failure. Do not pad it.
            - A page about a different route that happens to share this name, in a different
              area, is a miss, not a partial hit. Discard it.
            - Use the metadata above as rejection criteria: a page describing a route of a
              very different length, style, grade or geography is about a different route.
            - Never return a URL you did not open or receive in a search result. Do not
              reconstruct, complete or guess a URL — a URL you are not certain of is a miss.
            - `evidence` must be copied verbatim from the page (not paraphrased) and must name
              BOTH this route and its sector or crag. If you cannot quote such a sentence, the
              page does not qualify: leave it out.
            - Set `confidence` to "high" only when that quote alone would convince a sceptical
              reader that the page is about this route. Anything less is "medium" or "low",
              and anything below "high" is discarded downstream.

            If one of the pages publishes a topo (route-line diagram or photo) for this route,
            set `topoPageUrl` to that page's URL. Set `topoImageUrl` only if you actually saw
            the topo image's own URL on that page; if you did not see one, leave it null
            rather than guessing it.
            """;
    }

    // Every URL the model demonstrably saw: the search results it was handed and the pages
    // it opened. Canonicalised on the way in so the comparison in FindAsync is symmetric.
    internal static HashSet<string> RetrievedUrls(IEnumerable<ContentBlock> content)
    {
        var urls = new HashSet<string>();
        foreach (var block in content)
        {
            switch (block.Value)
            {
                case WebSearchToolResultBlock search
                    when search.Content.TryPickWebSearchResultBlocks(out var results):
                    foreach (var result in results)
                    {
                        urls.Add(Canonical(result.Url));
                    }
                    break;
                case WebFetchToolResultBlock fetch
                    when fetch.Content.TryPickWebFetchBlock(out var fetched):
                    urls.Add(Canonical(fetched.Url));
                    break;
            }
        }
        return urls;
    }

    // The normaliser itself now lives in Core (UrlCanonicalizer), hoisted there in plan 31
    // once the image sweep and the API's duplicate-submission check needed the same
    // "are these the same page" answer. Kept as a forwarder rather than replaced at every
    // call site: grounding is stated in terms of this provider, and the tests and golden
    // set read better naming it here.
    internal static string Canonical(string url) => UrlCanonicalizer.Canonical(url);

    // `effort` is set explicitly rather than left to the API's "high" default, and it
    // is `High` rather than the `Medium` this call carried while it was pinned to a
    // model that rejected the parameter outright. The failure this call actually has
    // is one of judgment — is this page about *this* route, in *this* sector — which
    // is what effort buys; Medium was chosen in plan 18 for a different model and its
    // reasoning doesn't transfer. Cost is the trade, and it is the one line to dial
    // down first (Medium, then Low) if per-run spend bites — see plan 29 §5.
    //
    // Effort still goes through WithAnthropicRequest's guard even though Model supports
    // it: the guard encodes the capability table, not today's pin, so a future re-pin to a
    // model that 400s on `effort` fails a test (ClaudeModelCapabilitiesTests) instead of
    // production.
    internal static ChatOptions BuildOptions() => new ChatOptions
    {
        ModelId = Model,
        // Bounds thinking *and* response text together, which is why 4096 no longer
        // fits: `thinking` is omitted below, and on Opus 5 that means adaptive
        // thinking is on (on sonnet-4-5, the previous pin, omitting it meant no
        // thinking at all). A budget sized for a non-thinking model truncates the
        // JSON mid-object here, and a truncated payload throws out of Deserialize
        // into five identical queue redeliveries.
        MaxOutputTokens = 8192,
        ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
    }.WithAnthropicRequest(Model, Effort.High,
        new WebSearchTool20260209
        {
            // Recall is the binding constraint, not search fees: a search is a cent
            // against a run costing dollars, so three more of them is noise in the
            // bill and a real widening of what the model gets to reject from.
            MaxUses = 8,
            // Search ranking is location-sensitive and every one of these queries is
            // Spanish-language and Spain-geographic. Costs nothing; the tool was
            // previously told nothing at all.
            UserLocation = new UserLocation { Country = "ES", Timezone = "Europe/Madrid" },
            BlockedDomains = BlockedSearchDomains
        },
        // Web search returns page *text* — it never returns an <img src>, which is
        // why plan 18's topoImageUrl could not work as written: the model was asked
        // for a fact that was never in its context, so whatever it answered was a
        // reconstruction that 404'd on download. Fetch can only open URLs already in
        // the conversation, which makes the pairing exactly right: search puts
        // candidates in context, fetch opens the promising ones. It also strengthens
        // the grounding and evidence rules for free — a model that has opened a page
        // can quote it, and its URLs land in the retrieved set.
        //
        // Both tools are declared raw rather than through HostedWebSearchTool, whose
        // portable shape carries none of the tuning above — see AnthropicChatExtensions.
        new WebFetchTool20260209 { MaxUses = 3, MaxContentTokens = 20000 });

    // Token counts are portable; the server-side search count is not — usage.server_tool_use
    // has no ChatResponse.Usage equivalent, so that one field alone comes off the raw
    // response, inline, since nothing else reads it.
    private void LogUsage(ChatResponse response, string routeName) =>
        _logger.LogInformation(
            "enrichment usage route={RouteName} input={Input} output={Output} searches={Searches}",
            routeName,
            response.Usage?.InputTokenCount ?? 0,
            response.Usage?.OutputTokenCount ?? 0,
            (response.RawRepresentation as Message)?.Usage.ServerToolUse?.WebSearchRequests ?? 0);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record EnrichmentPayload(
        [property: JsonPropertyName("links")] List<PayloadLink>? Links,
        [property: JsonPropertyName("topoPageUrl")] string? TopoPageUrl,
        [property: JsonPropertyName("topoImageUrl")] string? TopoImageUrl);

    private sealed record PayloadLink(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("evidence")] string? Evidence,
        [property: JsonPropertyName("confidence")] string? Confidence);
}

// The topo candidate's verification: a binary image classification, cheap enough that the
// frontier model buys nothing — claude-haiku-4-5 answers in one field.
public class ClaudeTopoVerificationProvider : ITopoVerificationProvider
{
    // Named for the same reason as ClaudeRouteEnrichmentProvider.Model — lets
    // ClaudeModelCapabilitiesTests assert this model's effort support directly.
    internal const string Model = "claude-haiku-4-5";

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeTopoVerificationProvider> _logger;

    private static readonly JsonElement ResultSchema = JsonSerializer
        .Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": { "isTopo": { "type": "boolean" } },
          "required": ["isTopo"],
          "additionalProperties": false
        }
        """);

    public ClaudeTopoVerificationProvider(IChatClient chatClient, ILogger<ClaudeTopoVerificationProvider> logger)
    {
        _logger = logger;
        _chatClient = chatClient;
    }

    public async Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken)
    {
        // "Plausibly matching" (plan 18's wording) invites a yes for any topo of any route
        // on the crag, which is most of them — the bar is identification, not plausibility.
        var prompt = $"""
            Is this image a climbing topo (a route-line photo or diagram) on which the line or
            the name of the route "{context.Name}" (grade {context.Grade}) in
            {context.AreaBreadcrumb} is itself identifiably marked?

            A topo of the sector that does not identify this particular route is a no, even
            when the route is somewhere on the wall it shows. So is any other image: a photo
            of a climber, a map, a logo, a page screenshot. Answer via the schema.
            """;

        // Image content is portable: DataContent carries the bytes and the media type, and
        // the adapter builds the base64 image block the raw path used to hand-assemble.
        ChatMessage message = new(ChatRole.User,
            [new DataContent(context.ImageBytes, context.ContentType), new TextContent(prompt)]);

        _logger.LogInformation("topo verification calling the chat client for route={RouteName}", context.Name);
        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync([message], BuildOptions(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "topo verification chat call failed for route={RouteName}", context.Name);
            throw;
        }

        _logger.LogInformation(
            "topo verification usage route={RouteName} input={Input} output={Output}",
            context.Name, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

        // No stop_reason check here, unlike the other four providers — an existing
        // inconsistency carried forward unchanged rather than fixed under a transport swap
        // (plan 32 §6): a pause_turn/refusal response still falls through to the blank-payload
        // check below and answers false.
        var json = response.Text;

        if (string.IsNullOrWhiteSpace(json)) return false;

        var parsed = JsonSerializer.Deserialize<TopoVerificationPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return parsed?.IsTopo ?? false;
    }

    // Effort is asked for and silently skipped: haiku-4-5 isn't in ClaudeModelCapabilities'
    // set, and WithAnthropicRequest is the one place that decides so — see plan 32 §4 on why
    // every provider calls it even when it contributes nothing.
    internal static ChatOptions BuildOptions() => new ChatOptions
    {
        ModelId = Model,
        MaxOutputTokens = 256,
        ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
    }.WithAnthropicRequest(Model, Effort.Low);

    private sealed record TopoVerificationPayload([property: JsonPropertyName("isTopo")] bool IsTopo);
}
