namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// The kind of answer <see cref="Forecast.Project"/> gives about when a budget runs out.
/// </summary>
public enum ForecastKind
{
    /// <summary>Spend has already reached or passed the budget.</summary>
    AlreadyExceeded,

    /// <summary>Nothing has been spent yet, so there is no rate to project from.</summary>
    NoSpendYet,

    /// <summary>The period has not started (no elapsed time), so a rate cannot be computed.</summary>
    NoRateYet,

    /// <summary>At the current average rate the budget runs out before the period ends.</summary>
    RunsOutWithinPeriod,

    /// <summary>At the current average rate the budget lasts at least until the period ends.</summary>
    LastsPastPeriodEnd,
}
