namespace DeveloperOverwatch.Application.Validation;

using DeveloperOverwatch.Domain.Entities;
using FluentValidation;

internal static class DeveloperPlanRules
{
    public const decimal MaxUsd = 100_000m;

    /// <summary>Apply to the trimmed name, which is what the entity keeps.</summary>
    public static IRuleBuilderOptions<T, string?> ValidPlanName<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MaximumLength(DeveloperPlan.PlanNameMaxLength);

    public static IRuleBuilderOptions<T, decimal> ValidMonthlyPrice<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0m).LessThanOrEqualTo(MaxUsd);

    /// <summary>Null (no limit set) passes: comparison rules skip nulls.</summary>
    public static IRuleBuilderOptions<T, decimal?> ValidWindowLimit<T>(this IRuleBuilder<T, decimal?> rule) =>
        rule.GreaterThan(0m).LessThanOrEqualTo(MaxUsd);
}
