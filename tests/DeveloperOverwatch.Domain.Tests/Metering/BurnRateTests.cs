namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class BurnRateTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    [Fact]
    public void Over_PointsInLookback_DividesByFullLookback()
    {
        UsagePoint[] points =
        [
            new(Now.AddMinutes(-9), 600, 1.00m),
            new(Now.AddMinutes(-1), 400, 0.50m),
        ];

        var result = BurnRate.Over(points, Now, TenMinutes);

        result.Should().Be(new BurnRateResult(100d, 0.15m));
    }

    [Fact]
    public void Over_PointExactlyAtLookbackStart_IsExcluded()
    {
        UsagePoint[] points = [new(Now - TenMinutes, 1000, 1m)];

        BurnRate.Over(points, Now, TenMinutes).Should().Be(new BurnRateResult(0d, 0m));
    }

    [Fact]
    public void Over_PointExactlyAtNow_IsIncluded()
    {
        UsagePoint[] points = [new(Now, 1000, 1m)];

        BurnRate.Over(points, Now, TenMinutes).Should().Be(new BurnRateResult(100d, 0.1m));
    }

    [Fact]
    public void Over_FuturePoint_IsExcluded()
    {
        UsagePoint[] points = [new(Now.AddSeconds(1), 1000, 1m)];

        BurnRate.Over(points, Now, TenMinutes).Should().Be(new BurnRateResult(0d, 0m));
    }

    [Fact]
    public void Over_NoPoints_ReturnsZero()
    {
        BurnRate.Over([], Now, TenMinutes).Should().Be(new BurnRateResult(0d, 0m));
    }

    [Fact]
    public void Over_NonPositiveLookback_Throws()
    {
        var act = () => BurnRate.Over([], Now, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
