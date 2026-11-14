namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Queries.GetBudgetStatuses;
using DeveloperOverwatch.Application.Queries.GetPlanValues;
using FluentAssertions;
using Xunit;

/// <summary>Budget status and plan value count in the viewer's local day and month, so both refuse a request
/// without a zone they can resolve, naming the query parameter the dashboard sends.</summary>
public sealed class TimeZoneQueryValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("America")]
    public void Validate_MissingOrUnknownZone_FailsOnTz(string? zone)
    {
        var statusErrors = new GetBudgetStatusesQueryValidator().Validate(new GetBudgetStatusesQuery(zone)).Errors;
        var valueErrors = new GetPlanValuesQueryValidator().Validate(new GetPlanValuesQuery(zone)).Errors;

        statusErrors.Concat(valueErrors).Should().HaveCount(2).And.AllSatisfy(e =>
        {
            e.PropertyName.Should().Be("tz");
            e.ErrorMessage.Should().Be("'tz' must be a known IANA time zone id.");
        });
    }

    [Fact]
    public void Validate_KnownZone_Passes()
    {
        new GetBudgetStatusesQueryValidator().Validate(new GetBudgetStatusesQuery("America/New_York")).IsValid.Should().BeTrue();
        new GetPlanValuesQueryValidator().Validate(new GetPlanValuesQuery("America/New_York")).IsValid.Should().BeTrue();
    }
}
