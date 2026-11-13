namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Xunit;
using static DeveloperOverwatch.Application.Tests.Persistence.UsageDatabase;

/// <summary>The usage page's numbers are sums the database computes; these pin each grouping as real SQL.</summary>
public sealed class UsageRepositorySlotTests : IDisposable
{
    private static readonly DateTime From = Utc(2026, 11, 10);
    private static readonly DateTime To = Utc(2026, 11, 11);

    private readonly UsageDatabase _data = new();

    public void Dispose() => _data.Dispose();

    // Turn magnitudes are 1, 2 and 4 units, so every key's sum names exactly which turns it holds.
    // A unit is 100 input, 10 output, 1 cache write, 1000 cache read and $1.
    [Theory]
    [InlineData(UsageDimension.Model, "claude-sonnet:5", "claude-opus:2")]
    [InlineData(UsageDimension.Project, "proj-a:3", "/src/b:4")]
    [InlineData(UsageDimension.Session, "session-a:3", "session-b:4")]
    [InlineData(UsageDimension.Subagent, "~:5", "Explore:2")]
    [InlineData(UsageDimension.Branch, "main:1", "feat:2", "~:4")]
    [InlineData(UsageDimension.Developer, "a@team.dev:3", "b@team.dev:4")]
    [InlineData(UsageDimension.Machine, "m1:3", "~:4")]
    [InlineData(UsageDimension.Agent, "claude-code:3", "codex:4")]
    public async Task GetSlotsAsync_EachDimension_SumsEveryTokenKindAndCostPerKey(
        UsageDimension dimension, params string[] expected)
    {
        await SeedTwoSessionsInOneSlot();

        var rows = await _data.Repository.GetSlotsAsync(From, To, UsageFilter.None);

        rows.Should().OnlyContain(r => r.SlotStartUtc == Utc(2026, 11, 10, 10, 0) && r.SlotStartUtc.Kind == DateTimeKind.Utc);
        rows.GroupBy(r => r.KeyOf(dimension))
            .Select(g => (g.Key, g.Sum(r => r.InputTokens), g.Sum(r => r.OutputTokens),
                g.Sum(r => r.CacheCreationInputTokens), g.Sum(r => r.CacheReadInputTokens), g.Sum(r => r.EstimatedCostUsd)))
            .Should().BeEquivalentTo(expected.Select(Units));
    }

    // Every split regroups these rows in memory, so each row must carry its own session's attributes.
    [Fact]
    public async Task GetSlotsAsync_EachRow_CarriesItsTurnKeysAndItsSessionsAttributes()
    {
        await SeedTwoSessionsInOneSlot();

        var rows = await _data.Repository.GetSlotsAsync(From, To, UsageFilter.None);

        rows.Select(r => (r.SessionId, r.Model, r.SubagentName, r.GitBranch, r.Project, r.DeveloperEmail,
                r.MachineName, r.Agent, r.TurnCount))
            .Should().BeEquivalentTo(new (string, string?, string?, string?, string?, string, string?, string, int)[]
            {
                ("session-a", "claude-sonnet", null, "main", "proj-a", "a@team.dev", "m1", "claude-code", 1),
                ("session-a", "claude-opus", "Explore", "feat", "proj-a", "a@team.dev", "m1", "claude-code", 1),
                ("session-b", "claude-sonnet", null, null, "/src/b", "b@team.dev", null, "codex", 1),
            });
    }

