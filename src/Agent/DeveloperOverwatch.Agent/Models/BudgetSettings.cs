namespace DeveloperOverwatch.Agent.Models;

/// <summary>config.json's optional <c>budget</c> section, in USD. A missing, zero or negative limit is not shown.</summary>
public sealed record BudgetSettings(decimal? DailyUsd = null, decimal? MonthlyUsd = null)
{
    public static readonly BudgetSettings None = new();
}
