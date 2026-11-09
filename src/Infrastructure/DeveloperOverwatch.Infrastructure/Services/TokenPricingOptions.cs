namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Models;

/// <summary>
/// Binding shape for the "TokenPricing" section. It deliberately has no in-code rates: the binder
/// merges configured keys into whatever a dictionary already holds, so defaults here would outlive
/// a price change in appsettings. The Domain table supplies case-insensitive lookup.
/// </summary>
public sealed class TokenPricingOptions
{
    public DateOnly AsOf { get; set; }
    public Dictionary<string, ModelRatesOptions> Models { get; set; } = [];

    public TokenPriceTable ToPriceTable()
    {
        if (AsOf == default)
            throw new InvalidOperationException("TokenPricing:AsOf must be set to the date the rates were published.");

        return new TokenPriceTable(
            AsOf, Models.Select(m => KeyValuePair.Create(m.Key, m.Value.ToModelRates(m.Key))));
    }
}
