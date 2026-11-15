namespace DeveloperOverwatch.Application.Queries.GetSessionPrompts;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>What each prompt in a session cost, with everything it set off up to the next prompt.</summary>
public sealed record GetSessionPromptsQuery(string SessionId) : IRequest<Result<IReadOnlyList<PromptCost>>>;

/// <summary>One prompt and the turns charged to it.</summary>
/// <param name="Index">1 for the first prompt; 0 for turns that came before any prompt.</param>
/// <param name="Label">The prompt's text with whitespace collapsed, cut to 80 characters; null when none was captured.</param>
/// <param name="SubagentCostUsd">The part of <paramref name="EstimatedCostUsd"/> spent by subagents.</param>
public sealed record PromptCost(
    int Index,
    DateTime StartedAt,
    string? Label,
    int TurnCount,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    long TotalTokens,
    decimal EstimatedCostUsd,
    decimal SubagentCostUsd);
