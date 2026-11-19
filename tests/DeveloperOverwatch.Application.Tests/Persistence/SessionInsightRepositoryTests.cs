namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>The session-level reads against the real schema on SQLite, so ordering, filtering and the
/// prompt-text cut are what the database does rather than what a mock was told to return.</summary>
public sealed class SessionInsightRepositoryTests : IDisposable
{
    private static readonly DateTime T0 = UsageDatabase.Utc(2026, 11, 14, 9);
    private readonly UsageDatabase _db = new();
    private readonly SessionInsightRepository _repo;

    public SessionInsightRepositoryTests() => _repo = new SessionInsightRepository(_db.Db);

    [Fact]
    public async Task SessionExistsAsync_UnknownAndKnown_ReportsEach()
    {
        await _db.Seed(UsageDatabase.Session("s-1"));

        (await _repo.SessionExistsAsync("s-1")).Should().BeTrue();
        (await _repo.SessionExistsAsync("nope")).Should().BeFalse();
    }

    [Fact]
    public async Task GetContextHeaderAsync_KnownSession_ReturnsPeakAndToolErrors()
    {
        var session = UsageDatabase.Session("s-1");
        session.PeakContextTokens = 191_000;
        session.PeakContextModel = "claude-opus-5-5";
        session.ToolErrorCount = 2;
        session.LastToolErrorName = "Bash";
        session.LastToolErrorAt = T0;
        await _db.Seed(session);

        var header = await _repo.GetContextHeaderAsync("s-1");

        header.Should().Be(new SessionContextHeader(191_000, "claude-opus-5-5", 2, "Bash", T0));
        (await _repo.GetContextHeaderAsync("nope")).Should().BeNull();
    }

    // The newest main-thread reading wins; a later subagent turn has its own window and a later user turn measured nothing.
    [Fact]
    public async Task GetLatestMainThreadContextAsync_MixedTurns_TakesNewestMainThreadMeasurement()
    {
        await _db.Seed(UsageDatabase.Session("s-1", turns:
        [
            Measured(T0, 1, "claude-sonnet-5", 40_000),
            Measured(T0.AddMinutes(1), 2, "claude-opus-5-5", 90_000),
            Subagent(T0.AddMinutes(2), 3, 150_000),
            Prompt(T0.AddMinutes(3), 4, "next"),
        ]));

        var reading = await _repo.GetLatestMainThreadContextAsync("s-1");

        reading.Should().Be(new ContextReading("claude-opus-5-5", 90_000));
    }

    [Fact]
    public async Task GetLatestMainThreadContextAsync_NothingMeasured_ReturnsNull()
    {
        await _db.Seed(UsageDatabase.Session("s-1", turns: [Prompt(T0, 1, "hi"), Measured(T0, 2, "m", 0)]));

        (await _repo.GetLatestMainThreadContextAsync("s-1")).Should().BeNull();
    }

    // Equal timestamps fall back to turn order, so the grouping by prompt is stable between reads.
    [Fact]
    public async Task GetTurnCostRowsAsync_OrdersByTimeThenTurnIndex()
    {
        await _db.Seed(UsageDatabase.Session("s-1", turns:
        [
            Measured(T0.AddMinutes(5), 3, "m", 10),
            Measured(T0, 2, "m", 10),
            Prompt(T0, 1, "first"),
        ]));

        var rows = await _repo.GetTurnCostRowsAsync("s-1");

        rows.Select(r => r.TurnIndex).Should().Equal(1, 2, 3);
    }

    // Only main-thread user turns can start a prompt, so only they carry text; it is cut so a long paste stays small.
    [Fact]
    public async Task GetTurnCostRowsAsync_PromptText_OnlyOnUserTurnsAndCut()
    {
        var longPrompt = new string('x', 1_000);
        await _db.Seed(UsageDatabase.Session("s-1", turns:
        [
            Prompt(T0, 1, longPrompt),
            WithContent(Measured(T0.AddSeconds(1), 2, "m", 10), "assistant reply"),
            WithContent(Subagent(T0.AddSeconds(2), 3, 10), "subagent work"),
        ]));

        var rows = await _repo.GetTurnCostRowsAsync("s-1");

        rows[0].PromptText.Should().Be(longPrompt[..SessionTurnCostRow.PromptTextLength]);
        rows[1].PromptText.Should().BeNull();
        rows[2].PromptText.Should().BeNull();
        rows[2].SubagentName.Should().Be("Explore");
    }

