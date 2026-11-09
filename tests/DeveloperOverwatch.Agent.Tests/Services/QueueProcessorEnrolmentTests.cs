using System.Collections.Concurrent;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The enrolment cutoff for a whole session: one that began before enrolment stays on this
/// machine, across the shipped-row purge, until a start from after enrolment opts it in.</summary>
public sealed class QueueProcessorEnrolmentTests : IDisposable
{
    private const string Transcript = "C:/transcripts/enrolment.jsonl";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-enrol-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IUsageStore _usage = Substitute.For<IUsageStore>();
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorEnrolmentTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // The invariant: once the shipped rows are purged, the queue must still know the session began before
    // enrolment, or it reads as Unknown and its content is POSTed.
    [Fact]
    public async Task PreEnrolmentSession_AfterTheShippedPurge_ItsLaterItemsAreNeverSent()
    {
        var cursors = TrackCursors();
        var sut = CreateSut();
        await EnqueueAsync(Start("start", "s"), _pilotSince.AddMinutes(-10));
        await sut.PollOnceAsync(CancellationToken.None);

        _clock.Now = _clock.Now.AddHours(25);
        await sut.PollOnceAsync(CancellationToken.None);

        var later = _clock.Now.UtcDateTime;
        CaptureTurns(("uuid:late", "post-enrolment text of a pre-enrolment session", later.AddMinutes(-2)));
        await EnqueueAsync(Event("tool", "s", EventType.ToolExecution, ToolExecution("s", "te-late")), later.AddMinutes(-4));
        await EnqueueAsync(Event("fail", "s", EventType.ToolFailed, ToolFailed()), later.AddMinutes(-3));
        await EnqueueAsync(Event("marker", "s", EventType.ConversationTurn, Marker()), later.AddMinutes(-1));
        await EnqueueAsync(Event("end", "s", EventType.SessionEnd, """{"sessionId":"s"}"""), later);
        await sut.PollOnceAsync(CancellationToken.None);
        await sut.PollOnceAsync(CancellationToken.None);

        _http.Bodies.Should().BeEmpty();
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
        await _usage.Received().RecordTurnsAsync("s", Arg.Is<IReadOnlyList<CapturedTurn>>(t => t.Count == 1), Arg.Any<CancellationToken>());
        cursors["s"].Should().Be(1, "turns of a session held here are never sent, so a later resume must not send them either");
    }

