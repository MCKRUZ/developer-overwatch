namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static IngestDatabase;

/// <summary>
/// A session keeps only its latest context snapshot. Each snapshot is complete, so a newer one replaces the
/// stored one whole, rows included; batches arrive out of order on retries, so an older one must never
/// overwrite a newer one, or the dashboard would show a context breakdown the session has moved past.
/// </summary>
public sealed class IngestContextSnapshotTests : IDisposable
{
    private readonly IngestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Ingest_WithSnapshot_StoresItWithEveryRow()
    {
        var sessionId = await _db.StartSession();
        var takenAt = Now.AddMinutes(-10);

        (await _db.Ingest(Command(sessionId, Snapshot(takenAt)))).IsSuccess.Should().BeTrue();

        var stored = await _db.StoredSnapshot();
        stored.TakenAt.Should().Be(takenAt);
        stored.TakenAt.Kind.Should().Be(DateTimeKind.Utc);
        stored.ClaudeCodeVersion.Should().Be("2.1.40");
        stored.Model.Should().Be("claude-opus-5-5");
        stored.MeasuredContextTokens.Should().Be(1000);
        stored.Categories.Select(c => (c.Category, c.Server, c.Tokens)).Should().BeEquivalentTo(new[]
        {
            (ContextCategory.SystemPrompt, (string?)null, 100L),
            (ContextCategory.McpTools, "github", 300L),
            (ContextCategory.McpTools, "(other servers)", 50L),
            (ContextCategory.Conversation, null, 550L),
        });
        stored.Tools.Select(t => (t.Group, t.IsMcp, t.ToolCount, t.TokensPerRequest, t.Used)).Should().BeEquivalentTo(new[]
        {
            ("github", true, 3, 300L, false),
            ("built-in", false, 12, 900L, true),
        });
        stored.Compactions.Select(c => (c.At, c.Trigger, c.PreTokens, c.PostTokens)).Should().BeEquivalentTo(new[]
        {
            (takenAt.AddHours(-2), "auto", 180000L, 20000L),
            (takenAt.AddHours(-1), "manual", 90000L, 15000L),
        });
        stored.Compactions.Should().OnlyContain(c => c.At.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task Ingest_NewerSnapshot_ReplacesTheStoredOneAndEveryCollectionWhole()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-10))));
        var newer = Snapshot(Now.AddMinutes(-5), model: "claude-sonnet-5") with
        {
            ClaudeCodeVersion = "2.1.41",
            MeasuredContextTokens = 2000,
            Categories = [new() { Category = ContextCategory.Skills, Tokens = 2000 }],
            Tools = [new() { Group = "jira", IsMcp = true, ToolCount = 1, TokensPerRequest = 40, Used = true }],
            Compactions = [],
        };

        await _db.Ingest(Command(sessionId, newer));

        var stored = await _db.StoredSnapshot();
        stored.TakenAt.Should().Be(Now.AddMinutes(-5));
        stored.ClaudeCodeVersion.Should().Be("2.1.41");
        stored.Model.Should().Be("claude-sonnet-5");
        stored.MeasuredContextTokens.Should().Be(2000);
        stored.Categories.Should().ContainSingle().Which.Category.Should().Be(ContextCategory.Skills);
        stored.Tools.Should().ContainSingle().Which.Group.Should().Be("jira");
        stored.Compactions.Should().BeEmpty();
        await AssertRowCounts(snapshots: 1, categories: 1, tools: 1, compactions: 0);
    }

    [Fact]
    public async Task Ingest_OlderSnapshot_IsIgnored()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-5))));

        var result = await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-10), model: "older-model") with
        {
            Categories = [new() { Category = ContextCategory.Skills, Tokens = 1 }],
        }));

        result.IsSuccess.Should().BeTrue();
        var stored = await _db.StoredSnapshot();
        stored.TakenAt.Should().Be(Now.AddMinutes(-5));
        stored.Model.Should().Be("claude-opus-5-5");
        stored.Categories.Should().HaveCount(4);
    }

    [Fact]
    public async Task Ingest_SameSnapshotResent_ReplacesItWithoutDuplicatingRows()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-5))));

        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-5), model: "same-moment")));

        (await _db.StoredSnapshot()).Model.Should().Be("same-moment");
        await AssertRowCounts(snapshots: 1, categories: 4, tools: 2, compactions: 2);
    }

    // A fast client clock must not store a snapshot in the future, where only a later-stamped one could
    // ever replace it and every honest snapshot after it would be ignored.
    [Fact]
    public async Task Ingest_SnapshotFromTheFuture_IsStoredAtNowAndStillReplacedByTheNextOne()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddHours(3))));
        (await _db.StoredSnapshot()).TakenAt.Should().Be(Now);

        await _db.Ingest(Command(sessionId, Snapshot(Now, model: "next")));

        (await _db.StoredSnapshot()).Model.Should().Be("next");
    }

    [Fact]
    public async Task Ingest_BatchWithoutSnapshot_LeavesTheStoredOneAlone()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-5))));

        await _db.Ingest(Command(sessionId, snapshot: null));

        (await _db.StoredSnapshot()).Categories.Should().HaveCount(4);
    }

    [Fact]
    public async Task DeletingTheSession_DeletesItsSnapshotAndRows()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Command(sessionId, Snapshot(Now.AddMinutes(-5))));

        _db.Db.Sessions.Remove(await _db.Db.Sessions.SingleAsync(s => s.Id == sessionId));
        await _db.Db.SaveChangesAsync();

        await AssertRowCounts(snapshots: 0, categories: 0, tools: 0, compactions: 0);
    }

    private async Task AssertRowCounts(int snapshots, int categories, int tools, int compactions)
    {
        (await _db.RowCount("SessionContextSnapshots")).Should().Be(snapshots);
        (await _db.RowCount("SessionContextCategories")).Should().Be(categories);
        (await _db.RowCount("SessionContextTools")).Should().Be(tools);
        (await _db.RowCount("SessionContextCompactions")).Should().Be(compactions);
    }

    // The Agent sends a snapshot only alongside turns, so every command here carries one.
    private static IngestSessionEventsCommand Command(string sessionId, ContextSnapshotDto? snapshot) => new()
    {
        SessionId = sessionId,
        ConversationTurns =
        [
            new() { Id = Guid.NewGuid().ToString("N"), Role = "user", TurnKind = "prompt", Timestamp = Now.AddMinutes(-1) },
        ],
        ContextSnapshot = snapshot,
    };
}
