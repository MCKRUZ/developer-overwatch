namespace DeveloperOverwatch.Application.Queries.GetSessionDailyUsage;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionDailyUsageQueryHandler
    : IRequestHandler<GetSessionDailyUsageQuery, Result<IReadOnlyList<DailyUsage>>>
{
    private readonly ISessionRepository _repository;

    public GetSessionDailyUsageQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<DailyUsage>>> Handle(
        GetSessionDailyUsageQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetTurnUsageRowsAsync(request.SessionId, cancellationToken);

        var daily = rows
            .GroupBy(r => DateOnly.FromDateTime(r.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g => new DailyUsage(
                g.Key,
                g.Count(),
                g.Sum(r => r.InputTokens ?? 0),
                g.Sum(r => r.OutputTokens ?? 0),
                g.Sum(r => r.CacheCreationInputTokens ?? 0),
                g.Sum(r => r.CacheReadInputTokens ?? 0),
                g.Sum(r => r.EstimatedCostUsd ?? 0m)))
            .ToList();

        return Result<IReadOnlyList<DailyUsage>>.Success(daily);
    }
}