    [Fact]
    public async Task GetSlotsAsync_TurnsEitherSideOfQuarterHour_LandInSeparateSlots()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 10, 9, 45), input: 1),
            Turn(Utc(2026, 11, 10, 9, 59, 59), input: 2),
            Turn(Utc(2026, 11, 10, 10, 0), input: 4),
        ]));

        var rows = await _data.Repository.GetSlotsAsync(From, To, UsageFilter.None);

        rows.OrderBy(r => r.SlotStartUtc).Select(r => (r.SlotStartUtc, r.InputTokens, r.TurnCount))
            .Should().Equal((Utc(2026, 11, 10, 9, 45), 3L, 2), (Utc(2026, 11, 10, 10, 0), 4L, 1));
    }

    [Fact]
    public async Task GetSlotsAsync_FirstAndLastAt_AreTheSlotsEarliestAndLatestTurnsInUtc()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(Utc(2026, 11, 10, 10, 7, 30)),
            Turn(Utc(2026, 11, 10, 10, 1, 5)),
            Turn(Utc(2026, 11, 10, 10, 12)),
        ]));

        var row = (await _data.Repository.GetSlotsAsync(From, To, UsageFilter.None)).Single();

        row.FirstAtUtc.Should().Be(Utc(2026, 11, 10, 10, 1, 5));
        row.LastAtUtc.Should().Be(Utc(2026, 11, 10, 10, 12));
        row.FirstAtUtc.Kind.Should().Be(DateTimeKind.Utc, "an unmarked time would be read as local by the dashboard");
        row.LastAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetSlotsAsync_RangeIsHalfOpen_IncludesFromAndExcludesTo()
    {
        await _data.Seed(Session("s", turns:
        [
            Turn(From.AddTicks(-1), input: 1),
            Turn(From, input: 2),
            Turn(To.AddTicks(-1), input: 4),
            Turn(To, input: 8),
        ]));

        var rows = await _data.Repository.GetSlotsAsync(From, To, UsageFilter.None);

        rows.Sum(r => r.InputTokens).Should().Be(6, "adjacent ranges must never count a turn twice or drop one");
    }

    // Rule 20: the Agent keeps its local log for 30 days, but the server is the long-term record. A turn far
    // older than that must still be summed.
    [Fact]
    public async Task GetSlotsAsync_TurnFrom400DaysAgo_IsStillCounted()
    {
        var longAgo = From.AddDays(-400);
        await _data.Seed(Session("old", turns: [Turn(longAgo.AddHours(3), input: 7, cost: 0.5m)]));

        var rows = await _data.Repository.GetSlotsAsync(longAgo, To, UsageFilter.None);

        rows.Should().ContainSingle().Which.InputTokens.Should().Be(7);
    }

    [Fact]
    public async Task GetSlotsAsync_DeveloperFilter_KeepsOnlyThatDevelopersTurns()
    {
        await SeedTwoSessionsInOneSlot();

        var rows = await _data.Repository.GetSlotsAsync(From, To, new UsageFilter("b@team.dev", null));

        rows.Select(r => r.DeveloperEmail).Should().Equal("b@team.dev");
    }

    [Theory]
    [InlineData("proj-a", "session-a")]
    [InlineData("/src/b", "session-b")]
    public async Task GetSlotsAsync_ProjectFilter_MatchesKeyOrRepoPathWhenNoKey(string project, string session)
    {
        await SeedTwoSessionsInOneSlot();

        var rows = await _data.Repository.GetSlotsAsync(From, To, new UsageFilter(null, project));

        rows.Select(r => r.SessionId).Distinct().Should().Equal(session);
    }

    [Fact]
    public async Task GetTurnPointsAsync_WindowExcludesStartIncludesEnd_AndSumsAllFourTokenKinds()
    {
        var until = Utc(2026, 11, 10, 12, 0);
        var after = until.AddMinutes(-15);
        await _data.Seed(
            Session("a", email: "a@team.dev", turns:
            [
                Turn(after, input: 1000, cost: 9m),
                Turn(after.AddSeconds(1), input: 1, output: 2, cacheWrite: 4, cacheRead: 8, cost: 0.25m),
                Turn(until, input: 16, cost: 0.5m),
                Turn(until.AddSeconds(1), input: 1000, cost: 9m),
            ]),
            Session("b", email: "b@team.dev", turns: [Turn(until.AddMinutes(-1), input: 1000, cost: 9m)]));

        var points = await _data.Repository.GetTurnPointsAsync(after, until, new UsageFilter("a@team.dev", null));

        points.Select(p => (p.Tokens, p.CostUsd)).Should().BeEquivalentTo([(15L, 0.25m), (16L, 0.5m)]);
    }

    [Fact]
    public async Task GetDeveloperTurnPointsAsync_EveryDeveloperInWindow_CarriesEmailAndUtcTime()
    {
        var until = Utc(2026, 11, 10, 12, 0);
        var after = until.AddHours(-24);
        await _data.Seed(
            Session("a", email: "a@team.dev", turns:
            [
                Turn(after, input: 1000, cost: 9m),
                Turn(after.AddSeconds(1), input: 1, output: 2, cacheWrite: 4, cacheRead: 8, cost: 0.25m),
                Turn(until.AddSeconds(1), input: 1000, cost: 9m),
            ]),
            Session("b", email: "b@team.dev", projectKey: "other", turns: [Turn(until, input: 16, cost: 0.5m)]));

        var points = await _data.Repository.GetDeveloperTurnPointsAsync(after, until);

        points.Should().BeEquivalentTo(
        [
            new DeveloperTurnPoint("a@team.dev", after.AddSeconds(1), 15L, 0.25m),
            new DeveloperTurnPoint("b@team.dev", until, 16L, 0.5m),
        ]);
        points.Should().OnlyContain(p => p.AtUtc.Kind == DateTimeKind.Utc);
    }

    private Task SeedTwoSessionsInOneSlot() => _data.Seed(
        Session("session-a", email: "a@team.dev", machine: "m1", projectKey: "proj-a", agent: "claude-code", turns:
        [
            UnitTurn(Utc(2026, 11, 10, 10, 5), 1, "claude-sonnet", "main", null),
            UnitTurn(Utc(2026, 11, 10, 10, 10), 2, "claude-opus", "feat", "Explore"),
        ]),
        Session("session-b", email: "b@team.dev", machine: null, projectKey: null, repoPath: "/src/b", agent: "codex",
            turns: [UnitTurn(Utc(2026, 11, 10, 10, 12), 4, "claude-sonnet", null, null)]));

    private static Domain.Entities.ConversationTurn UnitTurn(
        DateTime at, int units, string model, string? branch, string? subagent) =>
        Turn(at, input: 100 * units, output: 10 * units, cacheWrite: units, cacheRead: 1000 * units, cost: units,
            model: model, branch: branch, subagent: subagent);

    private static (string? Key, long, long, long, long, decimal) Units(string keyAndUnits)
    {
        var split = keyAndUnits.LastIndexOf(':');
        var key = keyAndUnits[..split];
        var units = int.Parse(keyAndUnits[(split + 1)..]);
        return (key == "~" ? null : key, 100L * units, 10L * units, units, 1000L * units, units);
    }
}
