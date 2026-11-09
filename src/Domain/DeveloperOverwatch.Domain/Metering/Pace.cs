namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// Compares the share of a limit already used with the share of its period already elapsed,
/// to say whether spending is running ahead of time, on pace, or behind it.
/// </summary>
public static class Pace
{
    /// <summary>The default band, in fraction-of-limit units, within which spending counts as on pace.</summary>
    public const decimal DefaultTolerance = 0.05m;

    /// <summary>
    /// Computes pace at <paramref name="now"/>. Status is <see cref="PaceStatus.Ahead"/> when the used fraction exceeds
    /// the elapsed fraction by more than <paramref name="tolerance"/>, <see cref="PaceStatus.Behind"/> when it trails by more,
    /// otherwise <see cref="PaceStatus.OnPace"/>. The elapsed fraction is clamped to [0, 1].
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is not positive, or <paramref name="periodEnd"/> is not after <paramref name="periodStart"/>.</exception>
    public static PaceResult Compute(
        decimal used,
        decimal limit,
        DateTime periodStart,
        DateTime periodEnd,
        DateTime now,
        decimal tolerance = DefaultTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(periodEnd, periodStart);

        var usedFraction = used / limit;
        var elapsed = (decimal)(now - periodStart).Ticks / (periodEnd - periodStart).Ticks;
        var elapsedFraction = Math.Clamp(elapsed, 0m, 1m);

        var status = usedFraction > elapsedFraction + tolerance ? PaceStatus.Ahead
            : usedFraction < elapsedFraction - tolerance ? PaceStatus.Behind
            : PaceStatus.OnPace;

        return new PaceResult(usedFraction, elapsedFraction, status);
    }
}
