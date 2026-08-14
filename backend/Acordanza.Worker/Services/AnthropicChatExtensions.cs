using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace Acordanza.Worker.Services;

// The raw-passthrough seam (plan 32 §4). Everything the five Claude providers do that has a
// correct portable ChatOptions mapping — messages, vision, JSON-schema output, model id, max
// tokens — goes through IChatClient directly. Three things don't, and they all live here so
// there is one place to revisit when the portable surface catches up:
//
//   - output_config.effort, guarded per-model. ChatOptions.Reasoning.Effort serializes as
//     legacy thinking.budget_tokens for Anthropic, which is not the same field at all; and
//     the guard itself (ClaudeModelCapabilities) predates this seam and stays the single
//     source of truth for which models accept `effort` without 400ing.
//   - The exact Anthropic tool variant. HostedWebSearchTool downgrades to a search tool with
//     no MaxUses/UserLocation/BlockedDomains, which is most of why enrichment's search is
//     tuned at all.
//   - stop_reason and the server-tool-result content blocks, read off the raw response.
//     ChatResponse.FinishReason collapses pause_turn into the same "stop" value as end_turn,
//     so a check written against it would treat a tool-loop-exhausted response as a normal
//     completion — exactly the silent no-op plan 29 diagnosed and fixed. And
//     web_search_tool_result/web_fetch_tool_result have no portable equivalent at all.
public static class AnthropicChatExtensions
{
    // Called by every provider, whether or not it ends up contributing anything — a
    // haiku-pinned provider's fragment carries no OutputConfig and no Tools, and is
    // effectively a no-op merge (an absent raw OutputConfig doesn't clobber a portable
    // ResponseFormat already on the same options). Uniform call sites mean a future model
    // swap that changes the effort-support answer, or adds a tool to a call that has none
    // today, touches one call site instead of a bespoke one.
    public static ChatOptions WithAnthropicRequest(
        this ChatOptions options, string modelId, Effort? effort, params ToolUnion[] tools)
    {
        // MessageCreateParams marks Model and MaxTokens required, so the fragment has to
        // carry them even though it contributes nothing to either — which makes this the
        // natural place to enforce plan 32 §2's rule that both are always set explicitly.
        // FoundryClient.CreateChatClient deliberately configures no defaults for either
        // (five providers, three models, five different token budgets), and max_tokens is a
        // required field on the wire, so an omitted one fails at the first real call.
        var maxTokens = options.MaxOutputTokens
            ?? throw new InvalidOperationException(
                "ChatOptions.MaxOutputTokens must be set — the Anthropic IChatClient carries no default");

        options.RawRepresentationFactory = _ => new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = maxTokens,
            Messages = [],
            OutputConfig = effort is { } e && ClaudeModelCapabilities.SupportsEffort(modelId)
                ? new OutputConfig { Effort = e }
                : null,
            Tools = tools.Length > 0 ? [.. tools] : null,
        };
        return options;
    }

    // The read-side half. Every provider with an IsIncomplete check calls this instead of a
    // portable FinishReason comparison, because pause_turn and end_turn are
    // indistinguishable through FinishReason (both produce "stop").
    public static string? RawStopReason(this ChatResponse response) =>
        (response.RawRepresentation as Message)?.StopReason?.Raw();

    // Route enrichment's grounding needs the full block list, not just the stop reason —
    // same raw object, a different projection of it.
    public static IReadOnlyList<ContentBlock> RawContent(this ChatResponse response) =>
        (response.RawRepresentation as Message)?.Content ?? [];
}
