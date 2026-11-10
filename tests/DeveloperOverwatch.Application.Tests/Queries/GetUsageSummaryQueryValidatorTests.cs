namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using FluentAssertions;
using Xunit;

/// <summary>The date bounds keep every query inside the range the handler can pad by a day without running
/// past DateTime's limits. The upper bound moves with the clock, so it is pinned with a fixed one.</summary>
public sealed class GetUsageSummaryQueryValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);
    private readonly GetUsageSummaryQueryValidator _validator = new(new FixedTime(Today));

    [Fact]
    public void Validate_ToExactlyOneYearFromToday_IsValid()
    {
        var to = Today.AddYears(1);

        _validator.Validate(Query(to.AddDays(-6), to)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToOneDayPastOneYearFromToday_IsInvalid()
    {
        var to = Today.AddYears(1).AddDays(1);

        var result = _validator.Validate(Query(to.AddDays(-6), to));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("'to' must be at most one year from today"));
    }

    [Fact]
    public void Validate_FromTheDayBefore2000_IsInvalid()
    {
        var result = _validator.Validate(Query(new DateOnly(1999, 12, 31), new DateOnly(2000, 1, 6)));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage.Contains("'from' must be on or after 2000-01-01"));
    }

    private static GetUsageSummaryQuery Query(DateOnly from, DateOnly to) =>
        new(from, to, "day", "Etc/UTC", null, null);

    private sealed class FixedTime(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
