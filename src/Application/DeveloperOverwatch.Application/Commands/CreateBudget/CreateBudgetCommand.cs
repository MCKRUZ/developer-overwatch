namespace DeveloperOverwatch.Application.Commands.CreateBudget;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

/// <param name="DeveloperEmail">Required for <see cref="BudgetScope.Developer"/> (trimmed); null for the team.</param>
public sealed record CreateBudgetCommand(
    BudgetScope Scope,
    string? DeveloperEmail,
    BudgetPeriod Period,
    decimal AmountUsd) : IRequest<Result<BudgetDto>>;
