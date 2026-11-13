namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Entities;

/// <summary>A developer's plan as the dashboard sees it. <see cref="WindowLimitUsd"/> is null when the lead has not
/// set one and is written as an explicit null.</summary>
public sealed record DeveloperPlanDto(
    Guid Id,
    string DeveloperEmail,
    string PlanName,
    decimal MonthlyPriceUsd,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? WindowLimitUsd,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static DeveloperPlanDto From(DeveloperPlan plan) => new(
        plan.Id, plan.DeveloperEmail, plan.PlanName, plan.MonthlyPriceUsd, plan.WindowLimitUsd, plan.CreatedAt, plan.UpdatedAt);
}
