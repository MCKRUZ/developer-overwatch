namespace DeveloperOverwatch.Application.Tests.Services;

using DeveloperOverwatch.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

public class TokenPricingCalculatorTests
{
    private static TokenPricingCalculator CreateSut(TokenPricingConfig? config = null) =>
        new(Options.Create(config ?? new TokenPricingConfig()));

    [Fact]
    public void CalculateCost_NoModel_UsesDefaultRates()
    {
        var sut = CreateSut();

        // default: $3/MTok input, $15/MTok output
        var cost = sut.CalculateCost(null, 1_000_000, 1_000_000, null, null);

        cost.Should().Be(18.00m);
    }

    [Fact]
    public void CalculateCost_UnconfiguredModel_FallsBackToDefault()
    {
        var sut = CreateSut();

        var cost = sut.CalculateCost("some-brand-new-model-nobody-configured-yet", 1_000_000, 0, null, null);

        cost.Should().Be(3.00m); // default input rate
    }

    [Fact]
    public void CalculateCost_ModelMatchingFamilyPrefix_UsesFamilyRate()
    {
        var sut = CreateSut();

        // "claude-opus-4-1-20250805"-style version suffix should still match the "claude-opus" family rate
        var cost = sut.CalculateCost("claude-opus-4-1-20250805", 1_000_000, 0, null, null);

        cost.Should().Be(15.00m); // opus input rate, not the $3 default/sonnet rate
    }

    [Fact]
    public void CalculateCost_ExactVersionOverride_TakesPrecedenceOverFamilyPrefix()
    {
        // Two Sonnet versions with different rates configured explicitly by exact model string —
        // proves version-level differentiation is possible, not just family-level (opus/sonnet/haiku).
        var config = new TokenPricingConfig
        {
            Models = new(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new ModelRates { InputPerMillionUsd = 3.00m },
                ["claude-sonnet"] = new ModelRates { InputPerMillionUsd = 3.00m },
                ["claude-sonnet-4-8"] = new ModelRates { InputPerMillionUsd = 2.50m },
                ["claude-sonnet-5"] = new ModelRates { InputPerMillionUsd = 4.00m },
            }
        };
        var sut = CreateSut(config);

        sut.CalculateCost("claude-sonnet-4-8", 1_000_000, 0, null, null).Should().Be(2.50m);
        sut.CalculateCost("claude-sonnet-5", 1_000_000, 0, null, null).Should().Be(4.00m);
        // A sonnet version with no exact entry still falls back to the family prefix rate.
        sut.CalculateCost("claude-sonnet-4-9", 1_000_000, 0, null, null).Should().Be(3.00m);
    }
}
