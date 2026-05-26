namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ResolveViolationCommand : IRequest<Result<string>>
{
    public required string ViolationId { get; init; }
    public required string ResolvedBy { get; init; }
    public required string ResolutionReason { get; init; }
}
