using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Acordanza.Worker.Services;

// The image plus the one thing that is *given* rather than inferred: the sector the
// uploader picked in the form. Passing it in converts an impossible inference into
// context, which makes every other answer better — the same lesson plan 29 §2 learned
// when it started passing first ascensionists into the enrichment prompt.
public record TopoExtractionContext(byte[] ImageBytes, string ContentType, string AreaBreadcrumb);

// Name/Grade/Evidence/Confidence stay nullable strings and Length a nullable int: this is
// exactly what the model returned, unvalidated. RouteImportService.Accept is what turns
// it into something writable, so the acceptance rules live in one testable place rather
// than being half-enforced by the type.
public record ExtractedRoute(string? Name, string? Grade, int? Length, string? Evidence, string? Confidence);

// MultipleRoutes with no dominant route is a discard, not a coin flip (§3): trad topos
// are usually single-route, which is what makes one-route-per-import the right model, but
// when they aren't, a confidently wrong route is worse than a stated failure the uploader
// can act on. Completed=false is the stop_reason warning path, as everywhere else.
public record TopoExtractionResult(
    ExtractedRoute? Route,
    bool MultipleRoutes,
    string? Reason,
    bool Completed = true);

public interface ITopoExtractionProvider
{
    Task<TopoExtractionResult> ExtractAsync(TopoExtractionContext context, CancellationToken cancellationToken);
}

public class NoOpTopoExtractionProvider(ILogger<NoOpTopoExtractionProvider> logger) : ITopoExtractionProvider
{
    public Task<TopoExtractionResult> ExtractAsync(TopoExtractionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("topo extraction skipped: no Anthropic:FoundryResource configured");
        return Task.FromResult(new TopoExtractionResult(null, false, "not configured", Completed: false));
    }
}

// The expensive half: read the route's name, grade and length off the image.
public class ClaudeTopoExtractionProvider : ITopoExtractionProvider
{
    // Not the Haiku the gate uses, and the reason is not "smarter model", it's
    // **resolution**: Haiku downsamples to a 1568 px long edge, Opus 5 to 2576 px.
    // Anything larger is downsampled server-side, so a 4000 px phone photo of a
    // guidebook page reaches Haiku at 1568 px no matter what is sent. For a binary "is
    // there a line drawn on this rock" that is plenty; for reading «La Loca 6b+ 25m» off
    // small print it is very possibly the difference between a correct extraction and a
    // garbled one — which is the whole feature.
    //
    // Opus 5 rather than plan 30 §3's claude-sonnet-5: this subscription has no
    // GlobalStandard quota for the sonnet family, and Opus 5 is in the same 2576 px
    // image tier, so the resolution argument above carries over unchanged — the swap
    // costs money ($5/$25 per MTok against $3/$15), not fidelity.
    //
    // Must equal the Foundry deployment name in infra/terraform/ai_foundry.tf: the SDK
    // sends this string through as the model id, so a mismatch is a 404 at request time.
    internal const string Model = "claude-opus-5";

    private readonly IChatClient _chatClient;
    private readonly ILogger<ClaudeTopoExtractionProvider> _logger;

