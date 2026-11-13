namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Application.Queries.GetBudgetStatuses;
using DeveloperOverwatch.Application.Tests.Persistence;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Metering;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>Budget status on the real repositories and SQLite. Periods are the viewer's local day and month, so
/// these pin where spend starts counting, and the amber/red edges the dashboard colours rows by.</summary>
public sealed class GetBudgetStatusesTests : IDisposable
{
    private const string NewYork = "America/New_York";

    // 12:00 EST: the local day runs 05:00Z..05:00Z the next day, so exactly half of it has elapsed.
    private static readonly DateTime Noon = Utc(2026, 11, 10, 17);

    private readonly UsageDatabase _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData(74.99, BudgetLevel.Ok)]
    [InlineData(75, BudgetLevel.Amber)]
    [InlineData(99.99, BudgetLevel.Amber)]
    [InlineData(100, BudgetLevel.Red)]
    public async Task Handle_SpendAtTheBandEdges_ReportsTheLevelInclusively(decimal spent, BudgetLevel level)
    {
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, Noon));
        await _data.Seed(Session("s", turns: [Turn(Noon.AddHours(-1), cost: spent)]));

        var status = (await Run(Noon)).Single();

        status.SpentUsd.Should().Be(spent);
        status.UsedFraction.Should().Be(spent / 100m);
        status.Level.Should().Be(level);
        status.RemainingUsd.Should().Be(100m - spent);
    }

    [Fact]
    public async Task Handle_SpendOverTheAmount_ReportsNegativeRemainingAndAlreadyExceeded()
    {
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, Noon));
        await _data.Seed(Session("s", turns: [Turn(Noon.AddHours(-1), cost: 120m)]));

        var status = (await Run(Noon)).Single();

        status.RemainingUsd.Should().Be(-20m);
        status.UsedFraction.Should().Be(1.2m);
        status.Level.Should().Be(BudgetLevel.Red);
        status.Forecast.Should().Be(new ForecastDto(ForecastKind.AlreadyExceeded, null));
        status.Pace.Status.Should().Be(PaceStatus.Ahead);
    }

    // Sessions carry whatever case a developer's git config uses; a budget typed with other capitals is still theirs.
    [Fact]
    public async Task Handle_DeveloperBudgetEmailInOtherCase_CountsThatDevelopersSpend()
    {
        await AddBudgets(Budget.Create(BudgetScope.Developer, " Alice@Team.dev ", BudgetPeriod.Daily, 100m, Noon));
        await _data.Seed(Session("s", email: "alice@TEAM.dev", turns: [Turn(Noon.AddHours(-1), cost: 30m)]));

        var status = (await Run(Noon)).Single(s => s.Budget.Scope == BudgetScope.Developer);

        status.Budget.DeveloperEmail.Should().Be("alice@team.dev");
        status.SpentUsd.Should().Be(30m);
    }

    // Half the day gone: $60 of $100 is ahead of pace and runs out 8 hours later at the same rate; $50 is on pace;
    // $20 is behind and lasts past midnight; nothing spent has no rate to project.
    [Theory]
    [InlineData(60, PaceStatus.Ahead, ForecastKind.RunsOutWithinPeriod)]
    [InlineData(50, PaceStatus.OnPace, ForecastKind.LastsPastPeriodEnd)]
    [InlineData(20, PaceStatus.Behind, ForecastKind.LastsPastPeriodEnd)]
    [InlineData(0, PaceStatus.Behind, ForecastKind.NoSpendYet)]
    public async Task Handle_HalfwayThroughTheDay_ComparesSpendWithTimeAndProjectsTheRate(
        decimal spent, PaceStatus pace, ForecastKind forecast)
    {
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, Noon));
        await _data.Seed(Session("s", turns: [Turn(Noon.AddHours(-1), cost: spent)]));

        var status = (await Run(Noon)).Single();

        status.Pace.Should().Be(new PaceDto(spent / 100m, 0.5m, pace));
        status.Forecast.Kind.Should().Be(forecast);
        status.Forecast.RunsOutAt.Should().Be(
            forecast == ForecastKind.RunsOutWithinPeriod ? Utc(2026, 11, 11, 1) : null);
    }

    // At the very first instant of the day there is spend but no elapsed time, so no rate yet.
    [Fact]
    public async Task Handle_SpendAtTheFirstInstantOfTheDay_HasNoRateYet()
    {
        var dayStart = Utc(2026, 11, 10, 5);
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, dayStart));
        await _data.Seed(Session("s", turns: [Turn(dayStart, cost: 1m)]));

        var status = (await Run(dayStart)).Single();

        status.SpentUsd.Should().Be(1m);
        status.Forecast.Should().Be(new ForecastDto(ForecastKind.NoRateYet, null));
    }

    // 04:59Z is 23:59 on the 9th in New York; 05:00Z is the first moment of the 10th.
    [Fact]
    public async Task Handle_DailyBudget_CountsFromLocalMidnightToNow()
    {
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, Noon));
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 10, 4, 59), cost: 100m),
            Turn(Utc(2026, 11, 10, 5, 0), cost: 1m),
            Turn(Noon, cost: 2m),
            Turn(Noon.AddMinutes(1), cost: 400m), // after now
        ]));

        var status = (await Run(Noon)).Single();

        (status.PeriodStartUtc, status.PeriodEndUtc).Should().Be((Utc(2026, 11, 10, 5), Utc(2026, 11, 11, 5)));
        status.PeriodStartUtc.Kind.Should().Be(DateTimeKind.Utc);
        status.SpentUsd.Should().Be(3m);
    }

    // November starts at 04:00Z (midnight EDT) and December at 05:00Z (midnight EST): the month spans the DST end.
    [Fact]
    public async Task Handle_MonthlyBudget_CountsFromTheFirstOfTheLocalMonth()
    {
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Monthly, 1000m, Noon));
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 1, 3, 59), cost: 100m), // 23:59 on 31 October
            Turn(Utc(2026, 11, 1, 4, 0), cost: 1m),
            Turn(Utc(2026, 11, 9, 12, 0), cost: 2m),
        ]));

        var status = (await Run(Noon)).Single();

        (status.PeriodStartUtc, status.PeriodEndUtc).Should().Be((Utc(2026, 11, 1, 4), Utc(2026, 12, 1, 5)));
        status.SpentUsd.Should().Be(3m);
    }

    // DST ends at 02:00 on 1 November in New York, so that local day is 25 hours long. At 12:00 EST (17:00Z),
    // 13 of its 25 hours have passed; a 24-hour day from midnight EST would say exactly half.
    [Fact]
    public async Task Handle_DayDstEnds_UsesTheTwentyFiveHourDay()
    {
        var now = Utc(2026, 11, 1, 17);
        await AddBudgets(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, now));

        var status = (await Run(now)).Single();

        (status.PeriodStartUtc, status.PeriodEndUtc).Should().Be((Utc(2026, 11, 1, 4), Utc(2026, 11, 2, 5)));
        status.Pace.ElapsedFraction.Should().Be(13m / 25m);
    }

    // A developer budget counts only that developer's turns, whatever their capitals; the team budget counts everyone.
    [Fact]
    public async Task Handle_DeveloperAndTeamBudgets_CountTheirOwnSubjects()
    {
        await AddBudgets(
            Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 100m, Noon),
            Budget.Create(BudgetScope.Developer, "a@team.dev", BudgetPeriod.Daily, 10m, Noon));
        await _data.Seed(
            Session("a", email: "a@team.dev", turns: [Turn(Noon.AddHours(-1), cost: 1m)]),
            Session("A", email: "A@team.dev", turns: [Turn(Noon.AddHours(-1), cost: 2m)]),
            Session("b", email: "b@team.dev", turns: [Turn(Noon.AddHours(-1), cost: 4m)]));

        var statuses = await Run(Noon);

        statuses.Select(s => (s.Budget.DeveloperEmail, s.SpentUsd))
            .Should().Equal(((string?)null, 7m), ("a@team.dev", 3m));
    }

    [Fact]
    public async Task Handle_SeveralBudgets_ListsTeamFirstThenDevelopersByEmailDailyBeforeMonthly()
    {
        await AddBudgets(
            Budget.Create(BudgetScope.Developer, "b@team.dev", BudgetPeriod.Daily, 10m, Noon),
            Budget.Create(BudgetScope.Developer, "a@team.dev", BudgetPeriod.Monthly, 10m, Noon),
            Budget.Create(BudgetScope.Team, null, BudgetPeriod.Monthly, 10m, Noon),
            Budget.Create(BudgetScope.Developer, "a@team.dev", BudgetPeriod.Daily, 10m, Noon),
            Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 10m, Noon));

        var statuses = await Run(Noon);

        statuses.Select(s => (s.Budget.DeveloperEmail, s.Budget.Period)).Should().Equal(
            ((string?)null, BudgetPeriod.Daily), (null, BudgetPeriod.Monthly),
            ("a@team.dev", BudgetPeriod.Daily), ("a@team.dev", BudgetPeriod.Monthly), ("b@team.dev", BudgetPeriod.Daily));
    }

    [Fact]
    public async Task Handle_NoBudgets_ReturnsAnEmptyList()
    {
        (await Run(Noon)).Should().BeEmpty();
    }

    private async Task AddBudgets(params Budget[] budgets)
    {
        _data.Db.Budgets.AddRange(budgets);
        await _data.Db.SaveChangesAsync();
        _data.Db.ChangeTracker.Clear();
    }

    private async Task<IReadOnlyList<BudgetStatusDto>> Run(DateTime now)
    {
        var handler = new GetBudgetStatusesQueryHandler(new BudgetRepository(_data.Db), _data.Repository, new FixedTime(now));
        var result = await handler.Handle(new GetBudgetStatusesQuery(NewYork), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }
}
