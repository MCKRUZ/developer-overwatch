namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using DeveloperOverwatch.Application.Tests.Persistence;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>Runs the summary handler on the real repository and SQLite. Days are the viewer's local days, so
/// these pin the zone conversion where naive offset math goes wrong: DST, half-hour zones, week and month edges.</summary>
public sealed class GetUsageSummaryTests : IDisposable
{
    private static readonly DateTime Now = Utc(2026, 11, 10, 12, 0);
    private static readonly DateOnly PricesAsOf = new(2026, 10, 1);

    private readonly UsageDatabase _data = new();

    public void Dispose() => _data.Dispose();

    // DST starts in New York at 07:00 UTC on 2026-03-08, so that local day is 23 hours long and 04:30 UTC the
    // next morning is already 00:30 on the 9th. A fixed -5h offset would put it on the 8th.
    [Fact]
    public async Task Handle_DstStartDayInNewYork_PlacesTurnsOnTheirLocalDays()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 3, 8, 4, 30), input: 1), // 23:30 EST on the 7th
            Turn(Utc(2026, 3, 9, 3, 30), input: 2), // 23:30 EDT on the 8th
            Turn(Utc(2026, 3, 9, 4, 30), input: 4), // 00:30 EDT on the 9th
        ]));

        var summary = await Run(new(2026, 3, 7), new(2026, 3, 9), "day", "America/New_York");

        summary.Buckets.Select(b => (b.Start, b.InputTokens))
            .Should().Equal((new DateOnly(2026, 3, 7), 1L), (new DateOnly(2026, 3, 8), 2L), (new DateOnly(2026, 3, 9), 4L));
    }

    // Kolkata is UTC+05:30. Hour-wide slots could not split 18:20 from 18:40 UTC; quarter-hour slots can.
    [Fact]
    public async Task Handle_HalfHourZoneTurnsEitherSideOfLocalMidnight_LandOnTheRightDays()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 9, 18, 20), input: 1), // 23:50 on the 9th
            Turn(Utc(2026, 11, 9, 18, 40), input: 2), // 00:10 on the 10th
        ]));

        var summary = await Run(new(2026, 11, 9), new(2026, 11, 10), "day", "Asia/Kolkata");

        summary.Buckets.Select(b => (b.Start, b.InputTokens))
            .Should().Equal((new DateOnly(2026, 11, 9), 1L), (new DateOnly(2026, 11, 10), 2L));
    }

    // ISO weeks start on Monday: Sunday the 8th closes the week of the 2nd, Monday the 9th opens the next.
    [Fact]
    public async Task Handle_WeekGranularityAcrossSundayToMonday_SplitsAtMondayAndZeroFillsFromFirstMonday()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 8, 23, 59), input: 1),
            Turn(Utc(2026, 11, 9, 0, 0), input: 2),
        ]));

        var summary = await Run(new(2026, 11, 1), new(2026, 11, 15), "week", "Etc/UTC");

        summary.Buckets.Select(b => (b.Start, b.InputTokens)).Should().Equal(
            (new DateOnly(2026, 10, 26), 0L), (new DateOnly(2026, 11, 2), 1L), (new DateOnly(2026, 11, 9), 2L));
    }

    [Fact]
    public async Task Handle_MonthGranularity_RollsUpByCalendarMonthAndEdgeBucketsHoldOnlyInRangeUsage()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 10, 14, 23, 59), input: 100), // before the period, same month as its first day
            Turn(Utc(2026, 10, 31, 23, 59), input: 1),
            Turn(Utc(2026, 11, 1, 0, 0), input: 2),
            Turn(Utc(2026, 12, 5, 23, 0), input: 4),
            Turn(Utc(2026, 12, 6, 0, 0), input: 200), // after the period
        ]));

        var summary = await Run(new(2026, 10, 15), new(2026, 12, 5), "month", "Etc/UTC");

        summary.Buckets.Select(b => (b.Start, b.InputTokens)).Should().Equal(
            (new DateOnly(2026, 10, 1), 1L), (new DateOnly(2026, 11, 1), 2L), (new DateOnly(2026, 12, 1), 4L));
        summary.Totals.InputTokens.Should().Be(7);
    }

    // Rule 19: one developer working on two machines is one person's spend.
    [Fact]
    public async Task Handle_OneDeveloperOnTwoMachines_DeveloperSplitCombinesThemAndMachineSplitSeparatesThem()
    {
        await _data.Seed(
            Session("laptop-session", email: "dev@team.dev", machine: "laptop", turns: [Turn(Now.AddHours(-2), cost: 1m)]),
            Session("desktop-session", email: "dev@team.dev", machine: "desktop", turns: [Turn(Now.AddHours(-1), cost: 2m)]));

        var summary = await Run(new(2026, 11, 10), new(2026, 11, 10), "day", "Etc/UTC");

        summary.Splits.Developer.Should().ContainSingle()
            .Which.Should().Match<UsageSplitRow>(r => r.Key == "dev@team.dev" && r.EstimatedCostUsd == 3m && r.TurnCount == 2);
        summary.Splits.Machine.Select(r => (r.Label, r.EstimatedCostUsd)).Should().Equal(("desktop", 2m), ("laptop", 1m));
    }

    // Subagent turns are stored under their parent session, so they are charged to it; the subagent split
    // separates them out, with the parent's own turns labelled "main thread".
    [Fact]
    public async Task Handle_SubagentTurns_ChargedToParentSessionAndSplitFromMainThread()
    {
        await _data.Seed(Session("abcdef1234567890", turns:
        [
            Turn(Now.AddHours(-3), cost: 1m),
            Turn(Now.AddHours(-2), cost: 3m, subagent: "Explore"),
        ]));

        var summary = await Run(new(2026, 11, 10), new(2026, 11, 10), "day", "Etc/UTC");

        var session = summary.Splits.Session.Should().ContainSingle().Subject;
        (session.Key, session.Label, session.EstimatedCostUsd).Should().Be(("abcdef1234567890", "abcdef12", 4m));
        (session.FirstAt, session.LastAt).Should().Be((Now.AddHours(-3), Now.AddHours(-2)));
        summary.Splits.Subagent.Select(r => (r.Key, r.Label, r.EstimatedCostUsd, r.CostShare))
            .Should().Equal(("Explore", "Explore", 3m, 0.75), (null, "main thread", 1m, 0.25));
    }

    [Fact]
    public async Task Handle_PeriodWithOutputButNoInput_CacheHitRateIsNullNotZero()
    {
        await _data.Seed(Session("s", turns: [Turn(Now.AddHours(-1), output: 50)]));

        var summary = await Run(new(2026, 11, 9), new(2026, 11, 10), "day", "Etc/UTC");

        summary.Totals.CacheHitRate.Should().BeNull("a hit rate of nothing is undefined, not 0%");
        summary.Buckets.Should().AllSatisfy(b => b.CacheHitRate.Should().BeNull());
        summary.Totals.TotalTokens.Should().Be(50);
    }

    // Every split must account for every turn: a dimension that dropped or doubled turns (a null key, a bad
    // join) would show a different total there than on the tiles.
    [Fact]
    public async Task Handle_EverySplit_SumsToThePeriodTotalsAndIsSortedByCost()
    {
        await _data.Seed(
            Session("a", email: "a@team.dev", machine: null, projectKey: "p", turns:
            [
                Turn(Now.AddHours(-5), input: 10, cacheRead: 30, cost: 1m, model: null, branch: null),
                Turn(Now.AddHours(-4), input: 20, cost: 5m, subagent: "Plan"),
            ]),
            Session("b", email: "b@team.dev", repoPath: null, agent: "codex", turns:
                [Turn(Now.AddHours(-3), output: 7, cacheWrite: 3, cost: 2m, model: "claude-opus")]));

        var summary = await Run(new(2026, 11, 10), new(2026, 11, 10), "day", "Etc/UTC");

        var splits = new[]
        {
            summary.Splits.Model, summary.Splits.Project, summary.Splits.Session, summary.Splits.Subagent,
            summary.Splits.Branch, summary.Splits.Developer, summary.Splits.Machine, summary.Splits.Agent,
        };
        summary.Totals.Should().Match<UsageTotals>(t => t.EstimatedCostUsd == 8m && t.TurnCount == 3 && t.TotalTokens == 70);
        summary.Totals.CacheHitRate.Should().BeApproximately(30d / 63, 1e-9);
        foreach (var split in splits)
        {
            split.Sum(r => r.EstimatedCostUsd).Should().Be(summary.Totals.EstimatedCostUsd);
            split.Sum(r => r.TotalTokens).Should().Be(summary.Totals.TotalTokens);
            split.Sum(r => r.TurnCount).Should().Be(summary.Totals.TurnCount);
            split.Select(r => r.EstimatedCostUsd).Should().BeInDescendingOrder();
        }
        summary.Splits.Machine.Should().Contain(r => r.Key == null && r.Label == "unknown");
    }

    // Burn is "right now": the 15 minutes before now, all four token kinds, filters applied, period ignored.
    [Fact]
    public async Task Handle_Burn_UsesLast15MinutesOfFilteredTurnsOverTheFullLookback()
    {
        await _data.Seed(
            Session("a", email: "a@team.dev", turns:
            [
                Turn(Now.AddMinutes(-5), input: 100, output: 50, cacheWrite: 50, cacheRead: 100, cost: 1.5m),
                Turn(Now.AddMinutes(-20), input: 9000, cost: 9m),
            ]),
            Session("b", email: "b@team.dev", turns: [Turn(Now.AddMinutes(-1), input: 9000, cost: 9m)]));

        var summary = await Run(new(2026, 1, 1), new(2026, 1, 1), "day", "Etc/UTC", developer: "a@team.dev");

        summary.Burn.Should().Be(new UsageBurn(20d, 0.1m, 15));
    }

    [Fact]
    public async Task Handle_EchoesThePeriodAndCarriesThePriceTableDate()
    {
        var summary = await Run(new(2026, 11, 1), new(2026, 11, 3), "day", "Asia/Kolkata");

        (summary.From, summary.To, summary.Granularity, summary.TimeZone, summary.PricesAsOf)
            .Should().Be((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), "day", "Asia/Kolkata", PricesAsOf));
        summary.Buckets.Select(b => b.Start).Should().Equal(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3));
        summary.Buckets.Should().AllSatisfy(b => b.TurnCount.Should().Be(0));
    }

    [Theory]
    [InlineData("a@team.dev", "/src/meter")]
    [InlineData(null, null)]
    public async Task Handle_EchoesTheAppliedFilters(string? developer, string? project)
    {
        var summary = await Handle(new GetUsageSummaryQuery(
            new(2026, 11, 1), new(2026, 11, 3), "day", "Etc/UTC", developer, project));

        (summary.Developer, summary.Project).Should().Be((developer, project));
    }

    private Task<UsageSummary> Run(
        DateOnly from, DateOnly to, string granularity, string zone, string? developer = null) =>
        Handle(new GetUsageSummaryQuery(from, to, granularity, zone, developer));

    private async Task<UsageSummary> Handle(GetUsageSummaryQuery query)
    {
        var handler = new GetUsageSummaryQueryHandler(_data.Repository, new FixedPrices(), new FixedTime(Now));
        var result = await handler.Handle(query, CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FixedPrices : ITokenPricingCalculator
    {
        public DateOnly PricesAsOf => GetUsageSummaryTests.PricesAsOf;

        public decimal CalculateCost(string? model, int? inputTokens, int? outputTokens,
            int? cacheCreationInputTokens, int? cacheReadInputTokens) => 0m;
    }
}