    // Borrows plan 29 §2's shape, because the same trick applies — make a fabricated
    // answer more expensive to produce than a blank one. `evidence` is the load-bearing
    // field: quoting the printed text is a much bigger fabrication step than inventing a
    // plausible route name.
    //
    // Note `pattern`, `minLength` and numeric bounds are NOT supported by structured
    // outputs, so every constraint on these values is enforced in C#
    // (RouteImportService.Accept), never in the schema.
    private static readonly JsonElement ResultSchema = JsonSerializer
        .Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "route": {
              "type": ["object", "null"],
              "properties": {
                "name": { "type": "string" },
                "grade": { "type": "string" },
                "length": { "type": ["integer", "null"] },
                "evidence": { "type": "string" },
                "confidence": { "type": "string", "enum": ["high", "medium", "low"] }
              },
              "required": ["name", "grade", "length", "evidence", "confidence"],
              "additionalProperties": false
            },
            "multipleRoutes": { "type": "boolean" },
            "reason": { "type": ["string", "null"] }
          },
          "required": ["route", "multipleRoutes", "reason"],
          "additionalProperties": false
        }
        """);

    public ClaudeTopoExtractionProvider(IChatClient chatClient, ILogger<ClaudeTopoExtractionProvider> logger)
    {
        _logger = logger;
        _chatClient = chatClient;
    }

    public async Task<TopoExtractionResult> ExtractAsync(TopoExtractionContext context, CancellationToken cancellationToken)
    {
        ChatMessage message = new(ChatRole.User,
            [new DataContent(context.ImageBytes, context.ContentType), new TextContent(BuildPrompt(context))]);

        _logger.LogInformation("topo extraction calling the chat client for area={Area}", context.AreaBreadcrumb);
        ChatResponse response;
        try
        {
            response = await _chatClient.GetResponseAsync([message], BuildOptions(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "topo extraction chat call failed for area={Area}", context.AreaBreadcrumb);
            throw;
        }

        _logger.LogInformation(
            "topo extraction usage area={Area} input={Input} output={Output}",
            context.AreaBreadcrumb, response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);

        var stopReason = response.RawStopReason();
        if (ClaudeRouteEnrichmentProvider.IsIncomplete(stopReason))
        {
            _logger.LogWarning("topo extraction incomplete stopReason={StopReason}", stopReason);
            return new TopoExtractionResult(null, false, null, Completed: false);
        }

        var json = response.Text;
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TopoExtractionResult(null, false, null, Completed: false);
        }

        _logger.LogInformation("topo extraction raw response area={Area} json={Json}", context.AreaBreadcrumb, json);

        var parsed = JsonSerializer.Deserialize<ExtractionPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var route = parsed?.Route is { } r
            ? new ExtractedRoute(r.Name, r.Grade, r.Length, r.Evidence, r.Confidence)
            : null;

        return new TopoExtractionResult(route, parsed?.MultipleRoutes ?? false, parsed?.Reason);
    }

    private static string BuildPrompt(TopoExtractionContext context) => $"""
        This image is a climbing topo uploaded to a private climbing log. Read the route it
        describes off the image.

        The uploader has already told us the sector: {context.AreaBreadcrumb}. Use it to
        orient yourself, but do not copy anything from it into the answer — the fields
        below must come from the image.

        - `name`: the route's name, exactly as printed. Do not translate, expand,
          re-capitalise or correct it.
        - `grade`: the grade exactly as printed. **Never convert between scales.** Spanish
          guidebooks are usually French-scale, but UIAA (V+) and YDS (5.11a) appear, and a
          converted grade is a guess wearing the costume of data. Copy what is printed; a
          grade this log can't accept is handled downstream, and is a better outcome than a
          silently converted one.
        - `length`: the route's length in metres if the topo states it, otherwise null. Do
          not estimate it from the photo.
        - `evidence`: the text you read the name and grade from, copied verbatim off the
          image. Not a paraphrase, not a description of where it appears.
        - `confidence`: "high" only when you can read the name and grade clearly enough
          that a sceptical reader looking at the same image would agree. Anything less is
          "medium" or "low", and anything below "high" is discarded downstream.

        Returning nothing is a correct and common answer, not a failure:

        - If the topo shows no route name at all, set `route` to null and say so in `reason`.
        - If the topo shows several routes and none is clearly the subject of the photo,
          set `multipleRoutes` to true and `route` to null. Only set `route` alongside
          `multipleRoutes` when one route is unmistakably the one being documented — a
          single highlighted line, or a page devoted to it.
        - If the print is too small, blurred or cut off to read with confidence, set
          `route` to null rather than guessing at the characters.

        `reason` is one short English sentence, read only by a developer tuning this
        prompt, never shown to the uploader.
        """;

    // Effort is Low, and this model does support it: reading small print off a photo is
    // perception, not judgment — there is no multi-step reasoning here for effort to buy,
    // unlike enrichment's "is this page about *this* route" call, which runs at High.
    internal static ChatOptions BuildOptions() => new ChatOptions
    {
        ModelId = Model,
        // Sized for thinking plus the payload, not the payload alone: the request
        // omits `thinking`, which on Opus 5 means adaptive thinking is *on*, and
        // the token budget bounds reasoning and response text together. The JSON here is a
        // few hundred tokens; the headroom is for the reasoning in front of it, and
        // running out truncates mid-object into a Deserialize throw and five queue
        // redeliveries. Same reason ClaudeRouteEnrichmentProvider carries 8192.
        MaxOutputTokens = 4096,
        ResponseFormat = ChatResponseFormat.ForJsonSchema(ResultSchema),
    }.WithAnthropicRequest(Model, Effort.Low);

    private sealed record ExtractionPayload(
        [property: JsonPropertyName("route")] PayloadRoute? Route,
        [property: JsonPropertyName("multipleRoutes")] bool MultipleRoutes,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record PayloadRoute(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("grade")] string? Grade,
        [property: JsonPropertyName("length")] int? Length,
        [property: JsonPropertyName("evidence")] string? Evidence,
        [property: JsonPropertyName("confidence")] string? Confidence);
}
