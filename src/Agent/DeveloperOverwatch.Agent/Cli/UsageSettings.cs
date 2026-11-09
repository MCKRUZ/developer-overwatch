using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Domain.Models;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>The prices and budget the usage report uses: config.json's sections when present, else the built-in defaults.</summary>
public sealed record UsageSettings(TokenPriceTable Prices, BudgetSettings Budget)
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool TryLoad(
        AgentConfig config,
        [NotNullWhen(true)] out UsageSettings? settings,
        [NotNullWhen(false)] out string? problem)
    {
        settings = null;
        if (!TryReadPrices(config.Pricing, out var prices, out problem))
            return false;
        if (!TryReadBudget(config.Budget, out var budget, out problem))
            return false;

        settings = new UsageSettings(prices, budget);
        return true;
    }

    private static bool TryReadPrices(
        JsonElement? json, [NotNullWhen(true)] out TokenPriceTable? prices, [NotNullWhen(false)] out string? problem)
    {
        problem = null;
        if (!IsPresent(json))
        {
            prices = PricingSection.ReadDefaults().ToPriceTable();
            return true;
        }

        try
        {
            prices = PricingSection.Read(json!.Value).ToPriceTable();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            prices = null;
            problem = $"The 'pricing' section in config.json is invalid: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadBudget(
        JsonElement? json, [NotNullWhen(true)] out BudgetSettings? budget, [NotNullWhen(false)] out string? problem)
    {
        problem = null;
        if (!IsPresent(json))
        {
            budget = BudgetSettings.None;
            return true;
        }

        try
        {
            budget = json!.Value.Deserialize<BudgetSettings>(ReadOptions) ?? BudgetSettings.None;
            return true;
        }
        catch (JsonException ex)
        {
            budget = null;
            problem = $"The 'budget' section in config.json is invalid: {ex.Message}";
            return false;
        }
    }

    private static bool IsPresent(JsonElement? json) =>
        json is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };
}
