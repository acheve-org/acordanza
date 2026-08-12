using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Foundry;
using Anthropic.Models.Messages;
using Azure.Identity;

namespace Acordanza.Worker.Services;

public record RouteEnrichmentContext(string Name, string Grade, string AreaBreadcrumb);

public record EnrichmentResult(List<string> Links, string? TopoImageUrl);

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
        return Task.FromResult(new EnrichmentResult([], null));
    }
}

public class NoOpTopoVerificationProvider : ITopoVerificationProvider
{
    public Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

// Both Claude providers below reach Foundry the same way — one construction, in one place,
// because getting either half of it wrong fails only at the first real call, in production,
// and both halves once were wrong:
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
}

// Search-and-extract: one Messages API call with the server-side web search tool and
// structured outputs, reached through Azure AI Foundry (Hosted on Anthropic — structured
// outputs and server-side tools 400 on Azure-hosted deployments) so the worker
// authenticates with its managed identity instead of a stored API key.
public class ClaudeRouteEnrichmentProvider : IRouteEnrichmentProvider
{
    // TEMPORARY: claude-sonnet-4-5, not claude-sonnet-5 — matches the Foundry
    // deployment name pinned in infra/terraform/ai_foundry.tf due to a 0 TPM
    // quota on claude-sonnet-5 in this subscription. Revert both together.
    // A named constant (rather than inlining the literal below) so
    // ClaudeModelCapabilitiesTests can assert this exact model's effort
    // support without duplicating the string.
    internal const string Model = "claude-sonnet-4-5";

    private readonly IAnthropicClient _client;
    private readonly ILogger<ClaudeRouteEnrichmentProvider> _logger;

    private static readonly IReadOnlyDictionary<string, JsonElement> ResultSchema = JsonSerializer
        .Deserialize<Dictionary<string, JsonElement>>("""
        {
          "type": "object",
          "properties": {
            "links": { "type": "array", "items": { "type": "string" } },
            "topoImageUrl": { "type": ["string", "null"] }
          },
          "required": ["links", "topoImageUrl"],
          "additionalProperties": false
        }
        """)!;

    public ClaudeRouteEnrichmentProvider(IConfiguration configuration, ILogger<ClaudeRouteEnrichmentProvider> logger)
    {
        _logger = logger;
        _client = FoundryClient.Create(configuration);
    }

    public async Task<EnrichmentResult> FindAsync(RouteEnrichmentContext context, CancellationToken cancellationToken)
    {
        var prompt = $"""
            You're researching the climbing route "{context.Name}" (grade {context.Grade}) in
            {context.AreaBreadcrumb}. Search the web for pages specifically about this route or
            its sector: first-ascent stories, videos, guidebook pages, topo publications. Most
            sources will be in Spanish. Return only pages that are actually about this route or
            sector — if nothing specific turns up, return an empty links list rather than
            tangential results. If one of the pages publishes a topo (route-line) image for this
            route, return its direct image URL as topoImageUrl; otherwise null.
            """;

        _logger.LogInformation("enrichment calling Messages.Create for route={RouteName}", context.Name);
        Message response;
        try
        {
            response = await _client.Messages.Create(BuildRequestParams(prompt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "enrichment Messages.Create failed for route={RouteName}", context.Name);
            throw;
        }
        _logger.LogInformation("enrichment Messages.Create returned for route={RouteName}", context.Name);

        LogUsage(response.Usage, context.Name);

        var json = string.Concat(response.Content
            .Select(b => b.Value as TextBlock)
            .Where(t => t is not null)
            .Select(t => t!.Text));

        if (string.IsNullOrWhiteSpace(json))
        {
            return new EnrichmentResult([], null);
        }

        var parsed = JsonSerializer.Deserialize<EnrichmentPayload>(json, JsonOptions);
        return new EnrichmentResult(parsed?.Links ?? [], parsed?.TopoImageUrl);
    }

    // Not optional: omitted, the API defaults to "high", and 5-family models think by
    // default — for a search-and-extract job that quietly buys the deepest (and most
    // expensive) reasoning tier. See the plan's cost decision. But `effort` 400s on a
    // model that doesn't support it (see ClaudeModelCapabilities) — Model is currently
    // pinned to one that doesn't, so this is conditional rather than always-on.
    internal static MessageCreateParams BuildRequestParams(string prompt)
    {
        var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResultSchema } };
        if (ClaudeModelCapabilities.SupportsEffort(Model))
        {
            outputConfig = outputConfig with { Effort = Effort.Medium };
        }

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 4096,
            OutputConfig = outputConfig,
            Tools = [new WebSearchTool20260209 { MaxUses = 5 }],
            Messages = [new MessageParam { Role = Role.User, Content = prompt }]
        };
    }

    private void LogUsage(Usage usage, string routeName) =>
        _logger.LogInformation(
            "enrichment usage route={RouteName} input={Input} output={Output} searches={Searches}",
            routeName, usage.InputTokens, usage.OutputTokens, usage.ServerToolUse?.WebSearchRequests ?? 0);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record EnrichmentPayload(
        [property: JsonPropertyName("links")] List<string>? Links,
        [property: JsonPropertyName("topoImageUrl")] string? TopoImageUrl);
}

