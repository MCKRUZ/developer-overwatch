namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

/// <summary>
/// Stored turn costs must always reflect the price table the dashboard labels them with, so rows
/// priced by an older table are recomputed from their stored tokens when the table's date changes.
/// </summary>
public sealed class TurnRepricerTests : IDisposable
{
    private static readonly DateOnly CurrentPrices = new(2026, 11, 9);
    private static readonly DateOnly OldPrices = new(2026, 6, 1);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OverwatchDbContext> _options;
    private readonly ITokenPricingCalculator _pricing = Substitute.For<ITokenPricingCalculator>();

    public TurnRepricerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OverwatchDbContext>().UseSqlite(_connection).Options;
        using var db = new OverwatchDbContext(_options);
        db.Database.EnsureCreated();

        // Current table: $0.001 per input token, $0.01 per output token, opus at 2x.
        _pricing.PricesAsOf.Returns(CurrentPrices);
        _pricing.CalculateCost(Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(ci =>
            {
                var rate = ci.ArgAt<string?>(0) == "opus" ? 2m : 1m;
                return rate * (((ci.ArgAt<int?>(1) ?? 0) * 0.001m) + ((ci.ArgAt<int?>(2) ?? 0) * 0.01m));
            });
    }

    private static ConversationTurn Turn(
        string id, string sessionId, int index, decimal? cost, DateOnly? pricesAsOf,
        int? input = 100, int? output = 10, string? model = "opus") => new()
    {
        Id = id,
        SessionId = sessionId,
        TurnIndex = index,
        Role = "assistant",
        InputTokens = input,
        OutputTokens = output,
        Model = model,
        EstimatedCostUsd = cost,
        PricesAsOf = pricesAsOf,
        Timestamp = DateTime.UtcNow,
    };

    private async Task SeedAsync(Session session, params ConversationTurn[] turns)
    {
        await using var db = new OverwatchDbContext(_options);
        db.Sessions.Add(session);
        db.ConversationTurns.AddRange(turns);
        await db.SaveChangesAsync();
    }

    private async Task<(int Turns, int Sessions)> RepriceAsync()
    {
        await using var db = new OverwatchDbContext(_options);
        var result = await new TurnRepricer(db, _pricing, NullLogger<TurnRepricer>.Instance).RepriceAsync();
        return (result.TurnCount, result.SessionCount);
    }

    private async Task<(Session Session, List<ConversationTurn> Turns)> LoadAsync(string sessionId)
    {
        await using var db = new OverwatchDbContext(_options);
        var session = await db.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        var turns = await db.ConversationTurns.AsNoTracking()
            .Where(t => t.SessionId == sessionId).OrderBy(t => t.TurnIndex).ToListAsync();
        return (session, turns);
    }

    [Fact]
    public async Task RepriceAsync_TurnsPricedByOldTable_RecomputesTurnCostsAndSessionTotal()
    {
        // Old table charged 3x: 0.6 per turn. Current table: 2 * (0.1 + 0.1) = 0.4.
        await SeedAsync(
            new Session { Id = "s1", EstimatedCostUsd = 1.2m },
            Turn("t1", "s1", 0, 0.6m, null),
            Turn("t2", "s1", 1, 0.6m, OldPrices));

        var (turnCount, sessionCount) = await RepriceAsync();

        var (session, turns) = await LoadAsync("s1");
        turns.Should().AllSatisfy(t =>
        {
            t.EstimatedCostUsd.Should().Be(0.4m);
            t.PricesAsOf.Should().Be(CurrentPrices);
        });
        session.EstimatedCostUsd.Should().Be(0.8m);
        turnCount.Should().Be(2);
        sessionCount.Should().Be(1);
    }

    [Fact]
    public async Task RepriceAsync_SessionWithCurrentAndStaleTurns_TotalIncludesUntouchedTurns()
    {
        // t1 is already at the current date with a deliberately distinctive cost: it must not be
        // recomputed, but it must still count towards the session total.
        await SeedAsync(
            new Session { Id = "s1", EstimatedCostUsd = 0.6m + 0.123m },
            Turn("t1", "s1", 0, 0.123m, CurrentPrices),
            Turn("t2", "s1", 1, 0.6m, null, model: "sonnet"));

        var (turnCount, _) = await RepriceAsync();

        var (session, turns) = await LoadAsync("s1");
        turns[0].EstimatedCostUsd.Should().Be(0.123m);
        turns[1].EstimatedCostUsd.Should().Be(0.2m);
        session.EstimatedCostUsd.Should().Be(0.323m);
        turnCount.Should().Be(1);
    }

    [Fact]
    public async Task RepriceAsync_RunTwice_SecondRunChangesNothing()
    {
        await SeedAsync(
            new Session { Id = "s1", EstimatedCostUsd = 1.2m },
            Turn("t1", "s1", 0, 0.6m, null),
            Turn("t2", "s1", 1, 0.6m, OldPrices));
        await RepriceAsync();

        var (turnCount, sessionCount) = await RepriceAsync();

        var (session, turns) = await LoadAsync("s1");
        turnCount.Should().Be(0);
        sessionCount.Should().Be(0);
        session.EstimatedCostUsd.Should().Be(0.8m);
        turns.Should().AllSatisfy(t => t.EstimatedCostUsd.Should().Be(0.4m));
    }

    [Fact]
    public async Task RepriceAsync_TurnsWithoutTokenData_AreLeftAlone()
    {
        await SeedAsync(
            new Session { Id = "s1", EstimatedCostUsd = 5m },
            Turn("t1", "s1", 0, null, null, input: null, output: null, model: null));

        var (turnCount, sessionCount) = await RepriceAsync();

        var (session, turns) = await LoadAsync("s1");
        turnCount.Should().Be(0);
        sessionCount.Should().Be(0);
        turns[0].PricesAsOf.Should().BeNull();
        turns[0].EstimatedCostUsd.Should().BeNull();
        session.EstimatedCostUsd.Should().Be(5m);
    }

    [Fact]
    public async Task RepriceAsync_MoreTurnsThanOneBatch_RepricesEveryTurn()
    {
        var turns = Enumerable.Range(0, TurnRepricer.BatchSize + 7)
            .Select(i => Turn($"t{i:D5}", "s1", i, 9m, null))
            .ToArray();
        await SeedAsync(new Session { Id = "s1" }, turns);

        var (turnCount, sessionCount) = await RepriceAsync();

        var (session, stored) = await LoadAsync("s1");
        turnCount.Should().Be(turns.Length);
        sessionCount.Should().Be(1);
        stored.Should().AllSatisfy(t => t.EstimatedCostUsd.Should().Be(0.4m));
        session.EstimatedCostUsd.Should().Be(0.4m * turns.Length);
    }

    public void Dispose() => _connection.Dispose();
}
