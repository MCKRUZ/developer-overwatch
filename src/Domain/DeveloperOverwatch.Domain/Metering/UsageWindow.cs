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
    /// <para>
    /// Windows chain from earlier history, so <paramref name="activityUtc"/> must reach back at least to the last gap
    /// of <paramref name="length"/> or more (for example pass the last 24 hours, or everything since the period start).
    /// Filtering to only the last <paramref name="length"/> gives wrong windows: with activity at now-6h, now-4.5h and
    /// now-0.5h the current window starts at now-0.5h, but the last 5 hours alone would start it at now-4.5h.
    /// </para>
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
