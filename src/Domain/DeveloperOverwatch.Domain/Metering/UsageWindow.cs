namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// Reconstructs Claude-style rolling usage windows (5-hour blocks by default) from activity timestamps.
/// A window opens at the first activity and lasts a fixed length; the first activity at or after its end
/// opens the next one. The result is an estimate: the provider's own window boundaries are not observable,
/// so this reproduces their documented rule from the activity we recorded.
/// </summary>
public static class UsageWindow
{
    /// <summary>The window length used when none is supplied.</summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromHours(5);

    /// <summary>
    /// Returns the estimated window containing <paramref name="nowUtc"/>, or <c>null</c> when no window is open
    /// (no activity yet, or the latest window has already reset). Activity after <paramref name="nowUtc"/> is ignored;
    /// activity exactly at <paramref name="nowUtc"/> counts. Input need not be sorted.
    /// </summary>
    /// <param name="activityUtc">UTC timestamps of usage activity.</param>
    /// <param name="nowUtc">The moment to evaluate; passed in so the function never reads a clock.</param>
    /// <param name="length">Window length; defaults to <see cref="DefaultLength"/>. Must be positive.</param>
    public static UsageWindowResult? Current(IEnumerable<DateTime> activityUtc, DateTime nowUtc, TimeSpan? length = null)
    {
        ArgumentNullException.ThrowIfNull(activityUtc);
        var windowLength = length ?? DefaultLength;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(windowLength, TimeSpan.Zero, nameof(length));

        DateTime? start = null;
        foreach (var t in activityUtc.Where(t => t <= nowUtc).Order())
        {
            if (start is null || t >= start.Value + windowLength)
            {
                start = t;
            }
        }

        if (start is null)
        {
            return null;
        }

        var resetsAt = start.Value + windowLength;
        return nowUtc < resetsAt ? new UsageWindowResult(start.Value, resetsAt) : null;
    }
}
