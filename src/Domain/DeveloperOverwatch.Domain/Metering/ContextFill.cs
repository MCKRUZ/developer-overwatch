namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// How full a model's context window is.
/// </summary>
public static class ContextFill
{
    /// <summary>
    /// Returns <c>contextTokens / windowTokens</c>, or <c>null</c> when the window size is unknown (not positive).
    /// Deliberately not clamped: a value above 1 means the configured window size is wrong or outdated,
    /// and clamping would hide that.
    /// </summary>
    public static double? Of(long contextTokens, long windowTokens) =>
        windowTokens <= 0 ? null : (double)contextTokens / windowTokens;
}
