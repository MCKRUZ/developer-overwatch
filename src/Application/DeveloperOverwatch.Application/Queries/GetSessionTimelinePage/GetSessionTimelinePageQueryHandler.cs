namespace DeveloperOverwatch.Application.Queries.GetSessionTimelinePage;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionTimelinePageQueryHandler
    : IRequestHandler<GetSessionTimelinePageQuery, Result<SessionTimelinePage>>
{
    private const int MaxLimit = 200;
    private readonly ISessionRepository _repository;

    public GetSessionTimelinePageQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<SessionTimelinePage>> Handle(
        GetSessionTimelinePageQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, MaxLimit);

        var page = await _repository.GetTimelinePageAsync(
            request.SessionId, request.Before, limit, cancellationToken);

        return Result<SessionTimelinePage>.Success(new SessionTimelinePage(
            page.ConversationTurns, page.ToolExecutions, page.FileChanges, page.NextCursor, page.HasMore));
    }
}
