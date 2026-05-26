namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionStartCommand : IRequest<Result<string>>
{
    public required string SessionId { get; init; }
    public required string DeveloperEmail { get; init; }
    public string? DeveloperName { get; init; }
    public string? RepoPath { get; init; }
    public required DateTime StartedAt { get; init; }
}
