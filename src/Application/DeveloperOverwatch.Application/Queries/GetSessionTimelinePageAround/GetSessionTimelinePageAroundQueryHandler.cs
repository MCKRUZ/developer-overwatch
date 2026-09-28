namespace DeveloperOverwatch.Application.Queries.GetSessionTimelinePageAround;

using DeveloperOverwatch.Application.Queries.GetSessionTimelinePage;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionTimelinePageAroundQueryHandler
    : IRequestHandler<GetSessionTimelinePageAroundQuery, Result<SessionTimelinePage>>
{
    private const int MaxLimit = 200;
    private readonly ISessionRepository _repository;

    public GetSessionTimelinePageAroundQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<SessionTimelinePage>> Handle(
        GetSessionTimelinePageAroundQuery request, CancellationToken cancellationToken)
    {
        if (request.TurnId is null && request.ToolExecutionId is null)
            return Result<SessionTimelinePage>.Fail("Either turnId or toolExecutionId is required");

        var limit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, MaxLimit);

        var page = await _repository.GetTimelinePageAroundAsync(
            request.SessionId, request.TurnId, request.ToolExecutionId, limit, cancellationToken);

        if (page is null)
            return Result<SessionTimelinePage>.Fail("Anchor not found in this session");

        return Result<SessionTimelinePage>.Success(new SessionTimelinePage(
            page.ConversationTurns, page.ToolExecutions, page.FileChanges, page.NextCursor, page.HasMore));
    }
}
