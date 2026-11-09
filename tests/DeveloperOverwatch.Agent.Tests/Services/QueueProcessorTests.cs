using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class QueueProcessorTests
{
    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitFallbackWriter _gitFallback;
    private readonly IConversationCapture _conversationCapture;
    private readonly IConversationCursorStore _cursorStore;
    private readonly AgentConfig _config;
    private readonly QueueProcessor _sut;

    public QueueProcessorTests()
    {
        _queue = Substitute.For<IEventQueue>();
        _api = Substitute.For<IApiClient>();
        _redactor = Substitute.For<IPrivacyRedactor>();
        _gitFallback = Substitute.For<IGitFallbackWriter>();
        _conversationCapture = Substitute.For<IConversationCapture>();
        _cursorStore = Substitute.For<IConversationCursorStore>();

        _redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        _config = new AgentConfig
        {
            PollIntervalSeconds = 1,
            BatchSize = 50,
            GitFallbackThreshold = 100,
            // Never the real ~/.overwatch: the processor imports and deletes its fallback file.
            OverwatchDir = Path.Combine(Path.GetTempPath(), $"ow-qp-{Guid.NewGuid():N}")
        };

        _gitRemote = Substitute.For<IGitRemoteReader>();

        var logger = Substitute.For<ILogger<QueueProcessor>>();
        _sut = new QueueProcessor(
            _queue, _api, _redactor, _gitFallback, _conversationCapture, _cursorStore, _gitRemote,
            Substitute.For<IUsageStore>(), _config, logger);
    }

    private readonly IGitRemoteReader _gitRemote;

    private static QueuedEvent CreateEvent(
        string sessionId = "session-1",
        EventType type = EventType.ToolExecution,
        string payload = "{\"tool\":\"read\"}") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = DateTime.UtcNow
    };

    // Every group also ships its activity signals last; QueueProcessorSessionSignalsTests covers them.
    private static bool IsSignals(string payload) => payload.Contains("\"activityEvents\"");

    private async Task RunOneTickAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _sut.StartAsync(cts.Token);
        await Task.Delay(1500);
        await _sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessBatch_ShipsEvents_MarksAsShipped()
    {
        var evt = CreateEvent();
        var batch = new List<QueuedEvent> { evt };
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batch, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionEventsAsync(
            evt.SessionId,
            evt.PayloadJson,
            Arg.Any<CancellationToken>());
        await _queue.Received(1).MarkShippedAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Contains(evt.Id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_EmptyQueue_DoesNothing()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _api.DidNotReceive().SendSessionStartAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEventsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEndAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().MarkShippedAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_ApiFailure_IncrementsRetry()
    {
        var evt = CreateEvent();
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _queue.GetUnshippedCountAsync(Arg.Any<CancellationToken>())
            .Returns(1);

        await RunOneTickAsync();

        await _queue.Received(1).IncrementRetryAsync(evt.Id, Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().MarkShippedAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_RedactsBeforeShipping()
    {
        var rawPayload = "{\"secret\":\"sk-1234567890abcdef\"}";
        var redactedPayload = "{\"secret\":\"[REDACTED]\"}";
        var evt = CreateEvent(payload: rawPayload);

        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _redactor.Redact(rawPayload).Returns(redactedPayload);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        _redactor.Received(1).Redact(rawPayload);
        await _api.Received(1).SendSessionEventsAsync(
            evt.SessionId,
            redactedPayload,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SessionStartEvent_CallsSendSessionStart()
    {
        var evt = CreateEvent(type: EventType.SessionStart, payload: "{\"init\":true}");
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionStartAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEventsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SessionEndEvent_CallsSendSessionEnd()
    {
        var evt = CreateEvent(type: EventType.SessionEnd, payload: "{\"end\":true}");
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionEndAsync(
            evt.SessionId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_MultipleSessionsInBatch_GroupsCorrectly()
    {
        var evt1 = CreateEvent(sessionId: "session-A");
        var evt2 = CreateEvent(sessionId: "session-B");
        var evt3 = CreateEvent(sessionId: "session-A");

        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt1, evt2, evt3 }, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received().SendSessionEventsAsync(
            "session-A", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received().SendSessionEventsAsync(
            "session-B", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_InitializesQueueOnStart()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _queue.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_PurgesOldEventsAfterBatch()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _queue.Received().PurgeShippedAsync(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task ConversationTurn_WithCustomTitleSidecar_IncludesSessionTitleInPayload()
    {
        var projectDir = Directory.CreateTempSubdirectory();
        try
        {
            var transcriptPath = Path.Combine(projectDir.FullName, "abc-session.jsonl");
            File.WriteAllText(transcriptPath, "{}");
            var sidecarDir = Directory.CreateDirectory(Path.Combine(projectDir.FullName, "abc-session"));
            File.WriteAllText(
                Path.Combine(sidecarDir.FullName, "custom-title.json"),
                "{\"customTitle\":\"renamed-session\"}");

            var evt = CreateEvent(
                type: EventType.ConversationTurn,
                payload: $"{{\"transcriptPath\":{JsonSerializer.Serialize(transcriptPath)}}}");
            _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new List<QueuedEvent> { evt }, []);
            _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
                .Returns(new ConversationCaptureResult(
                    [new CapturedTurn("uuid:u-1", "user", "hello", DateTime.UtcNow)], 1));
            _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            await RunOneTickAsync();

            await _api.Received(1).SendSessionEventsAsync(
                evt.SessionId,
                Arg.Is<string>(json => json.Contains("\"sessionTitle\":\"renamed-session\"")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            projectDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConversationTurn_WithoutCustomTitleSidecar_OmitsSessionTitleAsNull()
    {
        var projectDir = Directory.CreateTempSubdirectory();
        try
        {
            var transcriptPath = Path.Combine(projectDir.FullName, "abc-session.jsonl");
            File.WriteAllText(transcriptPath, "{}");

            var evt = CreateEvent(
                type: EventType.ConversationTurn,
                payload: $"{{\"transcriptPath\":{JsonSerializer.Serialize(transcriptPath)}}}");
            _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new List<QueuedEvent> { evt }, []);
            _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
                .Returns(new ConversationCaptureResult(
                    [new CapturedTurn("uuid:u-1", "user", "hello", DateTime.UtcNow)], 1));
            _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);

            await RunOneTickAsync();

            await _api.Received(1).SendSessionEventsAsync(
                evt.SessionId,
                Arg.Is<string>(json => json.Contains("\"sessionTitle\":null")),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            projectDir.Delete(recursive: true);
        }
    }

    private QueuedEvent ArrangeConversationEvent(string transcriptPath, string sessionId = "session-1")
    {
        var evt = CreateEvent(
            sessionId: sessionId,
            type: EventType.ConversationTurn,
            payload: $"{{\"transcriptPath\":{JsonSerializer.Serialize(transcriptPath)}}}");
        return evt;
    }

    private static List<JsonElement> TurnsIn(string payload) =>
        JsonDocument.Parse(payload).RootElement.GetProperty("conversationTurns").EnumerateArray().ToList();

    [Fact]
    public async Task ConversationTurn_BranchAndSubagentName_AreShippedOnEachTurn()
    {
        const string transcriptPath = "C:/nowhere/session.jsonl";
        const string subagentPath = "C:/nowhere/session/subagents/agent-a1.jsonl";
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { ArrangeConversationEvent(transcriptPath) }, []);
        var t0 = DateTime.UtcNow;
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_A", "assistant", "hi", t0, GitBranch: "feat/meter")], 4));
        _conversationCapture.FindSubagentTranscripts(transcriptPath).Returns([subagentPath]);
        _conversationCapture.CaptureFromFileAsync(subagentPath, Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_S1", "subagent", null, t0.AddSeconds(1), GitBranch: "feat/meter", SubagentName: "Explore")], 3));
        string? payload = null;
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => { if (!IsSignals(p)) payload = p; }), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        var turns = TurnsIn(payload!);
        turns.Select(t => t.GetProperty("gitBranch").GetString()).Should().Equal("feat/meter", "feat/meter");
        turns.Select(t => t.GetProperty("subagentName").GetString()).Should().Equal(null, "Explore");
    }

    private void ArrangeSessionStart(string payload)
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { CreateEvent(type: EventType.SessionStart, payload: payload) }, []);
        _api.SendSessionStartAsync(Arg.Do<string>(p => _sentStart = p), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private string? _sentStart;

    [Fact]
    public async Task SessionStart_WithRepoRemote_CarriesAgentMachineAndCredentialFreeProjectKey()
    {
        ArrangeSessionStart("""{"sessionId":"s1","developerEmail":"dev@example.com","repoPath":"C:/Code Repos/widget"}""");
        _gitRemote.GetOriginUrlAsync("C:/Code Repos/widget", Arg.Any<CancellationToken>())
            .Returns("https://dev:ghp_secret@GitHub.com/acme/widget.git\n");

        await RunOneTickAsync();

        var body = JsonDocument.Parse(_sentStart!).RootElement;
        body.GetProperty("agent").GetString().Should().Be("claude-code");
        body.GetProperty("machineName").GetString().Should().Be(Environment.MachineName);
        body.GetProperty("projectKey").GetString().Should().Be("github.com/acme/widget");
        body.GetProperty("sessionId").GetString().Should().Be("s1");
        body.GetProperty("developerEmail").GetString().Should().Be("dev@example.com");
        _sentStart.Should().NotContain("ghp_secret");
    }

    [Fact]
    public async Task SessionStart_RepoWithoutRemote_FallsBackToFolderName()
    {
        ArrangeSessionStart("""{"sessionId":"s1","repoPath":"C:/Code Repos/widget"}""");
        _gitRemote.GetOriginUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        await RunOneTickAsync();

        JsonDocument.Parse(_sentStart!).RootElement.GetProperty("projectKey").GetString().Should().Be("widget");
    }

    [Fact]
    public async Task SessionStart_NoRepoPath_NeverRunsGitAndSendsNullProjectKey()
    {
        ArrangeSessionStart("""{"sessionId":"s1"}""");

        await RunOneTickAsync();

        await _gitRemote.DidNotReceive().GetOriginUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var body = JsonDocument.Parse(_sentStart!).RootElement;
        body.GetProperty("projectKey").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("agent").GetString().Should().Be("claude-code");
    }

    [Fact]
    public async Task SessionStart_PayloadNotAnObject_IsStillSentUnchanged()
    {
        ArrangeSessionStart("not json");

        await RunOneTickAsync();

        _sentStart.Should().Be("not json");
    }

    [Fact]
    public async Task ConversationTurn_SameSourceIdShippedTwice_GetsTheSameTurnIdBothTimes()
    {
        // Retries and resumed-session copies resend the same transcript lines. Only a turn id
        // derived from the line's own id lets the server recognise the resend and skip it.
        const string transcriptPath = "C:/nowhere/session.jsonl";
        var first = ArrangeConversationEvent(transcriptPath);
        var second = ArrangeConversationEvent(transcriptPath);
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { first, second }, []);
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_A", "assistant", "hi", DateTime.UtcNow),
                 new CapturedTurn("msg:msg_B", "assistant", "there", DateTime.UtcNow)], 2));
        var payloads = new List<string>();
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => { if (!IsSignals(p)) payloads.Add(p); }), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        payloads.Should().HaveCount(2);
        var firstIds = TurnsIn(payloads[0]).Select(t => t.GetProperty("id").GetString()).ToList();
        var secondIds = TurnsIn(payloads[1]).Select(t => t.GetProperty("id").GetString()).ToList();
        secondIds.Should().Equal(firstIds);
        firstIds.Should().OnlyHaveUniqueItems();
        firstIds.Should().AllSatisfy(id => Guid.TryParse(id, out _).Should().BeTrue());
    }

    [Fact]
    public async Task ConversationTurn_TokenCount_IncludesCacheTokens()
    {
        const string transcriptPath = "C:/nowhere/session.jsonl";
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { ArrangeConversationEvent(transcriptPath) }, []);
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_A", "assistant", null, DateTime.UtcNow,
                    InputTokens: 3, OutputTokens: 50, CacheCreationInputTokens: 400, CacheReadInputTokens: 9000)], 1));
        string? payload = null;
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => { if (!IsSignals(p)) payload = p; }), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        TurnsIn(payload!).Single().GetProperty("tokenCount").GetInt32().Should().Be(9453);
    }

    [Fact]
    public async Task ConversationTurn_SubagentTranscripts_ShipInSamePayloadAndAdvanceTheirOwnCursors()
    {
        const string transcriptPath = "C:/nowhere/session.jsonl";
        const string subagentPath = "C:/nowhere/session/subagents/agent-a1.jsonl";
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { ArrangeConversationEvent(transcriptPath) }, []);
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult([new CapturedTurn("msg:msg_A", "assistant", "hi", DateTime.UtcNow)], 4));
        _conversationCapture.FindSubagentTranscripts(transcriptPath).Returns([subagentPath]);
        _cursorStore.GetAsync("session-1/agent-a1", Arg.Any<CancellationToken>()).Returns(10);
        _conversationCapture.CaptureFromFileAsync(subagentPath, 10, true, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_S1", "subagent", null, DateTime.UtcNow, OutputTokens: 30, Model: "claude-haiku-5-5")], 17));
        string? payload = null;
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => { if (!IsSignals(p)) payload = p; }), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        TurnsIn(payload!).Select(t => t.GetProperty("role").GetString()).Should().Equal("assistant", "subagent");
        await _cursorStore.Received().SetAsync("session-1", 4, Arg.Any<CancellationToken>());
        await _cursorStore.Received().SetAsync("session-1/agent-a1", 17, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversationTurn_ShipFails_LeavesEveryCursorUnmoved()
    {
        const string transcriptPath = "C:/nowhere/session.jsonl";
        const string subagentPath = "C:/nowhere/session/subagents/agent-a1.jsonl";
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { ArrangeConversationEvent(transcriptPath) }, []);
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult([new CapturedTurn("msg:msg_A", "assistant", "hi", DateTime.UtcNow)], 4));
        _conversationCapture.FindSubagentTranscripts(transcriptPath).Returns([subagentPath]);
        _conversationCapture.CaptureFromFileAsync(subagentPath, Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult([new CapturedTurn("msg:msg_S1", "subagent", null, DateTime.UtcNow)], 17));
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await RunOneTickAsync();

        await _cursorStore.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConversationTurn_SubagentAndMainTurns_ShipInTimestampOrder()
    {
        // The server numbers turns in the order they arrive, and governance reads them in that
        // order — so a subagent's work must sit between the prompt and the parent's final answer.
        const string transcriptPath = "C:/nowhere/session.jsonl";
        const string subagentPath = "C:/nowhere/session/subagents/agent-a1.jsonl";
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { ArrangeConversationEvent(transcriptPath) }, []);
        _conversationCapture.CaptureFromFileAsync(transcriptPath, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("uuid:u-1", "user", "prompt", t0),
                 new CapturedTurn("msg:msg_F", "assistant", "final answer", t0.AddMinutes(10))], 4));
        _conversationCapture.FindSubagentTranscripts(transcriptPath).Returns([subagentPath]);
        _conversationCapture.CaptureFromFileAsync(subagentPath, Arg.Any<int>(), true, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("msg:msg_S1", "subagent", "digging", t0.AddMinutes(2))], 3));
        string? payload = null;
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => { if (!IsSignals(p)) payload = p; }), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        TurnsIn(payload!).Select(t => t.GetProperty("content").GetString())
            .Should().Equal("prompt", "digging", "final answer");
    }
}