    // A start from after enrolment is a fresh opt-in: the session is captured from its next start or /resume.
    // Items between enrolment and that start stay here. The reopening outlives the purge of the shipped start.
    [Fact]
    public async Task PreEnrolmentSession_ResumedAfterEnrolment_ShipsFromTheResume_AndStaysOpenAfterThePurge()
    {
        var sut = CreateSut();
        await EnqueueAsync(Start("start", "s"), _pilotSince.AddMinutes(-10));
        await sut.PollOnceAsync(CancellationToken.None);

        await EnqueueAsync(Event("between", "s", EventType.ToolExecution, ToolExecution("s", "te-between")), _pilotSince.AddMinutes(5));
        await sut.PollOnceAsync(CancellationToken.None);
        _http.Bodies.Should().BeEmpty("the session began before enrolment and has not been resumed since");

        await EnqueueAsync(Start("resume", "s"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Event("after", "s", EventType.ToolExecution, ToolExecution("s", "te-after")), _pilotSince.AddMinutes(11));
        await sut.PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("\"sessionId\":\"s\"").And.Contain("te-after").And.NotContain("te-between");

        await _queue.PurgeShippedAsync(TimeSpan.Zero);
        await EnqueueAsync(Event("later", "s", EventType.ToolExecution, ToolExecution("s", "te-later")), _pilotSince.AddMinutes(20));
        await sut.PollOnceAsync(CancellationToken.None);

        string.Join("\n", _http.Bodies).Should().Contain("te-later");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // Backfill is the explicit opt-in for Local-era usage, so its marker is exempt from the turn cutoff.
    [Fact]
    public async Task BackfillMarker_PreEnrolmentTurn_ShipsItsUsage_AndNoText()
    {
        CaptureTurns(("uuid:old", "pre-enrolment text", _pilotSince.AddMinutes(-5)));
        await EnqueueAsync(Start("start", "s"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Event("marker", "s", EventType.ConversationTurn, BackfillMarker(usageOnly: true)), _pilotSince.AddMinutes(2));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain(StableTurnId.From("uuid:old")).And.Contain("\"inputTokens\":10").And.NotContain("pre-enrolment text");
    }

    // The exemption is keyed on the backfill flag, not on what a turn happens to carry.
    [Fact]
    public async Task LiveMarker_PreEnrolmentTurnWithNoContent_IsNotSent()
    {
        CaptureTurns(("uuid:quiet", null, _pilotSince.AddMinutes(-5)));
        await EnqueueAsync(Start("start", "s"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Event("marker", "s", EventType.ConversationTurn, Marker()), _pilotSince.AddMinutes(2));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        string.Join("\n", _http.Bodies).Should().NotContain(StableTurnId.From("uuid:quiet"));
    }

    // Usage only holds by construction: a backfill marker strips text at send time even without usageOnly.
    [Fact]
    public async Task BackfillMarker_TurnWithText_SendsNoText_AndLeavesTheLiveCursorAlone()
    {
        CaptureTurns(("uuid:new", "backfilled secret text", _pilotSince.AddMinutes(5)));
        await EnqueueAsync(Start("start", "s"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Event("marker", "s", EventType.ConversationTurn, BackfillMarker(usageOnly: false)), _pilotSince.AddMinutes(6));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain(StableTurnId.From("uuid:new")).And.NotContain("backfilled secret text");
        await _cursors.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = _pilotSince, OverwatchDir = _dir };
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var api = new ApiClient(new HttpClient(_http) { BaseAddress = new Uri("http://overwatch.invalid/") }, NullLogger<ApiClient>.Instance);
        return new QueueProcessor(
            _queue, api, redactor, _capture, _cursors, Substitute.For<IGitReader>(), _usage, config,
            QueueProcessorLifecycleTests.Fixed(config.Mode), Substitute.For<IHostApplicationLifetime>(),
            NullLogger<QueueProcessor>.Instance);
    }

    private ConcurrentDictionary<string, int> TrackCursors()
    {
        var cursors = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        _cursors.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => cursors.GetValueOrDefault(ci.ArgAt<string>(0)));
        _cursors.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => cursors[ci.ArgAt<string>(0)] = ci.ArgAt<int>(1));
        return cursors;
    }

    private void CaptureTurns(params (string SourceId, string? Content, DateTime At)[] turns)
    {
        var captured = turns.Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, InputTokens: 10, StartLine: i)).ToList();
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, string sessionId, EventType type, string payload) =>
        new() { Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload };

    private static QueuedEvent Start(string id, string sessionId) =>
        Event(id, sessionId, EventType.SessionStart,
            JsonSerializer.Serialize(new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow }));

    private static string Marker() => JsonSerializer.Serialize(new { transcriptPath = Transcript });

    private string BackfillMarker(bool usageOnly) => usageOnly
        ? JsonSerializer.Serialize(new { transcriptPath = Transcript, usageOnly, isBackfill = true, from = _pilotSince.AddDays(-1) })
        : JsonSerializer.Serialize(new { transcriptPath = Transcript, isBackfill = true, from = _pilotSince.AddDays(-1) });

    private static string ToolFailed() => $$"""{"toolName":"Bash","at":"{{DateTime.UtcNow:o}}"}""";

    private static string ToolExecution(string sessionId, string itemId) => JsonSerializer.Serialize(new
    {
        sessionId,
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = DateTime.UtcNow } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });
}
