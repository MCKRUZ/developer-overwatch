namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class PaceTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddDays(10);

    [Fact]
    public void Compute_SpendingFasterThanTime_IsAhead()
    {
        var result = Pace.Compute(60m, 100m, Start, End, Start.AddDays(4));

        result.UsedFraction.Should().Be(0.6m);
        result.ElapsedFraction.Should().Be(0.4m);
        result.Status.Should().Be(PaceStatus.Ahead);
    }

    [Fact]
    public void Compute_SpendingSlowerThanTime_IsBehind()
    {
        var result = Pace.Compute(20m, 100m, Start, End, Start.AddDays(5));

        result.Status.Should().Be(PaceStatus.Behind);
    }

    [Fact]
    public void Compute_SpendingWithinTolerance_IsOnPace()
    {
        var result = Pace.Compute(54m, 100m, Start, End, Start.AddDays(5));

        result.Status.Should().Be(PaceStatus.OnPace);
    }

    [Fact]
    public void Compute_ExactlyAtToleranceEdge_IsOnPace()
    {
        Pace.Compute(55m, 100m, Start, End, Start.AddDays(5)).Status.Should().Be(PaceStatus.OnPace);
        Pace.Compute(45m, 100m, Start, End, Start.AddDays(5)).Status.Should().Be(PaceStatus.OnPace);
    }

    [Fact]
    public void Compute_CustomTolerance_IsApplied()
    {
        var result = Pace.Compute(54m, 100m, Start, End, Start.AddDays(5), tolerance: 0.01m);

        result.Status.Should().Be(PaceStatus.Ahead);
    }

    [Fact]
    public void Compute_NowBeforeStart_ClampsElapsedToZero()
    {
        var result = Pace.Compute(0m, 100m, Start, End, Start.AddDays(-1));

        result.ElapsedFraction.Should().Be(0m);
        result.Status.Should().Be(PaceStatus.OnPace);
    }

    [Fact]
    public void Compute_NowAfterEnd_ClampsElapsedToOne()
    {
        var result = Pace.Compute(100m, 100m, Start, End, End.AddDays(3));

        result.ElapsedFraction.Should().Be(1m);
        result.Status.Should().Be(PaceStatus.OnPace);
    }

    [Fact]
    public void Compute_UsedOverLimit_ReportsFractionAboveOne()
    {
        var result = Pace.Compute(150m, 100m, Start, End, Start.AddDays(5));

        result.UsedFraction.Should().Be(1.5m);
        result.Status.Should().Be(PaceStatus.Ahead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compute_NonPositiveLimit_Throws(int limit)
    {
        var act = () => Pace.Compute(1m, limit, Start, End, Start);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compute_EndNotAfterStart_Throws()
    {
        var act = () => Pace.Compute(1m, 100m, Start, Start, Start);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
