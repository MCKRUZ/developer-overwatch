namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPushWebhookCommand : IRequest<Result<int>>
{
    public required string Repo { get; init; }
    public required IReadOnlyList<PushCommitDto> Commits { get; init; }
}

public sealed record PushCommitDto
{
    public required string Sha { get; init; }
    public required string Message { get; init; }
    public required DateTime Timestamp { get; init; }
}
