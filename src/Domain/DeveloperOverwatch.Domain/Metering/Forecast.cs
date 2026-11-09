namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// Projects when a budget runs out by extending the average spend rate since the period started.
/// The projection is an estimate: it assumes future spend continues at the average rate observed so far.
/// </summary>
public static class Forecast
{
    /// <summary>
    /// Projects the budget at <paramref name="now"/>. Checks run in this order: already exceeded, no spend,
    /// no elapsed time, period over, then the rate projection. A run-out exactly at <paramref name="periodEnd"/>
    /// counts as lasting the period.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is not positive, or <paramref name="periodEnd"/> is not after <paramref name="periodStart"/>.</exception>
    public static ForecastResult Project(decimal spent, decimal budget, DateTime periodStart, DateTime periodEnd, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(periodEnd, periodStart);

        if (spent >= budget)
        {
            return ForecastResult.AlreadyExceeded();
        }

        if (spent <= 0m)
        {
            return ForecastResult.NoSpendYet();
        }

        if (now <= periodStart)
        {
            return ForecastResult.NoRateYet();
        }

        if (now >= periodEnd)
        {
            return ForecastResult.LastsPastPeriodEnd();
        }

        // remaining / (spent / elapsed), rearranged to avoid rounding the rate. Compared as double
        // before any TimeSpan is built, because a slow rate can project far beyond TimeSpan.MaxValue.
        var elapsedSeconds = (now - periodStart).TotalSeconds;
        var secondsLeft = (double)(budget - spent) * elapsedSeconds / (double)spent;
        var secondsToPeriodEnd = (periodEnd - now).TotalSeconds;

        return secondsLeft < secondsToPeriodEnd
            ? ForecastResult.RunsOut(now + TimeSpan.FromSeconds(secondsLeft))
            : ForecastResult.LastsPastPeriodEnd();
    }
}
