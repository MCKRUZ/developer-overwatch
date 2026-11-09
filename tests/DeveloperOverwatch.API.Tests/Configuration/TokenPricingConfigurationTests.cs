namespace DeveloperOverwatch.API.Tests.Configuration;

using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

public class TokenPricingConfigurationTests
{
    // Same path DI takes: bind the section, then map to the Domain table.
    private static TokenPriceTable Bind(IConfiguration configuration) =>
        configuration.GetSection("TokenPricing").Get<TokenPricingOptions>()!.ToPriceTable();

    private static TokenPriceTable BindInMemory(Dictionary<string, string?> values) =>
        Bind(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Theory]
    [InlineData("claude-fable-5-1", 10.00, 50.00, 12.50, 0.25)]
    [InlineData("claude-mythos-5-1", 10.00, 50.00, 12.50, 0.25)]
    [InlineData("claude-fable-5", 10.00, 50.00, 12.50, 1.00)]
    [InlineData("claude-mythos-5", 10.00, 50.00, 12.50, 1.00)]
    [InlineData("claude-opus-5-5", 4.00, 20.00, 5.00, 0.20)]
    [InlineData("claude-opus-5", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-8", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-7", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-6", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-5", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-opus-4-1", 15.00, 75.00, 18.75, 1.50)]
    [InlineData("claude-opus-4", 15.00, 75.00, 18.75, 1.50)]
    [InlineData("claude-sonnet-5-5", 2.00, 10.00, 2.50, 0.10)]
    [InlineData("claude-sonnet-5", 2.00, 10.00, 2.50, 0.20)]
    [InlineData("claude-sonnet-4-6", 3.00, 15.00, 3.75, 0.30)]
    [InlineData("claude-sonnet-4-5", 3.00, 15.00, 3.75, 0.30)]
    [InlineData("claude-sonnet-4", 3.00, 15.00, 3.75, 0.30)]
    [InlineData("claude-haiku-5-5", 0.10, 0.50, 0.125, 0.01)]
    [InlineData("claude-haiku-4-5", 1.00, 5.00, 1.25, 0.10)]
    // Dated ids and family catch-alls (latest version's rates), and the default (Sonnet 5.5).
    [InlineData("claude-opus-5-20260101", 5.00, 25.00, 6.25, 0.50)]
    [InlineData("claude-sonnet-4-5-20250929", 3.00, 15.00, 3.75, 0.30)]
    [InlineData("claude-opus", 4.00, 20.00, 5.00, 0.20)]
    [InlineData("claude-sonnet", 2.00, 10.00, 2.50, 0.10)]
    [InlineData("claude-haiku", 0.10, 0.50, 0.125, 0.01)]
    [InlineData("claude-fable", 10.00, 50.00, 12.50, 0.25)]
    [InlineData("claude-mythos", 10.00, 50.00, 12.50, 0.25)]
    [InlineData("an-unknown-model", 2.00, 10.00, 2.50, 0.10)]
    public void RealAppSettings_StandardRates_MatchPublishedPrices(
        string model, double input, double output, double cacheWrite, double cacheRead)
    {
        var rates = Bind(RepoAppSettings.Load()).RatesFor(model).Standard;

        rates.Should().Be(new TokenRates((decimal)input, (decimal)output, (decimal)cacheWrite, (decimal)cacheRead));
    }

    [Theory]
    [InlineData("claude-haiku-5-5")]
    [InlineData("claude-haiku")]
    public void RealAppSettings_Haiku55_HasLongPromptTierAbove100kTokens(string model)
    {
        var tier = Bind(RepoAppSettings.Load()).RatesFor(model).LongPrompt;

        tier.Should().Be(new LongPromptTier(100_000, new TokenRates(0.50m, 2.50m, 0.625m, 0.05m)));
    }

    [Fact]
    public void RealAppSettings_AsOf_IsThePriceFetchDate()
    {
        Bind(RepoAppSettings.Load()).PricesAsOf.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Bind_OnlyConfiguredModels_NoBuiltInRatesLeakThrough()
    {
        var table = BindInMemory(new()
        {
            ["TokenPricing:AsOf"] = "2026-01-02",
            ["TokenPricing:Models:default:InputPerMillionUsd"] = "2",
            ["TokenPricing:Models:Claude-Opus:InputPerMillionUsd"] = "4",
        });

        table.PricesAsOf.Should().Be(new DateOnly(2026, 1, 2));
        table.RatesFor("claude-opus-9").Standard.InputPerMillionUsd.Should().Be(4m);
        table.RatesFor("claude-haiku-4-5").Standard.InputPerMillionUsd.Should().Be(2m);
    }

    [Fact]
    public void Bind_TierThresholdWithoutRates_Throws()
    {
        var act = () => BindInMemory(new()
        {
            ["TokenPricing:AsOf"] = "2026-01-02",
            ["TokenPricing:Models:default:InputPerMillionUsd"] = "2",
            ["TokenPricing:Models:default:LongPromptThresholdTokens"] = "100000",
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*default*LongPrompt*");
    }

    [Fact]
    public void Bind_MissingAsOf_Throws()
    {
        var act = () => BindInMemory(new() { ["TokenPricing:Models:default:InputPerMillionUsd"] = "2" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*AsOf*");
    }

    [Fact]
    public void Bind_MissingDefault_Throws()
    {
        var act = () => BindInMemory(new()
        {
            ["TokenPricing:AsOf"] = "2026-01-02",
            ["TokenPricing:Models:claude-opus:InputPerMillionUsd"] = "4",
        });

        act.Should().Throw<ArgumentException>().WithMessage("*default*");
    }
}
