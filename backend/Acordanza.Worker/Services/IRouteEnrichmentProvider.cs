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

// Search-and-extract: one Messages API call with the server-side web search tool and
// structured outputs, reached through Azure AI Foundry (Hosted on Anthropic — structured
// outputs and server-side tools 400 on Azure-hosted deployments) so the worker
// authenticates with its managed identity instead of a stored API key.
public class ClaudeRouteEnrichmentProvider : IRouteEnrichmentProvider
{
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
        var resource = configuration["Anthropic:FoundryResource"]
            ?? throw new InvalidOperationException("Missing Anthropic:FoundryResource configuration");
        var credentials = new AnthropicFoundryIdentityTokenCredentials(new DefaultAzureCredential(), resource, []);
        _client = new AnthropicFoundryClient(credentials);
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

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            // TEMPORARY: claude-sonnet-4-5, not claude-sonnet-5 — matches the Foundry
            // deployment name pinned in infra/terraform/ai_foundry.tf due to a 0 TPM
            // quota on claude-sonnet-5 in this subscription. Revert both together.
            Model = "claude-sonnet-4-5",
            MaxTokens = 4096,
            // Not optional: omitted, the API defaults to "high", and 5-family models think by
            // default — for a search-and-extract job that quietly buys the deepest (and most
            // expensive) reasoning tier. See the plan's cost decision.
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Medium,
                Format = new JsonOutputFormat { Schema = ResultSchema }
            },
            Tools = [new WebSearchTool20260209 { MaxUses = 5 }],
            Messages = [new MessageParam { Role = Role.User, Content = prompt }]
        }, cancellationToken);

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
        var resource = configuration["Anthropic:FoundryResource"]
            ?? throw new InvalidOperationException("Missing Anthropic:FoundryResource configuration");
        var credentials = new AnthropicFoundryIdentityTokenCredentials(new DefaultAzureCredential(), resource, []);
        _client = new AnthropicFoundryClient(credentials);
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

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = "claude-haiku-4-5",
            MaxTokens = 256,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = ResultSchema }
            },
            Messages = [new MessageParam { Role = Role.User, Content = new List<ContentBlockParam> { imageBlock, textBlock } }]
        }, cancellationToken);

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

    private sealed record TopoVerificationPayload([property: JsonPropertyName("isTopo")] bool IsTopo);
}
