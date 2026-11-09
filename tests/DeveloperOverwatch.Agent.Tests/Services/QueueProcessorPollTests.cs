using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Drives the processor one poll at a time over a real queue and the real ApiClient, so the
/// enrolment cutoff, 413 handling, created_at dequeue order and the unreadable-config warning are tested end to end.</summary>
public sealed class QueueProcessorPollTests : IDisposable
{
    private static readonly DateTime PilotSince = DateTime.UtcNow.AddMinutes(-30);
    private const string Transcript = "C:/transcripts/poll.jsonl";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-poll-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IUsageStore _usage = Substitute.For<IUsageStore>();
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly RecordingHandler _http = new();
    private readonly CapturingLogger _logger = new();

    public QueueProcessorPollTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Pilot_NothingQueuedBeforePilotSinceIsSent_LaterEventsAre_AndOldUsageIsRecordedLocally()
    {
        var before = PilotSince.AddMinutes(-10);
        var after = PilotSince.AddMinutes(10);
        CaptureTurns(("uuid:old", "secret old turn", before));
        await EnqueueAsync(Start("old-start", "old"), before);
        await EnqueueAsync(Event("old-tool", "old", EventType.ToolExecution, ToolExecution("old", "te-old")), before);
        await EnqueueAsync(Event("old-marker", "old", EventType.ConversationTurn, MarkerPayload()), before);
        await EnqueueAsync(Event("old-fail", "old", EventType.ToolFailed, ToolFailed()), before);
        await EnqueueAsync(Start("new-start", "new"), after);
        await EnqueueAsync(Event("new-tool", "new", EventType.ToolExecution, ToolExecution("new", "te-new")), after);
        await EnqueueAsync(Event("new-fail", "new", EventType.ToolFailed, ToolFailed()), after);

        var sut = CreateSut(new AgentConfig { Mode = AgentMode.Pilot, PilotSince = PilotSince, OverwatchDir = _dir });
        await sut.PollOnceAsync(CancellationToken.None);
        await sut.PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().NotContain("te-old").And.NotContain("old-fail").And.NotContain("secret old turn")
            .And.NotContain("\"sessionId\":\"old\"");
        sent.Should().Contain("te-new").And.Contain("new-fail").And.Contain("\"sessionId\":\"new\"");
        await _usage.Received().RecordTurnsAsync("old", Arg.Is<IReadOnlyList<CapturedTurn>>(t => t.Count == 1), Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // A session resumed across enrolment queues its marker after pilotSince, but its transcript still holds
    // turns from before it. Those are Local-era content and must not leave the machine.
    [Fact]
    public async Task Pilot_MarkerAfterPilotSince_SendsOnlyTheTurnsFromAfterIt()
    {
        CaptureTurns(("uuid:a", "pre-enrolment turn", PilotSince.AddMinutes(-5)), ("uuid:b", "post-enrolment turn", PilotSince.AddMinutes(5)));
        await EnqueueAsync(Start("s", "s"), PilotSince.AddMinutes(1));
        await EnqueueAsync(Event("m", "s", EventType.ConversationTurn, MarkerPayload()), PilotSince.AddMinutes(6));

        var sut = CreateSut(new AgentConfig { Mode = AgentMode.Pilot, PilotSince = PilotSince, OverwatchDir = _dir });
        await sut.PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("post-enrolment turn").And.NotContain("pre-enrolment turn");
        await _cursors.Received().SetAsync("s", 2, Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // With a cutoff, a turn that carries no timestamp cannot be shown to postdate enrolment, so it fails closed.
    [Fact]
    public async Task Pilot_TurnWithNoTimestamp_IsNeverSent_ButItsUsageIsRecorded_AndTheCursorMovesPastIt()
    {
        var captured = new List<CapturedTurn>
        {
            new("uuid:undated", "user", "turn without a timestamp", null, StartLine: 0),
            new("uuid:dated", "user", "timestamped turn", PilotSince.AddMinutes(5), StartLine: 1)
        };
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
        await EnqueueAsync(Start("s", "s"), PilotSince.AddMinutes(1));
        await EnqueueAsync(Event("m", "s", EventType.ConversationTurn, MarkerPayload()), PilotSince.AddMinutes(6));

        var sut = CreateSut(new AgentConfig { Mode = AgentMode.Pilot, PilotSince = PilotSince, OverwatchDir = _dir });
        await sut.PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("timestamped turn").And.NotContain("turn without a timestamp");
        await _usage.Received().RecordTurnsAsync("s", Arg.Is<IReadOnlyList<CapturedTurn>>(t => t.Count == 2), Arg.Any<CancellationToken>());
        await _cursors.Received().SetAsync("s", 2, Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Pilot_NoPilotSince_HasNoCutoff()
    {
        await EnqueueAsync(Start("s", "s"), DateTime.UtcNow.AddDays(-2));

        await CreateSut(new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = _dir }).PollOnceAsync(CancellationToken.None);

        _http.Bodies.Should().ContainSingle(b => b.Contains("\"sessionId\":\"s\""));
    }

    [Fact]
    public async Task ApiClient_413_IsTooLarge()
    {
        _http.Respond = (_, _) => HttpStatusCode.RequestEntityTooLarge;

        var result = await Api().SendSessionEventsAsync("s", "{}", CancellationToken.None);

        result.Status.Should().Be(PostStatus.TooLarge);
    }

    [Fact]
    public async Task TooLarge_SingleToolExecution_IsHeld_NeverDead_AndTheSessionsLaterItemsWait()
    {
        _http.Respond = (_, body) => body.Contains("te-big") ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.OK;
        await EnqueueAsync(Start("s", "s"), DateTime.UtcNow.AddMinutes(-3));
        await EnqueueAsync(Event("big", "s", EventType.ToolExecution, ToolExecution("s", "te-big")), DateTime.UtcNow.AddMinutes(-2));
        await EnqueueAsync(Event("ok", "s", EventType.ToolExecution, ToolExecution("s", "te-ok")), DateTime.UtcNow.AddMinutes(-1));

        var sut = CreateSut(new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = _dir });
        (await sut.PollOnceAsync(CancellationToken.None)).Should().Be(QueueProcessor.PollResult.Continue);

        _http.Bodies.Count(b => b.Contains("te-big")).Should().Be(1);
        _http.Bodies.Should().NotContain(b => b.Contains("te-ok"));
        _logger.Warnings.Should().ContainSingle(w => w.Contains("too large"));
        (await _queue.DequeueBatchAsync(10)).Select(e => e.Id).Should().Equal("big", "ok");
    }

    [Fact]
    public async Task TooLarge_TurnChunk_HalvesWithinThePoll_UntilItFits()
    {
        CaptureTurns(Enumerable.Range(0, 8).Select(i => ($"uuid:{i}", $"turn {i}", DateTime.UtcNow.AddMinutes(-10).AddSeconds(i))).ToArray());
        _http.Respond = (_, body) => TurnCount(body) > 2 ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.OK;
        await EnqueueAsync(Event("m", "s", EventType.ConversationTurn, MarkerPayload()), DateTime.UtcNow.AddMinutes(-1));

        await CreateSut(new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = _dir }).PollOnceAsync(CancellationToken.None);

        _http.Bodies.Select(TurnCount).Where(count => count > 0).Should().Equal(8, 4, 2, 2, 4, 2, 2);
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TooLarge_SingleTurn_IsHeld_AndTheCursorStopsBeforeIt()
    {
        CaptureTurns(Enumerable.Range(0, 3).Select(i => ($"uuid:{i}", $"turn {i}", DateTime.UtcNow.AddMinutes(-10).AddSeconds(i))).ToArray());
        _http.Respond = (_, body) => body.Contains("turn 1") ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.OK;
        await EnqueueAsync(Event("m", "s", EventType.ConversationTurn, MarkerPayload()), DateTime.UtcNow.AddMinutes(-1));

        await CreateSut(new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = _dir }).PollOnceAsync(CancellationToken.None);

        _http.Bodies.Select(TurnCount).Where(count => count > 0).Should().Equal(3, 1, 2, 1);
        await _cursors.Received().SetAsync("s", 1, Arg.Any<CancellationToken>());
        await _cursors.DidNotReceive().SetAsync("s", Arg.Is<int>(line => line > 1), Arg.Any<CancellationToken>());
        _logger.Warnings.Should().ContainSingle(w => w.Contains("too large"));
        (await _queue.GetUnshippedCountAsync()).Should().Be(1);
    }

    // Events ship in the order they happened. A start newer than the batch's items is a resume: the
    // items before it belong to the session as it was, and do not wait on it.
    [Fact]
    public async Task Dequeue_OrdersByCreatedAt_SoANewerStartStaysBehindOlderItems()
    {
        await EnqueueAsync(Event("t1", "s", EventType.ToolExecution), DateTime.UtcNow.AddMinutes(-3));
        await EnqueueAsync(Event("t2", "s", EventType.ToolExecution), DateTime.UtcNow.AddMinutes(-2));
        await EnqueueAsync(Event("start", "s", EventType.SessionStart), DateTime.UtcNow.AddMinutes(-1));

        var batch = await _queue.DequeueBatchAsync(2);

        batch.Select(e => e.Id).Should().Equal("t1", "t2");
    }

    [Fact]
    public async Task UnreadableConfig_WarnsOnceAfterTwelvePolls_AndAgainOnlyAfterARecovery()
    {
        var modeSource = Substitute.For<IAgentModeSource>();
        modeSource.CurrentMode().Returns((AgentMode?)null);
        var sut = CreateSut(new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = _dir }, modeSource);

        for (var i = 0; i < QueueProcessor.UnreadableConfigWarningPolls - 1; i++)
            await sut.PollOnceAsync(CancellationToken.None);
        _logger.Warnings.Should().BeEmpty();

        for (var i = 0; i < 10; i++)
            await sut.PollOnceAsync(CancellationToken.None);
        _logger.Warnings.Should().ContainSingle(w => w.Contains("unreadable"));

        modeSource.CurrentMode().Returns(AgentMode.Pilot);
        await sut.PollOnceAsync(CancellationToken.None);
        modeSource.CurrentMode().Returns((AgentMode?)null);
        for (var i = 0; i < QueueProcessor.UnreadableConfigWarningPolls; i++)
            await sut.PollOnceAsync(CancellationToken.None);
        _logger.Warnings.Count(w => w.Contains("unreadable")).Should().Be(2);
    }

    private QueueProcessor CreateSut(AgentConfig config, IAgentModeSource? modeSource = null)
    {
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new QueueProcessor(
            _queue, Api(), redactor, _capture, _cursors, Substitute.For<IGitReader>(), _usage, config,
            modeSource ?? QueueProcessorLifecycleTests.Fixed(config.Mode), Substitute.For<IHostApplicationLifetime>(), _logger);
    }

    private ApiClient Api() =>
        new(new HttpClient(_http) { BaseAddress = new Uri("http://overwatch.invalid/") }, NullLogger<ApiClient>.Instance);

    private void CaptureTurns(params (string SourceId, string Content, DateTime At)[] turns)
    {
        var captured = turns.Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, StartLine: i)).ToList();
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, string sessionId, EventType type, string payload = "{}") =>
        new() { Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload };

