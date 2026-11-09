namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// The share of all input tokens that were served from the prompt cache.
/// </summary>
public static class CacheHitRate
{
    /// <summary>
    /// Returns <c>cacheRead / (input + cacheRead + cacheCreate)</c>, or <c>null</c> when there were no input tokens at all
    /// (a rate of nothing is undefined, not zero).
    /// </summary>
    public static double? Of(long input, long cacheRead, long cacheCreate)
    {
        var total = input + cacheRead + cacheCreate;
        return total == 0 ? null : (double)cacheRead / total;
    }
}
