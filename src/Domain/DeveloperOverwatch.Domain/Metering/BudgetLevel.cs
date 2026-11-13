namespace DeveloperOverwatch.Domain.Metering;

/// <summary>How close spending is to a limit, as shown on the dashboard.</summary>
public enum BudgetLevel
{
    /// <summary>Under three quarters used.</summary>
    Ok,

    /// <summary>At least three quarters used, not yet all of it.</summary>
    Amber,

    /// <summary>The limit is used up or exceeded.</summary>
    Red,
}
