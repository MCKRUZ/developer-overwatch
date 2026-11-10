namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionQueryHandler
    : IRequestHandler<GetSessionQuery, Result<SessionDetail>>
{
    private readonly ISessionRepository _repository;
    private readonly ISessionStaleThresholdProvider _staleThreshold;

    public GetSessionQueryHandler(ISessionRepository repository, ISessionStaleThresholdProvider staleThreshold)
    {
        _repository = repository;
        _staleThreshold = staleThreshold;
    }

    public async Task<Result<SessionDetail>> Handle(
        GetSessionQuery request, CancellationToken cancellationToken)
    {
        var projection = await _repository.GetDetailAsync(request.SessionId, cancellationToken);
        if (projection is null)
            return Result<SessionDetail>.Fail($"Session {request.SessionId} not found");

        var s = projection.Session;
        var staleCutoff = DateTime.UtcNow.AddMinutes(-_staleThreshold.StaleAfterMinutes);
        var effectiveStatus = s.Status == SessionStatus.Active && s.LastActivityAt < staleCutoff
            ? SessionStatus.Stale
            : s.Status;

        var detail = new SessionDetail(
            s.Id,
            s.DeveloperEmail,
            s.DeveloperName,
            s.RepoPath,
            s.Title,
            effectiveStatus,
            s.StartedAt,
            s.EndedAt,
            s.SummaryJson,
            s.CreatedAt,
            s.TotalInputTokens,
            s.TotalOutputTokens,
            s.TotalCacheCreationInputTokens,
            s.TotalCacheReadInputTokens,
            s.EstimatedCostUsd,
            projection.ConversationTurnCount,
            projection.ToolExecutionCount,
            projection.FileChangeCount,
            s.GovernanceViolations.ToList(),
            s.ArchitectAnnotations.ToList(),
            s.Agent,
            s.MachineName,
            s.ProjectKey,
            s.PeakContextTokens,
            s.ActivityState,
            s.ActivityStateAt,
            s.ToolErrorCount,
            s.LastToolErrorName,
            s.LastToolErrorAt);

        return Result<SessionDetail>.Success(detail);
    }
}
