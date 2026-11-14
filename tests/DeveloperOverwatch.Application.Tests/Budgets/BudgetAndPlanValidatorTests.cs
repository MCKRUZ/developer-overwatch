namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Commands.CreateBudget;
using DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;
using DeveloperOverwatch.Application.Commands.UpdateBudget;
using DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using FluentAssertions;
using Xunit;

/// <summary>Whatever the validators let through reaches the entity, which throws on bad input, and a throw is a
/// 500. So every input the entity refuses, and every bound the contract sets, must fail validation here.</summary>
public class BudgetAndPlanValidatorTests
{
    private static readonly string LongEmail = new string('a', 309) + "@example.com"; // 321 characters
    private static readonly string LongName = new('n', DeveloperPlan.PlanNameMaxLength + 1);

    public static TheoryData<CreateBudgetCommand> InvalidBudgets => new()
    {
        new(BudgetScope.Team, "", BudgetPeriod.Daily, 10m),
        new(BudgetScope.Team, "dev@team.dev", BudgetPeriod.Daily, 10m),
        new(BudgetScope.Developer, null, BudgetPeriod.Daily, 10m),
        new(BudgetScope.Developer, "   ", BudgetPeriod.Daily, 10m),
        new(BudgetScope.Developer, "not-an-email", BudgetPeriod.Daily, 10m),
        new(BudgetScope.Developer, " @team.dev", BudgetPeriod.Daily, 10m),
        new(BudgetScope.Developer, LongEmail, BudgetPeriod.Daily, 10m),
        new(BudgetScope.Team, null, BudgetPeriod.Daily, 0m),
        new(BudgetScope.Team, null, BudgetPeriod.Daily, -1m),
        new(BudgetScope.Team, null, BudgetPeriod.Daily, 1_000_000.01m),
        new((BudgetScope)5, null, BudgetPeriod.Daily, 10m),
        new(BudgetScope.Team, null, (BudgetPeriod)5, 10m),
    };

    [Theory]
    [MemberData(nameof(InvalidBudgets))]
    public void CreateBudget_InvalidInput_Fails(CreateBudgetCommand command) =>
        new CreateBudgetCommandValidator().Validate(command).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(BudgetScope.Team, null, 1_000_000)]
    [InlineData(BudgetScope.Developer, " dev@team.dev ", 0.01)]
    public void CreateBudget_ValidInput_Passes(BudgetScope scope, string? email, decimal amount) =>
        new CreateBudgetCommandValidator().Validate(new CreateBudgetCommand(scope, email, BudgetPeriod.Monthly, amount))
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_001)]
    public void UpdateBudget_AmountOutOfRange_Fails(decimal amount) =>
        new UpdateBudgetCommandValidator().Validate(new UpdateBudgetCommand(Guid.NewGuid(), amount))
            .IsValid.Should().BeFalse();

    public static TheoryData<CreateDeveloperPlanCommand> InvalidPlans => new()
    {
        new(null!, "Pro", 20m, null),
        new("  ", "Pro", 20m, null),
        new("not-an-email", "Pro", 20m, null),
        new(LongEmail, "Pro", 20m, null),
        new("dev@team.dev", null!, 20m, null),
        new("dev@team.dev", "   ", 20m, null),
        new("dev@team.dev", LongName, 20m, null),
        new("dev@team.dev", "Pro", 0m, null),
        new("dev@team.dev", "Pro", 100_000.01m, null),
        new("dev@team.dev", "Pro", 20m, 0m),
        new("dev@team.dev", "Pro", 20m, -1m),
        new("dev@team.dev", "Pro", 20m, 100_000.01m),
    };

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void CreatePlan_InvalidInput_Fails(CreateDeveloperPlanCommand command) =>
        new CreateDeveloperPlanCommandValidator().Validate(command).IsValid.Should().BeFalse();

    // A name padded past the limit is fine: the entity stores it trimmed.
    [Fact]
    public void CreatePlan_BoundaryValues_Pass() =>
        new CreateDeveloperPlanCommandValidator()
            .Validate(new CreateDeveloperPlanCommand(
                " dev@team.dev ", " " + new string('n', DeveloperPlan.PlanNameMaxLength) + " ", 100_000m, null))
            .IsValid.Should().BeTrue();

    public static TheoryData<UpdateDeveloperPlanCommand> InvalidPlanUpdates => new()
    {
        new(Guid.NewGuid(), null!, 20m, null),
        new(Guid.NewGuid(), "  ", 20m, null),
        new(Guid.NewGuid(), LongName, 20m, null),
        new(Guid.NewGuid(), "Pro", 0m, null),
        new(Guid.NewGuid(), "Pro", 20m, 0m),
        new(Guid.NewGuid(), "Pro", 20m, 100_000.01m),
    };

    [Theory]
    [MemberData(nameof(InvalidPlanUpdates))]
    public void UpdatePlan_InvalidInput_Fails(UpdateDeveloperPlanCommand command) =>
        new UpdateDeveloperPlanCommandValidator().Validate(command).IsValid.Should().BeFalse();
}
