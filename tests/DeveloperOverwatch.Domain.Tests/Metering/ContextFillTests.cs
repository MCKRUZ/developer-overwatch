namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class ContextFillTests
{
    [Fact]
    public void Of_PartlyFull_ReturnsFraction()
    {
        ContextFill.Of(50_000, 200_000).Should().Be(0.25);
    }

    [Fact]
    public void Of_OverWindow_IsNotClamped()
    {
        ContextFill.Of(300_000, 200_000).Should().Be(1.5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Of_NonPositiveWindow_ReturnsNull(long window)
    {
        ContextFill.Of(1000, window).Should().BeNull();
    }
}
