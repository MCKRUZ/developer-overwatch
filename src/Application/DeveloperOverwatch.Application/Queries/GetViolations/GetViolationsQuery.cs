namespace DeveloperOverwatch.Application.Queries.GetViolations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record GetViolationsQuery : IRequest<Result<IReadOnlyList<GovernanceViolation>>>
{
    public required string SessionId { get; init; }
    public ViolationSeverity? Severity { get; init; }
}
