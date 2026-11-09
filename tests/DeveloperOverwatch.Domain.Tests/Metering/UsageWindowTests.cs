namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class UsageWindowTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Current_HistoryBeyondWindowLength_ChainsFromEarlierBlock()
    {
        var now = T0.AddHours(12);
        DateTime[] activity = [now.AddHours(-6), now.AddMinutes(-270), now.AddMinutes(-30)];

        // The block opened at now-6h ends at now-1h, swallowing now-4.5h; now-0.5h opens the current block.
        UsageWindow.Current(activity, now)
            .Should().Be(new UsageWindowResult(now.AddMinutes(-30), now.AddMinutes(270)));

        // Pre-filtering to the last 5h loses the now-6h anchor and wrongly starts the block at now-4.5h.
        UsageWindow.Current(activity.Where(t => t >= now.AddHours(-5)), now)
            .Should().Be(new UsageWindowResult(now.AddMinutes(-270), now.AddMinutes(30)));
    }

    [Fact]
    public void Current_NoActivity_ReturnsNull()
    {
        UsageWindow.Current([], T0).Should().BeNull();
    }

    [Fact]
    public void Current_OnlyFutureActivity_ReturnsNull()
    {
        UsageWindow.Current([T0.AddMinutes(1), T0.AddHours(2)], T0).Should().BeNull();
    }

    [Fact]
    public void Current_ActivityInsideWindow_ReturnsWindowFromFirstActivity()
    {
        var result = UsageWindow.Current([T0, T0.AddHours(1), T0.AddHours(2)], T0.AddHours(3));

        result.Should().Be(new UsageWindowResult(T0, T0.AddHours(5)));
    }

    [Fact]
    public void Current_ActivityExactlyAtWindowEnd_OpensNewWindow()
    {
        var result = UsageWindow.Current([T0, T0.AddHours(5)], T0.AddHours(6));

        result.Should().Be(new UsageWindowResult(T0.AddHours(5), T0.AddHours(10)));
    }

    [Fact]
    public void Current_ActivityExactlyAtNow_CountsAndOpensWindow()
    {
        var now = T0.AddHours(7);

        var result = UsageWindow.Current([T0, now], now);

        result.Should().Be(new UsageWindowResult(now, now.AddHours(5)));
    }

    [Fact]
    public void Current_NowEqualsResetsAt_ReturnsNull()
    {
        UsageWindow.Current([T0], T0.AddHours(5)).Should().BeNull();
    }

    [Fact]
    public void Current_LastActivityOlderThanWindow_ReturnsNull()
    {
        UsageWindow.Current([T0, T0.AddHours(1)], T0.AddHours(8)).Should().BeNull();
    }

    [Fact]
    public void Current_GapLongerThanWindow_StartsAtActivityAfterGap()
    {
        var result = UsageWindow.Current([T0, T0.AddHours(12), T0.AddHours(13)], T0.AddHours(14));

        result.Should().Be(new UsageWindowResult(T0.AddHours(12), T0.AddHours(17)));
    }

    [Fact]
    public void Current_UnsortedInput_SortsBeforeWalking()
    {
        var result = UsageWindow.Current([T0.AddHours(6), T0, T0.AddHours(4)], T0.AddHours(7));

        result.Should().Be(new UsageWindowResult(T0.AddHours(6), T0.AddHours(11)));
    }

    [Fact]
    public void Current_FutureActivityMixedIn_IsIgnored()
    {
        var result = UsageWindow.Current([T0, T0.AddHours(30)], T0.AddHours(1));

        result.Should().Be(new UsageWindowResult(T0, T0.AddHours(5)));
    }

    [Fact]
    public void Current_CustomLength_UsesThatLength()
    {
        var result = UsageWindow.Current([T0], T0.AddMinutes(30), TimeSpan.FromHours(1));

        result.Should().Be(new UsageWindowResult(T0, T0.AddHours(1)));
    }

    [Fact]
    public void Current_NonPositiveLength_Throws()
    {
        var act = () => UsageWindow.Current([T0], T0, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
