namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// How spending compares with the passage of time through a budget period.
/// </summary>
public enum PaceStatus
{
    /// <summary>Spending faster than time is passing; the limit will be reached early if this continues.</summary>
    Ahead,

    /// <summary>Spending roughly in step with time, within the tolerance.</summary>
    OnPace,

    /// <summary>Spending slower than time is passing.</summary>
    Behind,
}
