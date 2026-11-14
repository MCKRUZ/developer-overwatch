namespace DeveloperOverwatch.Application.Validation;

using FluentValidation;

internal static class BudgetRules
{
    public const decimal MaxAmountUsd = 1_000_000m;

    public static IRuleBuilderOptions<T, decimal> ValidBudgetAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0m).LessThanOrEqualTo(MaxAmountUsd);
}
