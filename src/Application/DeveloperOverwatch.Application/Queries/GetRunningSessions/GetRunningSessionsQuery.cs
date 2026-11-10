namespace DeveloperOverwatch.Application.Queries.GetRunningSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

/// <summary>Sessions that are running right now: stored Active and not past the stale threshold.</summary>
/// <param name="Developer">Exact developer email to narrow to, as on the usage summary; null does not filter.</param>
/// <param name="Project">Project key, or repository path when a session has none; null does not filter.</param>
public sealed record GetRunningSessionsQuery(string? Developer = null, string? Project = null)
    : IRequest<Result<IReadOnlyList<RunningSessionItem>>>;

/// <summary>One running session with its usage so far and its burn over the last 15 minutes.</summary>
/// <param name="Project">The project key when the Agent sent one, otherwise the repository path.</param>
/// <param name="IdleForSeconds">Seconds since the session went idle; null unless it is idle now.</param>
/// <param name="CurrentContextTokens">Context of the latest main-thread request; subagent turns have their
/// own context window and are ignored. Null when no main-thread turn reported one.</param>
/// <param name="BurnTokensPerMinute">All four token kinds over the last 15 minutes, per minute.</param>
/// <param name="BurnCostPerMinute">Estimated US dollars over the last 15 minutes, per minute.</param>
public sealed record RunningSessionItem(
    string Id,
    string DeveloperEmail,
    string? DeveloperName,
    string? Project,
    string? MachineName,
    string Agent,
    ActivityState? ActivityState,
    long? IdleForSeconds,
    DateTime LastActivityAt,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd,
    long? CurrentContextTokens,
    double BurnTokensPerMinute,
    decimal BurnCostPerMinute);
