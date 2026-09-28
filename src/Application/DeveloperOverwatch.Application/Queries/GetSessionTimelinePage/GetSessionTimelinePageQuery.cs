namespace DeveloperOverwatch.Application.Queries.GetSessionTimelinePage;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetSessionTimelinePageQuery(
    string SessionId, DateTime? Before, int Limit) : IRequest<Result<SessionTimelinePage>>;

public sealed record SessionTimelinePage(
    IReadOnlyList<ConversationTurn> ConversationTurns,
    IReadOnlyList<ToolExecution> ToolExecutions,
    IReadOnlyList<FileChange> FileChanges,
    DateTime? NextCursor,
    bool HasMore);