    private static QueuedEvent Start(string id, string sessionId) =>
        Event(id, sessionId, EventType.SessionStart,
            JsonSerializer.Serialize(new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow }));

    private static string MarkerPayload() => JsonSerializer.Serialize(new { transcriptPath = Transcript });

    private static string ToolFailed() => $$"""{"toolName":"Bash","at":"{{DateTime.UtcNow:o}}"}""";

    private static string ToolExecution(string sessionId, string itemId) => JsonSerializer.Serialize(new
    {
        sessionId,
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = DateTime.UtcNow } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });

    private static int TurnCount(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("conversationTurns", out var turns) ? turns.GetArrayLength() : 0;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _bodies = new();

        public Func<string, string, HttpStatusCode> Respond { get; set; } = (_, _) => HttpStatusCode.OK;

        public IReadOnlyList<string> Bodies => _bodies.ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            _bodies.Enqueue(body);
            var path = request.RequestUri!.AbsolutePath;
            var status = Respond(path, body);
            var reply = status == HttpStatusCode.OK ? OkReply(path, body) : "{}";
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }

        /// <summary>What the server answers: a start or end names its outcome, and <c>/events</c> lists
        /// every item it was sent as accepted.</summary>
        private static string OkReply(string path, string body)
        {
            using var doc = JsonDocument.Parse(body);
            if (path == "/api/sessions/start")
                return JsonSerializer.Serialize(new { sessionId = doc.RootElement.GetProperty("sessionId").GetString(), outcome = "accepted" });
            if (path.EndsWith("/end", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new { sessionId = path.Split('/')[3], outcome = "accepted" });

            var accepted = new[] { "conversationTurns", "toolExecutions", "fileChanges", "toolFailures" }
                .Where(list => doc.RootElement.TryGetProperty(list, out _))
                .SelectMany(list => doc.RootElement.GetProperty(list).EnumerateArray())
                .Select(item => item.GetProperty("id").GetString())
                .ToList();
            return JsonSerializer.Serialize(new
            {
                eventsIngested = accepted.Count,
                results = new { accepted, duplicate = Array.Empty<string>(), rejected = Array.Empty<object>() }
            });
        }
    }

    private sealed class CapturingLogger : ILogger<QueueProcessor>
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => _warnings.ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                _warnings.Enqueue(formatter(state, exception));
        }
    }
}
