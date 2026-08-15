using System.Text.Json;
using Acordanza.Worker.Services;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// The regression this file exists for: plan 32 moved the five providers' hand-written JSON
// schemas from the raw MessageCreateParams onto the portable ChatOptions.ResponseFormat,
// which routes them through the Anthropic SDK's schema transform on the way out. That
// transform reads a subschema's `type` only when it is a plain string; an array-valued
// `type` falls through to a base keyword whitelist that carries no `properties`,
// `required` or `additionalProperties`, so all three are stripped off the subschema and
// serialized into its `description`. A nullable *object* written as
// `"type": ["object", "null"]` therefore reached Foundry as a bare `{"type": ["object",
// "null"], "description": "..."}` and 400'd with
//
//   output_config.format.schema: For 'object' type, 'additionalProperties' must be
//   explicitly set to false
//
// which killed both route-import kinds in production while the enrichment schema (no
// union-typed object in it) kept working. Nullable objects are spelled with `anyOf`
// instead — a shape both the API and the transform preserve intact.
//
// Asserted structurally over every provider's real schema rather than against one known
// bad string, so a sixth provider, or a new nullable object in an existing one, is caught
// here instead of at the first live Foundry call. Pure unit tier — no Docker, no network.
public class ClaudeSchemaShapeTests
{
    public static TheoryData<string, ChatOptions> ProviderOptions() => new()
    {
        { nameof(ClaudeRouteEnrichmentProvider), ClaudeRouteEnrichmentProvider.BuildOptions() },
        { nameof(ClaudeTopoVerificationProvider), ClaudeTopoVerificationProvider.BuildOptions() },
        { nameof(ClaudeTopoImportGateProvider), ClaudeTopoImportGateProvider.BuildOptions() },
        { nameof(ClaudeTopoExtractionProvider), ClaudeTopoExtractionProvider.BuildOptions() },
        { nameof(ClaudePageExtractionProvider), ClaudePageExtractionProvider.BuildOptions() },
    };

    // The SDK-transform rule: `type` must be a plain string wherever the subschema carries
    // object keywords, because an array-valued `type` is what makes the transform drop them.
    [Theory]
    [MemberData(nameof(ProviderOptions))]
    public void No_schema_declares_an_object_through_a_union_type(string provider, ChatOptions options)
    {
        foreach (var node in Subschemas(SchemaOf(options)))
        {
            if (!node.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Array) continue;

            type.EnumerateArray()
                .Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "object")
                .ShouldBeFalse(
                    $"{provider} declares an object through a union type; use anyOf: [<object>, {{\"type\": \"null\"}}]");
        }
    }

    // The API's own rule, and the one the 400 above actually named. Checked independently
    // of the transform rule so a schema that simply forgets the line fails here too.
    [Theory]
    [MemberData(nameof(ProviderOptions))]
    public void Every_object_in_every_schema_sets_additionalProperties_false(string provider, ChatOptions options)
    {
        foreach (var node in Subschemas(SchemaOf(options)))
        {
            if (!node.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "object")
            {
                continue;
            }

            node.TryGetProperty("additionalProperties", out var additional).ShouldBeTrue(
                $"{provider} has an object subschema with no additionalProperties");
            additional.ValueKind.ShouldBe(JsonValueKind.False,
                $"{provider} has an object subschema whose additionalProperties isn't false");
        }
    }

    private static JsonElement SchemaOf(ChatOptions options)
    {
        var format = options.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        return format.Schema.ShouldNotBeNull();
    }

    // Every subschema, the root included: the values under `properties`, `items`, and each
    // branch of `anyOf`/`allOf` — the shapes these five schemas are built from.
    private static IEnumerable<JsonElement> Subschemas(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) yield break;

        yield return node;

        if (node.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                foreach (var nested in Subschemas(property.Value)) yield return nested;
            }
        }

        if (node.TryGetProperty("items", out var items))
        {
            foreach (var nested in Subschemas(items)) yield return nested;
        }

        foreach (var keyword in new[] { "anyOf", "allOf" })
        {
            if (!node.TryGetProperty(keyword, out var branches)
                || branches.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var branch in branches.EnumerateArray())
            {
                foreach (var nested in Subschemas(branch)) yield return nested;
            }
        }
    }
}
