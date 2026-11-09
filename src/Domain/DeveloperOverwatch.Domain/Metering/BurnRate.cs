namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// Average token and cost consumption per minute over a trailing lookback ending at a given moment.
/// </summary>
public static class BurnRate
{
    /// <summary>
    /// Sums points with <c>now - lookback &lt; AtUtc &lt;= now</c> (exclusive start, inclusive end, so adjacent
    /// lookbacks never count a point twice) and divides by the FULL lookback in minutes, not by the span between
    /// the first and last point: a quiet stretch is part of the rate, and dividing by the active span alone
    /// would overstate burn after a pause.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lookback"/> is not positive.</exception>
    public static BurnRateResult Over(IEnumerable<UsagePoint> points, DateTime now, TimeSpan lookback)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lookback, TimeSpan.Zero);

        var from = now - lookback;
        long tokens = 0;
        var cost = 0m;
        foreach (var point in points.Where(p => p.AtUtc > from && p.AtUtc <= now))
        {
            tokens += point.Tokens;
            cost += point.CostUsd;
        }

        var minutes = lookback.TotalMinutes;
        return new BurnRateResult(tokens / minutes, cost / (decimal)minutes);
    }
}