    // One joined query would return categories x tools x compactions rows; split, it is one query per table.
    [Fact]
    public async Task GetContextSnapshotAsync_LoadsEveryRow_InOneQueryPerTable()
    {
        await _db.Seed(UsageDatabase.Session("s-1"));
        _db.Db.SessionContextSnapshots.Add(SnapshotWith(categories: 2, tools: 3, compactions: 2));
        await _db.Db.SaveChangesAsync();
        _db.Db.ChangeTracker.Clear();
        _db.Commands.Clear();

        var snapshot = await _repo.GetContextSnapshotAsync("s-1");

        snapshot!.Categories.Should().HaveCount(2);
        snapshot.Tools.Should().HaveCount(3);
        snapshot.Compactions.Should().HaveCount(2);
        _db.Commands.Executed.Should().HaveCount(4);
        _db.Db.ChangeTracker.Entries().Should().BeEmpty("a read must not be tracked");
    }

    // The ingest path reads the snapshot tracked, to replace it in place; it must split the same way.
    [Fact]
    public async Task SessionRepository_GetContextSnapshotAsync_AlsoOneQueryPerTable()
    {
        await _db.Seed(UsageDatabase.Session("s-1"));
        _db.Db.SessionContextSnapshots.Add(SnapshotWith(categories: 2, tools: 3, compactions: 2));
        await _db.Db.SaveChangesAsync();
        _db.Db.ChangeTracker.Clear();
        _db.Commands.Clear();

        var snapshot = await new SessionRepository(_db.Db).GetContextSnapshotAsync("s-1");

        snapshot!.Tools.Should().HaveCount(3);
        _db.Commands.Executed.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetContextSnapshotAsync_NoneSent_ReturnsNull()
    {
        await _db.Seed(UsageDatabase.Session("s-1"));

        (await _repo.GetContextSnapshotAsync("s-1")).Should().BeNull();
    }

    private static SessionContextSnapshot SnapshotWith(int categories, int tools, int compactions) =>
        SessionContextSnapshot.Create(
            "s-1", T0, "2.1.0", "claude-opus-5-5", 1_000,
            Enumerable.Range(0, categories).Select(i => new SessionContextCategory(ContextCategory.Skills + i, null, 10)),
            Enumerable.Range(0, tools).Select(i => new SessionContextToolGroup($"g{i}", false, 1, 5, false)),
            Enumerable.Range(0, compactions).Select(i => new SessionContextCompaction(T0.AddHours(-i), "auto", 100, 10)));

    private static ConversationTurn Measured(DateTime at, int index, string model, long context)
    {
        var turn = UsageDatabase.Turn(at, input: 10, output: 5, model: model);
        turn.TurnIndex = index;
        turn.ContextTokens = context;
        return turn;
    }

    private static ConversationTurn Subagent(DateTime at, int index, long context)
    {
        var turn = Measured(at, index, "claude-haiku", context);
        turn.Role = SessionTurnCostRow.SubagentRole;
        turn.SubagentName = "Explore";
        return turn;
    }

    private static ConversationTurn Prompt(DateTime at, int index, string content) => new()
    {
        Id = Guid.NewGuid().ToString(),
        TurnIndex = index,
        Role = SessionTurnCostRow.UserRole,
        Content = content,
        TurnKind = "prompt",
        Timestamp = at,
    };

    private static ConversationTurn WithContent(ConversationTurn turn, string content)
    {
        turn.Content = content;
        return turn;
    }

    public void Dispose() => _db.Dispose();
}
