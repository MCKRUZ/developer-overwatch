using System.Text.Json;
using DeveloperOverwatch.Domain.Models;

namespace DeveloperOverwatch.Agent.Models;

/// <summary>
/// A price list: the built-in defaults shipped inside the agent, or config.json's <c>pricing</c> section,
/// which replaces them wholesale (it must carry its own <c>asOf</c> and a <c>default</c> model). Same shape
/// as the server's TokenPricing section.
/// </summary>
public sealed class PricingSection
{
    private const string DefaultsResource = "DeveloperOverwatch.Agent.default-prices.json";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public DateOnly AsOf { get; init; }
    public Dictionary<string, ModelRatesSection?>? Models { get; init; }

    /// <exception cref="JsonException">The JSON does not have the pricing shape.</exception>
    public static PricingSection Read(JsonElement json) =>
        json.Deserialize<PricingSection>(ReadOptions) ?? throw new JsonException("The pricing section is null.");

    public static PricingSection ReadDefaults()
    {
        using var stream = typeof(PricingSection).Assembly.GetManifestResourceStream(DefaultsResource)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultsResource}' is missing from the agent.");
        using var document = JsonDocument.Parse(stream);
        return Read(document.RootElement);
    }

    /// <exception cref="InvalidOperationException">No <c>asOf</c>, no models, or an invalid model entry.</exception>
    /// <exception cref="ArgumentException">No <c>default</c> model, or two keys differ only by case.</exception>
    public TokenPriceTable ToPriceTable()
    {
        if (AsOf == default)
            throw new InvalidOperationException("asOf must be set to the date the rates were published.");
        if (Models is null || Models.Count == 0)
            throw new InvalidOperationException("models must list at least a 'default' entry.");

        return new TokenPriceTable(AsOf, Models.Select(m => KeyValuePair.Create(
            m.Key,
            (m.Value ?? throw new InvalidOperationException($"Model '{m.Key}' has no rates.")).ToModelRates(m.Key))));
    }
}
