namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record GetSessionsQuery(
    string? DeveloperEmail = null,
    string? Repo = null,
    DateTime? From = null,
    DateTime? To = null,
    SessionStatus? Status = null) : IRequest<Result<IReadOnlyList<SessionListItem>>>;

public sealed record SessionListItem(
    string Id,
    string DeveloperEmail,
    string? DeveloperName,
    string? RepoPath,
    string? Title,
    SessionStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime CreatedAt,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheCreationInputTokens,
    int TotalCacheReadInputTokens,
    decimal EstimatedCostUsd,
    int ViolationCount,
    IReadOnlyDictionary<string, DailyTokenCost> DailyUsage);

/// <summary>Token/cost totals for one calendar day (ISO "yyyy-MM-dd" key in the owning
/// dictionary) — lets the sessions list show "what did this session cost today/on this day"
/// instead of its lifetime total, which is what actually matters for daily cost control.</summary>
public sealed record DailyTokenCost(int InputTokens, int OutputTokens, decimal EstimatedCostUsd);
