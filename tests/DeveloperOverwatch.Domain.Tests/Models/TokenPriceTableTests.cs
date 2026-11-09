namespace DeveloperOverwatch.Domain.Tests.Models;

using DeveloperOverwatch.Domain.Models;
using FluentAssertions;
using Xunit;

public class TokenPriceTableTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 9);
    private static readonly TokenRates Sonnet = new(2.00m, 10.00m, 2.50m, 0.10m);
    private static readonly TokenRates Opus = new(4.00m, 20.00m, 5.00m, 0.20m);
    private static readonly TokenRates HaikuShort = new(0.10m, 0.50m, 0.125m, 0.01m);
    private static readonly TokenRates HaikuLong = new(0.50m, 2.50m, 0.625m, 0.05m);

    private static TokenPriceTable CreateSut() => new(AsOf, new Dictionary<string, ModelRates>
    {
        ["default"] = new(Sonnet),
        ["claude-opus-5-5"] = new(Opus),
        ["claude-haiku-5-5"] = new(HaikuShort, new LongPromptTier(100_000, HaikuLong)),
    });

    [Fact]
    public void PricesAsOf_ReturnsTheTableDate()
    {
        CreateSut().PricesAsOf.Should().Be(AsOf);
    }

    [Fact]
    public void CalculateCost_PricesEachCategoryAtItsOwnRate()
    {
        var cost = CreateSut().CalculateCost("claude-opus-5-5", 1_000_000, 1_000_000, 1_000_000, 1_000_000);

        cost.Should().Be(4.00m + 20.00m + 5.00m + 0.20m);
    }

    [Fact]
    public void CalculateCost_NullTokenCounts_CountAsZero()
    {
        CreateSut().CalculateCost("claude-opus-5-5", null, null, null, null).Should().Be(0m);
    }

    [Fact]
    public void CalculateCost_UnknownOrMissingModel_UsesDefaultRates()
    {
        var sut = CreateSut();

        sut.CalculateCost(null, 1_000_000, 0, null, null).Should().Be(2.00m);
        sut.CalculateCost("some-brand-new-model", 1_000_000, 0, null, null).Should().Be(2.00m);
    }

    [Fact]
    public void CalculateCost_DatedVariant_UsesVersionRate()
    {
        CreateSut().CalculateCost("claude-opus-5-5-20260101", 1_000_000, 0, null, null).Should().Be(4.00m);
    }

    [Fact]
    public void CalculateCost_PromptAtLongPromptThreshold_UsesStandardRates()
    {
        // Prompt = input + cache write + cache read = 100,000: still the short tier.
        var cost = CreateSut().CalculateCost("claude-haiku-5-5", 40_000, 1_000_000, 30_000, 30_000);

        cost.Should().Be(
            0.04m * HaikuShort.InputPerMillionUsd + HaikuShort.OutputPerMillionUsd
            + 0.03m * HaikuShort.CacheWritePerMillionUsd + 0.03m * HaikuShort.CacheReadPerMillionUsd);
    }

    [Fact]
    public void CalculateCost_PromptOneTokenOverThreshold_UsesLongPromptRatesForEveryCategory()
    {
        var cost = CreateSut().CalculateCost("claude-haiku-5-5", 40_001, 1_000_000, 30_000, 30_000);

        cost.Should().Be(
            0.040001m * HaikuLong.InputPerMillionUsd + HaikuLong.OutputPerMillionUsd
            + 0.03m * HaikuLong.CacheWritePerMillionUsd + 0.03m * HaikuLong.CacheReadPerMillionUsd);
    }

    [Fact]
    public void CalculateCost_PromptCountsSumBeyondInt32_DoNotOverflow()
    {
        var cost = CreateSut().CalculateCost("claude-haiku-5-5", int.MaxValue, 0, int.MaxValue, int.MaxValue);

        cost.Should().BePositive();
    }

    [Fact]
    public void RatesFor_ResolvesConfiguredModelRates()
    {
        CreateSut().RatesFor("claude-haiku-5-5").LongPrompt.Should().Be(new LongPromptTier(100_000, HaikuLong));
    }
}
