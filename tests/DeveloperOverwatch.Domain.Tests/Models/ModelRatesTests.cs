namespace DeveloperOverwatch.Domain.Tests.Models;

using DeveloperOverwatch.Domain.Models;
using FluentAssertions;
using Xunit;

public class ModelRatesTests
{
    private static readonly TokenRates Short = new(0.10m, 0.50m, 0.125m, 0.01m);
    private static readonly TokenRates Long = new(0.50m, 2.50m, 0.625m, 0.05m);

    [Theory]
    [InlineData(0L)]
    [InlineData(100_000L)]
    public void RatesForPrompt_AtOrBelowThreshold_ReturnsStandard(long promptTokens)
    {
        new ModelRates(Short, new LongPromptTier(100_000, Long)).RatesForPrompt(promptTokens).Should().Be(Short);
    }

    [Fact]
    public void RatesForPrompt_AboveThreshold_ReturnsLongPromptRates()
    {
        new ModelRates(Short, new LongPromptTier(100_000, Long)).RatesForPrompt(100_001).Should().Be(Long);
    }

    [Fact]
    public void RatesForPrompt_NoTier_AlwaysReturnsStandard()
    {
        new ModelRates(Short).RatesForPrompt(10_000_000).Should().Be(Short);
    }
}
