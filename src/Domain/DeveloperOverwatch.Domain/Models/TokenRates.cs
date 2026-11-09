namespace DeveloperOverwatch.Domain.Models;

/// <summary>USD per million tokens, by usage category.</summary>
public sealed record TokenRates(
    decimal InputPerMillionUsd,
    decimal OutputPerMillionUsd,
    decimal CacheWritePerMillionUsd,
    decimal CacheReadPerMillionUsd)
{
    private const decimal Million = 1_000_000m;

    public decimal CostOf(long inputTokens, long outputTokens, long cacheWriteTokens, long cacheReadTokens) =>
        inputTokens / Million * InputPerMillionUsd
        + outputTokens / Million * OutputPerMillionUsd
        + cacheWriteTokens / Million * CacheWritePerMillionUsd
        + cacheReadTokens / Million * CacheReadPerMillionUsd;
}
