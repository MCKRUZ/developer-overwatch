namespace DeveloperOverwatch.Domain.Models;

using DeveloperOverwatch.Domain.Interfaces;

/// <summary>Model prices as published on <see cref="PricesAsOf"/>, resolved per model id.</summary>
public sealed class TokenPriceTable : ITokenPricingCalculator
{
    private readonly ModelKeyLookup<ModelRates> _rates;

    /// <exception cref="ArgumentException">No "default" entry, or two keys differ only by case.</exception>
    public TokenPriceTable(DateOnly pricesAsOf, IEnumerable<KeyValuePair<string, ModelRates>> models)
    {
        PricesAsOf = pricesAsOf;
        _rates = new ModelKeyLookup<ModelRates>(models);
    }

    public DateOnly PricesAsOf { get; }

    public ModelRates RatesFor(string? model) => _rates.Resolve(model);

    /// <remarks>
    /// A turn is treated as one API request, so its input + cache write + cache read tokens are the
    /// prompt length that selects a long-prompt tier.
    /// </remarks>
    public decimal CalculateCost(
        string? model,
        int? inputTokens,
        int? outputTokens,
        int? cacheCreationInputTokens,
        int? cacheReadInputTokens)
    {
        long input = inputTokens ?? 0;
        long cacheWrite = cacheCreationInputTokens ?? 0;
        long cacheRead = cacheReadInputTokens ?? 0;

        return RatesFor(model)
            .RatesForPrompt(input + cacheWrite + cacheRead)
            .CostOf(input, outputTokens ?? 0, cacheWrite, cacheRead);
    }
}
