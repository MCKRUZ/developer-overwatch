namespace DeveloperOverwatch.Application.Commands.DeleteBudget;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Deletes a budget; succeeds with its id.</summary>
public sealed record DeleteBudgetCommand(Guid Id) : IRequest<Result<Guid>>;
