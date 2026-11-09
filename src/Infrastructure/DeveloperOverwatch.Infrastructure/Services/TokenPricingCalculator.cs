namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Options;

/// <summary>USD per million tokens, by usage category, for one model.</summary>
public sealed class ModelRates
{
    public decimal InputPerMillionUsd { get; set; }
    public decimal OutputPerMillionUsd { get; set; }
    public decimal CacheWritePerMillionUsd { get; set; }
    public decimal CacheReadPerMillionUsd { get; set; }
}

/// <summary>
/// Per-model pricing table, keyed by model name/prefix (case-insensitive). "default" is used
/// when a turn's model doesn't match any other configured entry — e.g. a new model not yet
/// added here, or a turn where the model name wasn't captured. Defaults below are approximate
/// current Claude list pricing; override via the "TokenPricing" appsettings section to match
/// your actual rates/models.
/// </summary>
public sealed class TokenPricingConfig
{
    public Dictionary<string, ModelRates> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new ModelRates
        {
            InputPerMillionUsd = 3.00m, OutputPerMillionUsd = 15.00m,
            CacheWritePerMillionUsd = 3.75m, CacheReadPerMillionUsd = 0.30m
        },
        ["claude-opus"] = new ModelRates
        {
            InputPerMillionUsd = 15.00m, OutputPerMillionUsd = 75.00m,
            CacheWritePerMillionUsd = 18.75m, CacheReadPerMillionUsd = 1.50m
        },
        ["claude-sonnet"] = new ModelRates
        {
            InputPerMillionUsd = 3.00m, OutputPerMillionUsd = 15.00m,
            CacheWritePerMillionUsd = 3.75m, CacheReadPerMillionUsd = 0.30m
        },
        ["claude-haiku"] = new ModelRates
        {
            InputPerMillionUsd = 1.00m, OutputPerMillionUsd = 5.00m,
            CacheWritePerMillionUsd = 1.25m, CacheReadPerMillionUsd = 0.10m
        },
    };
}

public sealed class TokenPricingCalculator : ITokenPricingCalculator
{
    private readonly TokenPricingConfig _config;

    public TokenPricingCalculator(IOptions<TokenPricingConfig> config)
    {
        _config = config.Value;
    }

    public decimal CalculateCost(
        string? model,
        int? inputTokens,
        int? outputTokens,
        int? cacheCreationInputTokens,
        int? cacheReadInputTokens)
    {
        const decimal million = 1_000_000m;
        var rates = ResolveRates(model);

        return (inputTokens ?? 0) / million * rates.InputPerMillionUsd
            + (outputTokens ?? 0) / million * rates.OutputPerMillionUsd
            + (cacheCreationInputTokens ?? 0) / million * rates.CacheWritePerMillionUsd
            + (cacheReadInputTokens ?? 0) / million * rates.CacheReadPerMillionUsd;
    }

    private ModelRates ResolveRates(string? model)
    {
        var fallback = _config.Models.GetValueOrDefault("default") ?? new ModelRates();

        if (string.IsNullOrEmpty(model))
            return fallback;

        if (_config.Models.TryGetValue(model, out var exact))
            return exact;

        // Model names carry version suffixes (e.g. "claude-sonnet-4-5-20250514"); match by
        // configured prefix (e.g. "claude-sonnet") so new dated variants still price correctly.
        foreach (var (key, rates) in _config.Models)
        {
            if (!string.Equals(key, "default", StringComparison.OrdinalIgnoreCase) &&
                model.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                return rates;
            }
        }

        return fallback;
    }
}
