namespace DeveloperOverwatch.Application.Queries.GetSessionModelBreakdown;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionModelBreakdownQueryHandler
    : IRequestHandler<GetSessionModelBreakdownQuery, Result<IReadOnlyList<ModelUsage>>>
{
    private const string UnknownModel = "unknown";
    private readonly ISessionRepository _repository;

    public GetSessionModelBreakdownQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<ModelUsage>>> Handle(
        GetSessionModelBreakdownQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetTurnUsageRowsAsync(request.SessionId, cancellationToken);

        var breakdown = rows
            .GroupBy(r => string.IsNullOrEmpty(r.Model) ? UnknownModel : r.Model)
            .Select(g => new ModelUsage(
                g.Key,
                g.Count(),
                g.Sum(r => (long)(r.InputTokens ?? 0)),
                g.Sum(r => (long)(r.OutputTokens ?? 0)),
                g.Sum(r => (long)(r.CacheCreationInputTokens ?? 0)),
                g.Sum(r => (long)(r.CacheReadInputTokens ?? 0)),
                g.Sum(r => r.EstimatedCostUsd ?? 0m)))
            // Synthetic/decision-marker turns and turns ingested without model metadata land in
            // an "unknown"/"<synthetic>" bucket with zero cost and zero tokens — real rows, but
            // meaningless in a *cost* breakdown, where they show up as unreadable near-zero
            // slices overlapping the dominant model's label.
            .Where(m => m.EstimatedCostUsd > 0m || m.InputTokens > 0 || m.OutputTokens > 0)
            .OrderByDescending(m => m.EstimatedCostUsd)
            .ToList();

        return Result<IReadOnlyList<ModelUsage>>.Success(breakdown);
    }
}
