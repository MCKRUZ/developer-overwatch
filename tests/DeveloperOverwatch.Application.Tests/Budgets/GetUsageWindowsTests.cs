namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetUsageWindows;
using DeveloperOverwatch.Application.Tests.Persistence;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Metering;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>Estimated 5-hour windows per developer on the real repositories and SQLite: who is listed, what a
/// window holds with and without a plan limit, and the order the dashboard shows the cards in.</summary>
public sealed class GetUsageWindowsTests : IDisposable
{
    private static readonly DateTime Now = Utc(2026, 11, 10, 17);

    private readonly UsageDatabase _data = new();

    public void Dispose() => _data.Dispose();

    // 10:00 opens a window that resets at 15:00, so the turn at 15:00 opens the current one (15:00..20:00).
    // $5 of a $10 limit two hours into five is ahead of pace, and at that rate runs out at 19:00.
    [Fact]
    public async Task Handle_OpenWindowWithALimit_ReportsSpendPaceAndForecastOverTheWindow()
    {
        await AddPlans(DeveloperPlan.Create("a@team.dev", "Max", 200m, 10m, Now));
        await _data.Seed(Session("a", email: "a@team.dev", turns:
        [
            Turn(Utc(2026, 11, 10, 10), input: 1000, cost: 100m),
            Turn(Utc(2026, 11, 10, 15), input: 100, cost: 3m),
            Turn(Utc(2026, 11, 10, 16), input: 20, output: 10, cacheWrite: 15, cacheRead: 5, cost: 2m),
        ]));

        var row = (await Run()).Single();

        row.Should().BeEquivalentTo(new DeveloperWindowDto("a@team.dev", "Max", 10m, new WindowDto(
            Utc(2026, 11, 10, 15), Utc(2026, 11, 10, 20), 5m, 150, 0.5m, BudgetLevel.Ok,
            new PaceDto(0.5m, 0.4m, PaceStatus.Ahead),
            new ForecastDto(ForecastKind.RunsOutWithinPeriod, Utc(2026, 11, 10, 19)))));
        row.Window!.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // A plan typed with other capitals than the developer's sessions still applies its limit, on one row.
    [Fact]
    public async Task Handle_PlanEmailInOtherCase_AppliesTheLimitToThatDevelopersWindow()
    {
        await AddPlans(DeveloperPlan.Create("A@Team.dev", "Max", 200m, 10m, Now));
        await _data.Seed(Session("a", email: "a@TEAM.dev", turns: [Turn(Utc(2026, 11, 10, 16), input: 10, cost: 5m)]));

        var row = (await Run()).Single();

        row.WindowLimitUsd.Should().Be(10m);
        row.Window!.UsedFraction.Should().Be(0.5m);
    }

    // Without a limit there is nothing to measure against: the window and its spend are shown, the rest is null.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_OpenWindowWithoutALimit_LeavesFractionLevelPaceAndForecastNull(bool hasPlan)
    {
        if (hasPlan)
            await AddPlans(DeveloperPlan.Create("a@team.dev", "Pro", 20m, null, Now));
        await _data.Seed(Session("a", email: "a@team.dev", turns: [Turn(Now.AddHours(-1), input: 7, cost: 1.5m)]));

        var row = (await Run()).Single();

        row.Should().BeEquivalentTo(new DeveloperWindowDto(
            "a@team.dev", hasPlan ? "Pro" : null, null,
            new WindowDto(Now.AddHours(-1), Now.AddHours(4), 1.5m, 7, null, null, null, null)));
    }

    // A plan holder whose last window reset (or who has no usage at all) is still listed, with no window.
    // Someone without a plan is listed only while a window is open.
    [Fact]
    public async Task Handle_NoOpenWindow_ListsPlanHoldersOnly()
    {
        await AddPlans(
            DeveloperPlan.Create("idle@team.dev", "Max", 200m, 10m, Now),
            DeveloperPlan.Create("reset@team.dev", "Max", 200m, 10m, Now));
        await _data.Seed(
            Session("r", email: "reset@team.dev", turns: [Turn(Now.AddHours(-5), cost: 1m)]),
            Session("x", email: "noplan@team.dev", turns: [Turn(Now.AddHours(-6), cost: 1m)]));

        var rows = await Run();

        rows.Should().BeEquivalentTo(
            [
                new DeveloperWindowDto("idle@team.dev", "Max", 10m, null),
                new DeveloperWindowDto("reset@team.dev", "Max", 10m, null),
            ],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Handle_NoPlansAndNoUsage_ReturnsAnEmptyList()
    {
        (await Run()).Should().BeEmpty();
    }

    // Most-used first; rows with no fraction (no limit, or no window) after them, by email.
    [Fact]
    public async Task Handle_SeveralDevelopers_OrdersByUsedFractionDescendingThenEmailWithNullsLast()
    {
        await AddPlans(
            DeveloperPlan.Create("low@team.dev", "Max", 200m, 10m, Now),
            DeveloperPlan.Create("high@team.dev", "Max", 200m, 10m, Now),
            DeveloperPlan.Create("b-idle@team.dev", "Max", 200m, 10m, Now));
        await _data.Seed(
            Session("l", email: "low@team.dev", turns: [Turn(Now.AddHours(-1), cost: 2m)]),
            Session("h", email: "high@team.dev", turns: [Turn(Now.AddHours(-1), cost: 7.5m)]),
            Session("n", email: "c-nolimit@team.dev", turns: [Turn(Now.AddHours(-1), cost: 50m)]),
            Session("a", email: "a-nolimit@team.dev", turns: [Turn(Now.AddHours(-1), cost: 1m)]));

        var rows = await Run();

        rows.Select(r => (r.DeveloperEmail, r.Window?.UsedFraction, r.Window?.Level)).Should().Equal(
            ("high@team.dev", 0.75m, BudgetLevel.Amber),
            ("low@team.dev", 0.2m, BudgetLevel.Ok),
            ("a-nolimit@team.dev", null, null),
            ("b-idle@team.dev", null, null),
            ("c-nolimit@team.dev", null, null));
    }

    private async Task AddPlans(params DeveloperPlan[] plans)
    {
        _data.Db.DeveloperPlans.AddRange(plans);
        await _data.Db.SaveChangesAsync();
        _data.Db.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<DeveloperWindowDto>> Run()
    {
        var handler = new GetUsageWindowsQueryHandler(new DeveloperPlanRepository(_data.Db), _data.Repository, new FixedTime(Now));
        var result = await handler.Handle(new GetUsageWindowsQuery(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }
}
