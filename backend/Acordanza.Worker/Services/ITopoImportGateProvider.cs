using System.Text.Json;
using System.Text.Json.Serialization;
using Acordanza.Core.Services;
using Anthropic;
using Anthropic.Models.Messages;

namespace Acordanza.Worker.Services;

// The gate's whole input: one image. Deliberately carries no route context, because the
// question is "is this a climbing topo at all" — a route-line photo, a drawn overlay, a
// guidebook page — as opposed to a selfie, a landscape, a map, a screenshot.
//
// This is a *different question* from the one ITopoVerificationProvider answers ("is this
// a topo on which route X is identifiably marked"), which needs a route that doesn't
// exist yet at this point in the pipeline — hence a sibling interface rather than a
// widened one.
public record TopoImportGateContext(byte[] ImageBytes, string ContentType);

// Completed=false means the call ended on a warning path (a stop_reason that isn't
// end_turn) rather than on a real answer — RouteImportService turns that into Failed,
// never Discarded, so the uploader is told the pipeline fell over rather than that their
// topo was unreadable. Same distinction EnrichmentResult.Completed draws.
public record TopoImportGateResult(bool IsTopo, string? Reason, bool Completed = true);

public interface ITopoImportGateProvider
{
    Task<TopoImportGateResult> IsTopoAsync(TopoImportGateContext context, CancellationToken cancellationToken);
}

// No Anthropic:FoundryResource configured — the import is skipped rather than failing,
// same philosophy as ConsoleEmailSender and the enrichment no-ops: the whole
// upload → queue → worker → row loop stays exercisable locally against Azurite with no
// Foundry access and no `az login`.
//
// Completed: false, not IsTopo: false, and the distinction is the point — an unconfigured
// environment is the pipeline never having looked, which is exactly what Failed means
// (§1). Reporting "this doesn't look like a topo" would be the one lie the four states
// exist to prevent.
public class NoOpTopoImportGateProvider(ILogger<NoOpTopoImportGateProvider> logger) : ITopoImportGateProvider
{
    public Task<TopoImportGateResult> IsTopoAsync(TopoImportGateContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("topo import gate skipped: no Anthropic:FoundryResource configured");
        return Task.FromResult(new TopoImportGateResult(false, "no Anthropic:FoundryResource configured", Completed: false));
    }
}

// The cheap gate: one vision call on claude-haiku-4-5 (the deployment the enrichment
// topo check already uses), whose `false` short-circuits the run before the expensive
// extraction. A binary classifier is exactly the shape few-shot examples help most, so
// this one carries them — see TopoImportFewShotSet.
public class ClaudeTopoImportGateProvider : ITopoImportGateProvider
{
    // Same deployment ClaudeTopoVerificationProvider pins, for the same reason: a binary
    // "is there a line drawn on this rock" is a low-resolution question, and Haiku's
    // 1568 px long edge is plenty for it. The extraction is what needs Opus 5's
    // 2576 px — see ClaudeTopoExtractionProvider.
    internal const string Model = "claude-haiku-4-5";

    private readonly IAnthropicClient _client;
    private readonly TopoImportFewShotSet _fewShot;
    private readonly ILogger<ClaudeTopoImportGateProvider> _logger;

    private static readonly IReadOnlyDictionary<string, JsonElement> ResultSchema = JsonSerializer
        .Deserialize<Dictionary<string, JsonElement>>("""
        {
          "type": "object",
          "properties": {
            "isTopo": { "type": "boolean" },
            "reason": { "type": "string" }
          },
          "required": ["isTopo", "reason"],
          "additionalProperties": false
        }
        """)!;

    public ClaudeTopoImportGateProvider(
        IConfiguration configuration, TopoImportFewShotSet fewShot, ILogger<ClaudeTopoImportGateProvider> logger)
    {
        _logger = logger;
        _fewShot = fewShot;
        _client = FoundryClient.Create(configuration);
    }

    public async Task<TopoImportGateResult> IsTopoAsync(TopoImportGateContext context, CancellationToken cancellationToken)
    {
        var examples = await _fewShot.GetGateExamplesAsync(cancellationToken);
        var content = new List<ContentBlockParam>();

        if (examples.Count > 0)
        {
            content.Add(Text(
                "First, some labelled examples from this catalogue. They are examples only — " +
                "do not classify them, and do not let them be the image you answer about."));
            foreach (var example in examples)
            {
                content.Add(Text(example.IsTopo
                    ? "Example — this IS a climbing topo:"
                    : "Example — this is NOT a climbing topo:"));
                content.Add(Image(example.Bytes, example.ContentType));
            }
        }

        content.Add(Text("Now the image to classify:"));
        content.Add(Image(context.ImageBytes, context.ContentType));
        content.Add(Text("""
            Is this image a climbing topo — a photo or drawing of a crag with route lines,
            names or grades marked on it, a topo panel at a parking, or a page from a
            climbing guidebook?

            A photo of a climber, a landscape with no route markings, a map, a logo, a
            screenshot of a website, or a photo of anything that isn't rock is a no. Being
            a photo of rock is not by itself enough: a topo carries route information —
            drawn lines, printed names, grades, or bolt counts.

            Answer via the schema. `reason` is one short English sentence, and is only read
            by a developer tuning this prompt, never shown to the uploader.
            """));

        _logger.LogInformation("topo import gate calling Messages.Create with {Examples} few-shot example(s)", examples.Count);
        Message response;
        try
        {
            response = await _client.Messages.Create(BuildRequestParams(content), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "topo import gate Messages.Create failed");
            throw;
        }

        _logger.LogInformation(
            "topo import gate usage input={Input} output={Output}",
            response.Usage.InputTokens, response.Usage.OutputTokens);

        // Same allow-list of one as enrichment: anything but end_turn means there's no
        // trustworthy payload, and that's a Failed row (retryable), not a Discarded one.
        var stopReason = response.StopReason?.Raw();
        if (ClaudeRouteEnrichmentProvider.IsIncomplete(stopReason))
        {
            _logger.LogWarning("topo import gate incomplete stopReason={StopReason}", stopReason);
            return new TopoImportGateResult(false, null, Completed: false);
        }

        var json = string.Concat(response.Content
            .Select(b => b.Value as TextBlock)
            .Where(t => t is not null)
            .Select(t => t!.Text));
        if (string.IsNullOrWhiteSpace(json))
        {
            return new TopoImportGateResult(false, null, Completed: false);
        }

        var parsed = JsonSerializer.Deserialize<GatePayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new TopoImportGateResult(parsed?.IsTopo ?? false, parsed?.Reason);
    }

