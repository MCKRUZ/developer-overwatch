namespace DeveloperOverwatch.Domain.Tests.Entities;

using DeveloperOverwatch.Domain.Entities;
using FluentAssertions;
using Xunit;

public class DeveloperPlanTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_TrimsEmailAndPlanName_AndStampsBothTimes()
    {
        var plan = DeveloperPlan.Create(" dev@team.dev ", " Max 20x ", 200m, 40m, Now);

        plan.Id.Should().NotBe(Guid.Empty);
        plan.DeveloperEmail.Should().Be("dev@team.dev");
        plan.PlanName.Should().Be("Max 20x");
        plan.MonthlyPriceUsd.Should().Be(200m);
        plan.WindowLimitUsd.Should().Be(40m);
        plan.CreatedAt.Should().Be(Now);
        plan.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_WithoutWindowLimit_LeavesItNull()
    {
        var plan = DeveloperPlan.Create("dev@team.dev", "Pro", 20m, null, Now);

        plan.WindowLimitUsd.Should().BeNull();
    }

    [Fact]
    public void Update_ReplacesEditableFields_KeepsEmailAndCreatedAt()
    {
        var plan = DeveloperPlan.Create("dev@team.dev", "Pro", 20m, 10m, Now);
        var later = Now.AddDays(1);

        plan.Update(" Max 5x ", 100m, null, later);

        plan.PlanName.Should().Be("Max 5x");
        plan.MonthlyPriceUsd.Should().Be(100m);
        plan.WindowLimitUsd.Should().BeNull();
        plan.DeveloperEmail.Should().Be("dev@team.dev");
        plan.CreatedAt.Should().Be(Now);
        plan.UpdatedAt.Should().Be(later);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Create_BlankEmail_Throws(string? email)
    {
        var act = () => DeveloperPlan.Create(email!, "Pro", 20m, null, Now);

        act.Should().Throw<ArgumentException>();
    }
}
