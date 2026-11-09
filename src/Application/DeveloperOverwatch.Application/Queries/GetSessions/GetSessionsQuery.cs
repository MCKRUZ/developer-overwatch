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
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheCreationInputTokens,
    long TotalCacheReadInputTokens,
    decimal EstimatedCostUsd,
    int ViolationCount,
    IReadOnlyDictionary<string, DailyTokenCost> DailyUsage,
    string Agent,
    string? MachineName,
    string? ProjectKey,
    long? PeakContextTokens,
    ActivityState? ActivityState,
    DateTime? ActivityStateAt,
    int ToolErrorCount,
    string? LastToolErrorName,
    DateTime? LastToolErrorAt);

/// <summary>Token/cost totals for one calendar day (ISO "yyyy-MM-dd" key in the owning
/// dictionary) — lets the sessions list show "what did this session cost today/on this day"
/// instead of its lifetime total, which is what actually matters for daily cost control.</summary>
public sealed record DailyTokenCost(
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd);