    internal static ContentBlockParam Text(string text) => new(new TextBlockParam { Text = text });

    internal static ContentBlockParam Image(byte[] bytes, string contentType) => new(new ImageBlockParam
    {
        Source = new ImageBlockParamSource(new Base64ImageSource
        {
            MediaType = contentType,
            Data = Convert.ToBase64String(bytes)
        })
    });

    internal static MessageCreateParams BuildRequestParams(List<ContentBlockParam> content)
    {
        var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResultSchema } };
        if (ClaudeModelCapabilities.SupportsEffort(Model))
        {
            outputConfig = outputConfig with { Effort = Effort.Low };
        }

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 512,
            OutputConfig = outputConfig,
            Messages = [new MessageParam { Role = Role.User, Content = content }]
        };
    }

    private sealed record GatePayload(
        [property: JsonPropertyName("isTopo")] bool IsTopo,
        [property: JsonPropertyName("reason")] string? Reason);
}

public record TopoFewShotExample(byte[] Bytes, string ContentType, bool IsTopo);

// The labelled example set the gate prepends to every call, read from blob storage
// rather than the repo (plan 30 §5): in-repo embedded resources would be nicer —
// versioned with the prompt, reviewable in the same PR — but these are third-party topo
// scans, and checking them into a public repository is a copyright exposure this app
// doesn't currently have.
//
//   {FewShotPrefix}gate/topo/*        → labelled "this IS a topo"
//   {FewShotPrefix}gate/not-topo/*    → labelled "this is NOT a topo"
//
// The prefix *is* the label, so there's no manifest file to drift; curating is
// `az storage blob upload` into the right prefix, deliberately manual, matching the
// precedent that GoldenSet.json is checked in empty for a human to fill.
//
// Singleton with a once-per-process cache: the gate provider is scoped (one per queue
// message), and re-downloading the same images per message would pay the blob cost
// several times over in a single drain. No prompt caching on the Anthropic side, on the
// arithmetic in plan 30 §5 — uploads are sporadic, the 5-minute default TTL would
// essentially never hit, and the 1-hour TTL needs three reads to break even.
public class TopoImportFewShotSet(
    BlobStorageService blobStorage, IConfiguration configuration, ILogger<TopoImportFewShotSet> logger)
{
    // Bounds what an over-filled prefix can cost: at roughly $0.002 per Haiku image, a
    // prefix somebody dumped 200 scans into would quietly turn a $0.002 gate into a $0.40
    // one. Six per label is already past what plan 30 §5 sizes the set at.
    private const int MaxPerLabel = 6;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<TopoFewShotExample>? _cached;

    public async Task<IReadOnlyList<TopoFewShotExample>> GetGateExamplesAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null) return _cached;

            var prefix = configuration["Storage:FewShotPrefix"] ?? "_fewshot/";
            var examples = new List<TopoFewShotExample>();
            examples.AddRange(await LoadAsync($"{prefix}gate/topo/", isTopo: true, cancellationToken));
            examples.AddRange(await LoadAsync($"{prefix}gate/not-topo/", isTopo: false, cancellationToken));

            logger.LogInformation("topo import few-shot set loaded {Count} example(s) from {Prefix}", examples.Count, prefix);
            _cached = examples;
            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<TopoFewShotExample>> LoadAsync(string prefix, bool isTopo, CancellationToken cancellationToken)
    {
        var loaded = new List<TopoFewShotExample>();
        List<string> names;
        try
        {
            names = await blobStorage.ListAsync(prefix, cancellationToken);
        }
        catch (Exception ex)
        {
            // A missing or unreadable example set must degrade to a zero-shot gate, never
            // fail the import: the examples are a tuning aid, not an input.
            logger.LogWarning(ex, "topo import few-shot listing failed for {Prefix}, continuing without it", prefix);
            return loaded;
        }

        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal).Take(MaxPerLabel))
        {
            var blob = await blobStorage.DownloadAsync(name);
            if (blob is null) continue;

            var contentType = string.IsNullOrEmpty(blob.Value.ContentType)
                ? ContentTypeFor(name)
                : blob.Value.ContentType;
            if (!MediaUploadService.AllowedImageContentTypes.Contains(contentType))
            {
                logger.LogWarning("topo import few-shot skipped {Blob}: unsupported content type {ContentType}", name, contentType);
                continue;
            }

            loaded.Add(new TopoFewShotExample(blob.Value.Content, contentType, isTopo));
        }
        return loaded;
    }

    // `az storage blob upload` doesn't always set a content type, and the Messages API
    // needs a real media type — fall back to the extension rather than dropping the example.
    private static string ContentTypeFor(string blobName) => Path.GetExtension(blobName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => ""
    };
}
