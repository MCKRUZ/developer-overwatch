namespace DeveloperOverwatch.Domain.Tests.Metering;

using DeveloperOverwatch.Domain.Metering;
using FluentAssertions;
using Xunit;

public class BudgetThresholdTests
{
    // Amber and red are inclusive: a budget exactly three quarters used is already a warning, and one
    // exactly used up is already over.
    [Theory]
    [InlineData("0", BudgetLevel.Ok)]
    [InlineData("0.7499", BudgetLevel.Ok)]
    [InlineData("0.75", BudgetLevel.Amber)]
    [InlineData("0.9999", BudgetLevel.Amber)]
    [InlineData("1", BudgetLevel.Red)]
    [InlineData("2.5", BudgetLevel.Red)]
    public void LevelOf_UsedFraction_MapsToInclusiveBands(string usedFraction, BudgetLevel expected)
    {
        BudgetThreshold.LevelOf(decimal.Parse(usedFraction, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);
    }
}
