namespace DeveloperOverwatch.Domain.Interfaces;

public interface ITokenPricingCalculator
{
    /// <summary>
    /// Estimated USD cost for a single turn's token usage, priced according to the given model's
    /// configured rates (falls back to a default rate when <paramref name="model"/> is null or
    /// has no matching entry — e.g. teams that mix Sonnet/Opus/Haiku get correct per-model cost).
    /// </summary>
    decimal CalculateCost(
        string? model,
        int? inputTokens,
        int? outputTokens,
        int? cacheCreationInputTokens,
        int? cacheReadInputTokens);
}
