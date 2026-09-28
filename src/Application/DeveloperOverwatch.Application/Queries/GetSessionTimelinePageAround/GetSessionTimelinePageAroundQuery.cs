namespace DeveloperOverwatch.Application.Queries.GetSessionTimelinePageAround;

using DeveloperOverwatch.Application.Queries.GetSessionTimelinePage;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetSessionTimelinePageAroundQuery(
    string SessionId, string? TurnId, string? ToolExecutionId, int Limit)
    : IRequest<Result<SessionTimelinePage>>;
