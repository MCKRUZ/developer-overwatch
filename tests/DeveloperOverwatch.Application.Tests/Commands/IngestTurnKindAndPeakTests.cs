namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static IngestDatabase;

/// <summary>
/// A turn's kind is what lets the server charge a session's cost to the prompt that caused it, so it must be
/// stored as sent and an unknown kind refused rather than stored. The session's peak context is shown
/// against the context window of the model that reached it, so the peak and its model move together.
/// </summary>
public sealed class IngestTurnKindAndPeakTests : IDisposable
{
    private readonly IngestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Ingest_TurnKinds_AreStoredAsSent()
    {
        var sessionId = await _db.StartSession();

        await _db.Ingest(Turns(sessionId,
            UserTurn("t-prompt", "prompt"),
            UserTurn("t-injected", "injected"),
            UserTurn("t-summary", "compactSummary"),
            UserTurn("t-none", null)));

        var kinds = await _db.Db.ConversationTurns.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.TurnKind);
        kinds.Should().Equal(new Dictionary<string, string?>
        {
            ["t-prompt"] = "prompt",
            ["t-injected"] = "injected",
            ["t-summary"] = "compactSummary",
            ["t-none"] = null,
        });
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("Prompt")]
    [InlineData("")]
    public async Task Ingest_UnknownTurnKind_RejectsOnlyThatTurn(string kind)
    {
        var sessionId = await _db.StartSession();

        var result = await _db.Ingest(Turns(sessionId, UserTurn("good", "prompt"), UserTurn("bad", kind)));

        result.Value!.Results.Accepted.Should().Equal("good");
        result.Value.Results.Rejected.Should().ContainSingle()
            .Which.Should().Match<RejectedItem>(r => r.Id == "bad" && r.Reason.Contains("turnKind"));
    }

    // The model is copied into the session's bounded peak-model column, so an overlong one is refused on
    // its own instead of failing the whole save.
    [Fact]
    public async Task Ingest_TurnModelLongerThan255_RejectsOnlyThatTurn()
    {
        var sessionId = await _db.StartSession();

        var result = await _db.Ingest(Turns(sessionId,
            AssistantTurn("good", 10), AssistantTurn("bad", 20) with { Model = new string('m', 256) }));

        result.Value!.Results.Accepted.Should().Equal("good");
        result.Value.Results.Rejected.Should().ContainSingle()
            .Which.Should().Match<RejectedItem>(r => r.Id == "bad" && r.Reason.Contains("model"));
    }

    [Fact]
    public async Task Ingest_MainThreadTurnRaisesThePeak_RecordsItsModel()
    {
        var sessionId = await _db.StartSession();

        await _db.Ingest(Turns(sessionId,
            AssistantTurn("a", 1000) with { Model = "claude-sonnet-5" },
            AssistantTurn("b", 5000) with { Model = "claude-opus-5-5" }));

        var session = await _db.Db.Sessions.AsNoTracking().SingleAsync();
        session.PeakContextTokens.Should().Be(5000);
        session.PeakContextModel.Should().Be("claude-opus-5-5");
    }

    [Fact]
    public async Task Ingest_TurnThatDoesNotRaiseThePeak_LeavesThePeakModel()
    {
        var sessionId = await _db.StartSession();
        await _db.Ingest(Turns(sessionId, AssistantTurn("a", 5000) with { Model = "claude-opus-5-5" }));

        await _db.Ingest(Turns(sessionId,
            AssistantTurn("equal", 5000) with { Model = "equal-model" },
            AssistantTurn("lower", 100) with { Model = "lower-model" },
            AssistantTurn("subagent", 90000) with { Model = "subagent-model", Role = "subagent" }));

        var session = await _db.Db.Sessions.AsNoTracking().SingleAsync();
        session.PeakContextTokens.Should().Be(5000);
        session.PeakContextModel.Should().Be("claude-opus-5-5");
    }

    private static IngestSessionEventsCommand Turns(string sessionId, params ConversationTurnDto[] turns) =>
        new() { SessionId = sessionId, ConversationTurns = turns };

    private static ConversationTurnDto UserTurn(string id, string? kind) =>
        new() { Id = id, Role = "user", Content = "hello", TurnKind = kind, Timestamp = Now.AddMinutes(-1) };

    private static ConversationTurnDto AssistantTurn(string id, int input) =>
        new() { Id = id, Role = "assistant", InputTokens = input, Model = "claude-opus-5-5", Timestamp = Now.AddMinutes(-1) };
}
