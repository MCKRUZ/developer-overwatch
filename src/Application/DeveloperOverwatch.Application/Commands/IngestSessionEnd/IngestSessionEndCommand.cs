namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionEndCommand : IRequest<Result<IngestOutcome>>
{
    public required string SessionId { get; init; }

    /// <summary>The Agent's id for this end event. An end whose id was already processed is
    /// reported as a duplicate. Absent from older Agents, which get the previous behaviour.</summary>
    public string? EventId { get; init; }

    public required DateTime EndedAt { get; init; }
    public string? SummaryJson { get; init; }
}
