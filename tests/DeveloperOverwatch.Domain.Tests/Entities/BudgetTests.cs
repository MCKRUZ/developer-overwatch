namespace DeveloperOverwatch.Domain.Tests.Entities;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using FluentAssertions;
using Xunit;

public class BudgetTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_TeamBudget_UsesTeamSubjectKeyAndNoEmail()
    {
        var budget = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now);

        budget.Id.Should().NotBe(Guid.Empty);
        budget.Scope.Should().Be(BudgetScope.Team);
        budget.DeveloperEmail.Should().BeNull();
        budget.SubjectKey.Should().Be(Budget.TeamSubjectKey);
        budget.Period.Should().Be(BudgetPeriod.Daily);
        budget.AmountUsd.Should().Be(50m);
        budget.CreatedAt.Should().Be(Now);
        budget.UpdatedAt.Should().Be(Now);
    }

    // The subject key carries uniqueness, so " dev@x " and "dev@x" must land on the same key.
    [Fact]
    public void Create_DeveloperBudget_TrimsEmailAndUsesItAsSubjectKey()
    {
        var budget = Budget.Create(BudgetScope.Developer, "  dev@team.dev ", BudgetPeriod.Monthly, 200m, Now);

        budget.DeveloperEmail.Should().Be("dev@team.dev");
        budget.SubjectKey.Should().Be("dev@team.dev");
        Budget.SubjectKeyFor(BudgetScope.Developer, " dev@team.dev").Should().Be(budget.SubjectKey);
    }

    [Fact]
    public void Create_EachBudget_GetsItsOwnId()
    {
        var first = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 1m, Now);
        var second = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 1m, Now);

        first.Id.Should().NotBe(second.Id);
    }

    [Fact]
    public void Create_TeamBudgetWithEmail_Throws()
    {
        var act = () => Budget.Create(BudgetScope.Team, "dev@team.dev", BudgetPeriod.Daily, 1m, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_DeveloperBudgetWithoutEmail_Throws(string? email)
    {
        var act = () => Budget.Create(BudgetScope.Developer, email, BudgetPeriod.Daily, 1m, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveAmount_Throws(int amount)
    {
        var act = () => Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, amount, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ChangeAmount_UpdatesAmountAndUpdatedAtOnly()
    {
        var budget = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now);
        var later = Now.AddHours(2);

        budget.ChangeAmount(75m, later);

        budget.AmountUsd.Should().Be(75m);
        budget.UpdatedAt.Should().Be(later);
        budget.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeAmount_NonPositiveAmount_Throws()
    {
        var budget = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now);

        var act = () => budget.ChangeAmount(0m, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
