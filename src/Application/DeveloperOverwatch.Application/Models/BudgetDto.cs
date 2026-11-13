namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;

/// <summary>A budget as the dashboard sees it. <see cref="DeveloperEmail"/> is null for the team budget and is
/// written as an explicit null.</summary>
public sealed record BudgetDto(
    Guid Id,
    BudgetScope Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DeveloperEmail,
    BudgetPeriod Period,
    decimal AmountUsd,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static BudgetDto From(Budget budget) => new(
        budget.Id, budget.Scope, budget.DeveloperEmail, budget.Period, budget.AmountUsd, budget.CreatedAt, budget.UpdatedAt);
}
