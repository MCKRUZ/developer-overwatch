namespace DeveloperOverwatch.Domain.Enums;

/// <summary>Whose spending a budget limits.</summary>
public enum BudgetScope
{
    /// <summary>All developers together.</summary>
    Team,

    /// <summary>One developer, by email.</summary>
    Developer,
}
