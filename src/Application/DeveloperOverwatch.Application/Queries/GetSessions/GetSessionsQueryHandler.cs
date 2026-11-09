namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionsQueryHandler
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<SessionListItem>>>
{
    private readonly ISessionRepository _repository;
    private readonly ISessionStaleThresholdProvider _staleThreshold;

    public GetSessionsQueryHandler(ISessionRepository repository, ISessionStaleThresholdProvider staleThreshold)
    {
        _repository = repository;
        _staleThreshold = staleThreshold;
    }

    public async Task<Result<IReadOnlyList<SessionListItem>>> Handle(
        GetSessionsQuery request, CancellationToken cancellationToken)
    {
        // "Stale" is never stored — it's read-time-only, so a caller asking for Active or Stale
        // both need the underlying Active rows fetched, then split by computed staleness below.
        var dbStatusFilter = request.Status == SessionStatus.Stale ? SessionStatus.Active : request.Status;

        var sessions = await _repository.GetAllAsync(
            request.DeveloperEmail, request.Repo, request.From, request.To, dbStatusFilter, cancellationToken);

        var violationCounts = await _repository.GetViolationCountsAsync(
            sessions.Select(s => s.Id), cancellationToken);

        var usageRows = await _repository.GetTurnUsageRowsForSessionsAsync(
            sessions.Select(s => s.Id), cancellationToken);

        var dailyUsageBySession = usageRows
            .GroupBy(r => r.SessionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, DailyTokenCost>)g
                    .GroupBy(r => DateOnly.FromDateTime(r.Timestamp).ToString("yyyy-MM-dd"))
                    .ToDictionary(
                        dayGroup => dayGroup.Key,
                        dayGroup => new DailyTokenCost(
                            dayGroup.Sum(r => r.InputTokens ?? 0),
                            dayGroup.Sum(r => r.OutputTokens ?? 0),
                            dayGroup.Sum(r => r.CacheCreationInputTokens ?? 0),
                            dayGroup.Sum(r => r.CacheReadInputTokens ?? 0),
                            dayGroup.Sum(r => r.EstimatedCostUsd ?? 0m))));

        var emptyDailyUsage = (IReadOnlyDictionary<string, DailyTokenCost>)
            new Dictionary<string, DailyTokenCost>();

        var staleCutoff = DateTime.UtcNow.AddMinutes(-_staleThreshold.StaleAfterMinutes);
        SessionStatus EffectiveStatus(Session s) =>
            s.Status == SessionStatus.Active && s.LastActivityAt < staleCutoff
                ? SessionStatus.Stale
                : s.Status;

        var items = sessions.Select(s => new SessionListItem(
            s.Id,
            s.DeveloperEmail,
            s.DeveloperName,
            s.RepoPath,
            s.Title,
            EffectiveStatus(s),
            s.StartedAt,
            s.EndedAt,
            s.CreatedAt,
            s.TotalInputTokens,
            s.TotalOutputTokens,
            s.TotalCacheCreationInputTokens,
            s.TotalCacheReadInputTokens,
            s.EstimatedCostUsd,
            violationCounts.GetValueOrDefault(s.Id, 0),
            dailyUsageBySession.GetValueOrDefault(s.Id, emptyDailyUsage)))
            .ToList();

        if (request.Status is not null)
            items = items.Where(i => i.Status == request.Status.Value).ToList();

        return Result<IReadOnlyList<SessionListItem>>.Success(items);
    }
}
