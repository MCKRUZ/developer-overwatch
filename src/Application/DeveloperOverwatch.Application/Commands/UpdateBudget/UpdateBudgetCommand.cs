namespace DeveloperOverwatch.Application.Commands.UpdateBudget;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>Changes a budget's amount. Scope, developer and period are its identity and never change.</summary>
public sealed record UpdateBudgetCommand(Guid Id, decimal AmountUsd) : IRequest<Result<BudgetDto>>;
