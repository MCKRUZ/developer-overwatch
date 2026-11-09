namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class ForecastTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddDays(30);

    [Fact]
    public void Project_HalfBudgetInFirstThird_RunsOutAtDayTwenty()
    {
        var result = Forecast.Project(50m, 100m, Start, End, Start.AddDays(10));

        result.Kind.Should().Be(ForecastKind.RunsOutWithinPeriod);
        result.RunsOutAt.Should().Be(Start.AddDays(20));
    }

    [Fact]
    public void Project_SlowSpend_LastsPastPeriodEnd()
    {
        var result = Forecast.Project(10m, 100m, Start, End, Start.AddDays(10));

        result.Should().Be(ForecastResult.LastsPastPeriodEnd());
        result.RunsOutAt.Should().BeNull();
    }

    [Fact]
    public void Project_RunsOutExactlyAtPeriodEnd_LastsPastPeriodEnd()
    {
        var result = Forecast.Project(50m, 150m, Start, End, Start.AddDays(10));

        result.Kind.Should().Be(ForecastKind.LastsPastPeriodEnd);
    }

    [Fact]
    public void Project_SpentEqualsBudget_AlreadyExceeded()
    {
        Forecast.Project(100m, 100m, Start, End, Start.AddDays(1))
            .Should().Be(ForecastResult.AlreadyExceeded());
    }

    [Fact]
    public void Project_SpentOverBudget_AlreadyExceeded()
    {
        Forecast.Project(120m, 100m, Start, End, Start.AddDays(1)).Kind
            .Should().Be(ForecastKind.AlreadyExceeded);
    }

    [Fact]
    public void Project_NothingSpent_NoSpendYet()
    {
        Forecast.Project(0m, 100m, Start, End, Start.AddDays(1))
            .Should().Be(ForecastResult.NoSpendYet());
    }

    [Fact]
    public void Project_NowAtPeriodStart_NoRateYet()
    {
        Forecast.Project(5m, 100m, Start, End, Start)
            .Should().Be(ForecastResult.NoRateYet());
    }

    [Fact]
    public void Project_NowAfterPeriodEndUnderBudget_LastsPastPeriodEnd()
    {
        Forecast.Project(90m, 100m, Start, End, End.AddDays(1)).Kind
            .Should().Be(ForecastKind.LastsPastPeriodEnd);
    }

    [Fact]
    public void Project_ProjectionBeyondTimeSpanRange_DoesNotOverflow()
    {
        // 1e9 left at 1e-7 per day is ~8.6e20 seconds, far past TimeSpan.MaxValue (~9.2e11 s).
        var result = Forecast.Project(0.0000001m, 1_000_000_000m, Start, End, Start.AddDays(1));

        result.Kind.Should().Be(ForecastKind.LastsPastPeriodEnd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Project_NonPositiveBudget_Throws(int budget)
    {
        var act = () => Forecast.Project(1m, budget, Start, End, Start.AddDays(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Project_EndNotAfterStart_Throws()
    {
        var act = () => Forecast.Project(1m, 100m, Start, Start, Start.AddDays(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RunsOut_CarriesTimestamp()
    {
        var at = Start.AddDays(3);

        var result = ForecastResult.RunsOut(at);

        result.Kind.Should().Be(ForecastKind.RunsOutWithinPeriod);
        result.RunsOutAt.Should().Be(at);
    }
}
