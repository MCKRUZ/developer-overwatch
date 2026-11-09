namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class CacheHitRateTests
{
    [Fact]
    public void Of_MixedTokens_ReturnsReadShareOfAllInput()
    {
        CacheHitRate.Of(input: 100, cacheRead: 300, cacheCreate: 100).Should().Be(0.6);
    }

    [Fact]
    public void Of_NoTokens_ReturnsNull()
    {
        CacheHitRate.Of(0, 0, 0).Should().BeNull();
    }

    [Fact]
    public void Of_NoCacheReads_ReturnsZero()
    {
        CacheHitRate.Of(500, 0, 500).Should().Be(0d);
    }
}
