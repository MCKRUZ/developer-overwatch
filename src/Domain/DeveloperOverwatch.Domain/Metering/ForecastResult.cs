namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// The outcome of <see cref="Forecast.Project"/>. <see cref="RunsOutAt"/> is set only for
/// <see cref="ForecastKind.RunsOutWithinPeriod"/>; the factory methods are the only way to build one,
/// so the two fields can never disagree. Any run-out time is an estimate from the average rate so far.
/// </summary>
public sealed record ForecastResult
{
    private ForecastResult(ForecastKind kind, DateTime? runsOutAt)
    {
        Kind = kind;
        RunsOutAt = runsOutAt;
    }

    /// <summary>Which forecast outcome applies.</summary>
    public ForecastKind Kind { get; }

    /// <summary>Estimated moment the budget is exhausted; only set for <see cref="ForecastKind.RunsOutWithinPeriod"/>.</summary>
    public DateTime? RunsOutAt { get; }

    /// <summary>Spend has already reached the budget.</summary>
    public static ForecastResult AlreadyExceeded() => new(ForecastKind.AlreadyExceeded, null);

    /// <summary>No spend yet, so no rate.</summary>
    public static ForecastResult NoSpendYet() => new(ForecastKind.NoSpendYet, null);

    /// <summary>No elapsed time yet, so no rate.</summary>
    public static ForecastResult NoRateYet() => new(ForecastKind.NoRateYet, null);

    /// <summary>The budget is estimated to run out at <paramref name="at"/>, before the period ends.</summary>
    public static ForecastResult RunsOut(DateTime at) => new(ForecastKind.RunsOutWithinPeriod, at);

    /// <summary>The budget is estimated to last at least to the end of the period.</summary>
    public static ForecastResult LastsPastPeriodEnd() => new(ForecastKind.LastsPastPeriodEnd, null);
}
