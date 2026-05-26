namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionEndCommand : IRequest<Result<string>>
{
    public required string SessionId { get; init; }
    public required DateTime EndedAt { get; init; }
    public string? SummaryJson { get; init; }
}
