namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Enums;

/// <summary>Read-side usage queries for the usage meter, kept apart from <see cref="ISessionRepository"/>.</summary>
public interface IUsageRepository
{
    /// <summary>Sessions stored Active whose last activity is at or after <paramref name="activeSince"/>,
    /// which is the exact complement of the read-time Stale label.</summary>
    Task<IReadOnlyList<RunningSessionRow>> GetRunningSessionsAsync(
        DateTime activeSince, CancellationToken ct = default);

    /// <summary>Per-turn usage, main thread and subagents alike, of the sessions that
    /// <see cref="GetRunningSessionsAsync"/> returns for the same <paramref name="activeSince"/>,
    /// limited to turns after <paramref name="turnsAfter"/>.</summary>
    Task<IReadOnlyList<SessionTurnUsageRow>> GetRecentTurnsOfRunningSessionsAsync(
        DateTime activeSince, DateTime turnsAfter, CancellationToken ct = default);
}

/// <summary>A running session's identity, activity and running totals.</summary>
/// <param name="CurrentContextTokens">ContextTokens of the latest main-thread turn that reported one.</param>
public sealed record RunningSessionRow(
    string Id,
    string DeveloperEmail,
    string? DeveloperName,
    string? RepoPath,
    string? ProjectKey,
    string? MachineName,
    string Agent,
    ActivityState? ActivityState,
    DateTime? ActivityStateAt,
    DateTime LastActivityAt,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalCacheCreationInputTokens,
    long TotalCacheReadInputTokens,
    decimal EstimatedCostUsd,
    long? CurrentContextTokens);
