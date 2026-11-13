namespace DeveloperOverwatch.Application.Queries.GetBudgets;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Every budget: the team first, then developers by email, daily before monthly.</summary>
public sealed record GetBudgetsQuery : IRequest<Result<IReadOnlyList<BudgetDto>>>;
