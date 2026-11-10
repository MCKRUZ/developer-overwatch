namespace DeveloperOverwatch.Application.Queries.GetRunningSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using MediatR;

public sealed class GetRunningSessionsQueryHandler
    : IRequestHandler<GetRunningSessionsQuery, Result<IReadOnlyList<RunningSessionItem>>>
{
    /// <summary>How far back burn looks: long enough to smooth a single large turn, short enough to
    /// read as "right now".</summary>
    public static readonly TimeSpan BurnLookback = TimeSpan.FromMinutes(15);

    private readonly IUsageRepository _repository;
    private readonly ISessionStaleThresholdProvider _staleThreshold;
    private readonly TimeProvider _time;

    public GetRunningSessionsQueryHandler(
        IUsageRepository repository, ISessionStaleThresholdProvider staleThreshold, TimeProvider time)
    {
        _repository = repository;
        _staleThreshold = staleThreshold;
        _time = time;
    }

    public async Task<Result<IReadOnlyList<RunningSessionItem>>> Handle(
        GetRunningSessionsQuery request, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var activeSince = now.AddMinutes(-_staleThreshold.StaleAfterMinutes);

        var sessions = await _repository.GetRunningSessionsAsync(activeSince, cancellationToken);
        var turns = await _repository.GetRecentTurnsOfRunningSessionsAsync(
            activeSince, now - BurnLookback, cancellationToken);
        var pointsBySession = turns.ToLookup(t => t.SessionId, ToUsagePoint);

        IReadOnlyList<RunningSessionItem> items = sessions
            .Select(s => ToItem(s, BurnRate.Over(pointsBySession[s.Id], now, BurnLookback), now))
            .OrderByDescending(i => i.BurnCostPerMinute)
            .ThenByDescending(i => i.LastActivityAt)
            .ToList();

        return Result<IReadOnlyList<RunningSessionItem>>.Success(items);
    }

    private static UsagePoint ToUsagePoint(SessionTurnUsageRow turn) => new(
        turn.Timestamp,
        (long)(turn.InputTokens ?? 0) + (turn.OutputTokens ?? 0)
            + (turn.CacheCreationInputTokens ?? 0) + (turn.CacheReadInputTokens ?? 0),
        turn.EstimatedCostUsd ?? 0m);

    private static RunningSessionItem ToItem(RunningSessionRow s, BurnRateResult burn, DateTime now) => new(
        s.Id,
        s.DeveloperEmail,
        s.DeveloperName,
        s.ProjectKey ?? s.RepoPath,
        s.MachineName,
        s.Agent,
        s.ActivityState,
        IdleForSeconds(s, now),
        s.LastActivityAt,
        s.TotalInputTokens,
        s.TotalOutputTokens,
        s.TotalCacheCreationInputTokens,
        s.TotalCacheReadInputTokens,
        s.EstimatedCostUsd,
        s.CurrentContextTokens,
        burn.TokensPerMinute,
        burn.CostPerMinute);

    // The state time comes from the developer's machine; a clock ahead of the server must not
    // produce a negative idle time.
    private static long? IdleForSeconds(RunningSessionRow s, DateTime now) =>
        s.ActivityState == ActivityState.Idle && s.ActivityStateAt is { } since
            ? Math.Max(0L, (long)(now - since).TotalSeconds)
            : null;
}
