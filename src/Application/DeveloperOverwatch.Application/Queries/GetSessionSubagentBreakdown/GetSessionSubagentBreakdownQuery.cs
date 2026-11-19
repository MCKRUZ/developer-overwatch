namespace DeveloperOverwatch.Application.Queries.GetSessionSubagentBreakdown;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>A session's usage split between the main thread and each subagent.</summary>
public sealed record GetSessionSubagentBreakdownQuery(string SessionId) : IRequest<Result<IReadOnlyList<SubagentUsage>>>;

/// <param name="SubagentName">Null for the main thread.</param>
public sealed record SubagentUsage(
    string? SubagentName,
    int TurnCount,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    long TotalTokens,
    decimal EstimatedCostUsd);
