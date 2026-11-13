namespace DeveloperOverwatch.Domain.Enums;

/// <summary>The span a budget resets over, in the viewer's local time.</summary>
public enum BudgetPeriod
{
    /// <summary>The viewer's local calendar day.</summary>
    Daily,

    /// <summary>The viewer's local calendar month.</summary>
    Monthly,
}