// The topo candidate's verification: a binary image classification, cheap enough that the
// frontier model buys nothing — claude-haiku-4-5 answers in one field.
public class ClaudeTopoVerificationProvider : ITopoVerificationProvider
{
    // Named for the same reason as ClaudeRouteEnrichmentProvider.Model — lets
    // ClaudeModelCapabilitiesTests assert this model's effort support directly.
    internal const string Model = "claude-haiku-4-5";

    private readonly IAnthropicClient _client;
    private readonly ILogger<ClaudeTopoVerificationProvider> _logger;

    private static readonly IReadOnlyDictionary<string, JsonElement> ResultSchema = JsonSerializer
        .Deserialize<Dictionary<string, JsonElement>>("""
        {
          "type": "object",
          "properties": { "isTopo": { "type": "boolean" } },
          "required": ["isTopo"],
          "additionalProperties": false
        }
        """)!;

    public ClaudeTopoVerificationProvider(IConfiguration configuration, ILogger<ClaudeTopoVerificationProvider> logger)
    {
        _logger = logger;
        _client = FoundryClient.Create(configuration);
    }

    public async Task<bool> IsTopoAsync(TopoVerificationContext context, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Is this image a climbing topo (route-line photo/diagram) plausibly matching the
            route "{context.Name}" (grade {context.Grade}) in {context.AreaBreadcrumb}?
            Answer via the schema.
            """;

        var imageBlock = new ContentBlockParam(new ImageBlockParam
        {
            Source = new ImageBlockParamSource(new Base64ImageSource
            {
                MediaType = context.ContentType,
                Data = Convert.ToBase64String(context.ImageBytes)
            })
        });
        var textBlock = new ContentBlockParam(new TextBlockParam { Text = prompt });

        _logger.LogInformation("topo verification calling Messages.Create for route={RouteName}", context.Name);
        Message response;
        try
        {
            response = await _client.Messages.Create(
                BuildRequestParams(imageBlock, textBlock), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "topo verification Messages.Create failed for route={RouteName}", context.Name);
            throw;
        }

        _logger.LogInformation(
            "topo verification usage route={RouteName} input={Input} output={Output}",
            context.Name, response.Usage.InputTokens, response.Usage.OutputTokens);

        var json = string.Concat(response.Content
            .Select(b => b.Value as TextBlock)
            .Where(t => t is not null)
            .Select(t => t!.Text));

        if (string.IsNullOrWhiteSpace(json)) return false;

        var parsed = JsonSerializer.Deserialize<TopoVerificationPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return parsed?.IsTopo ?? false;
    }

    internal static MessageCreateParams BuildRequestParams(ContentBlockParam imageBlock, ContentBlockParam textBlock)
    {
        var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = ResultSchema } };
        if (ClaudeModelCapabilities.SupportsEffort(Model))
        {
            outputConfig = outputConfig with { Effort = Effort.Low };
        }

        return new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 256,
            OutputConfig = outputConfig,
            Messages = [new MessageParam { Role = Role.User, Content = new List<ContentBlockParam> { imageBlock, textBlock } }]
        };
    }

    private sealed record TopoVerificationPayload([property: JsonPropertyName("isTopo")] bool IsTopo);
}
