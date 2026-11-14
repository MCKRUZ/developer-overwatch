namespace DeveloperOverwatch.Application.Commands.DeleteDeveloperPlan;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Deletes a developer's plan; succeeds with its id.</summary>
public sealed record DeleteDeveloperPlanCommand(Guid Id) : IRequest<Result<Guid>>;
