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

/// <summary>The content floor: in Pilot, nothing from before enrolment leaves this machine, nor anything of a
/// session that existed before enrolment, or was open at it, from before the developer resumes it. A
/// session's first start may be a resume or a fork, whose transcript already holds earlier (copied) turns
/// that keep their original timestamps. The positive controls guard against the floor dropping a fresh
/// session's own content.</summary>
public sealed class QueueProcessorContentFloorTests : IDisposable
{
    private const string Transcript = "C:/transcripts/content-floor.jsonl";
    private const string Sentinel = "SUMMARY-SENTINEL";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorContentFloorTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        TrackCursors();
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // A session first seen through a resume existed before it: its turns before the resume stay here.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task NewSession_FirstStartIsResume_WindowTurnsBeforeTheResumeNeverShip(int batchSize)
    {
        CaptureTurns(
            Turn("uuid:window", "window text", _pilotSince.AddMinutes(5)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Marker("m1"), _pilotSince.AddMinutes(6));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m2"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut(batchSize));

        var sent = Sent();
        sent.Should().NotContain("window text", "the session existed before its first start here, a resume");
        sent.Should().Contain("post-resume text");
    }

    // A compact start is the same sitting going on, so it cannot be the start that lets a session in.
    [Fact]
    public async Task NewSession_FirstStartIsCompact_NothingShips_NotEvenALaterToolEvent()
    {
        var sut = CreateSut();
        CaptureTurns(
            Turn("uuid:before", "pre-compact text", _pilotSince.AddMinutes(5)),
            Turn("uuid:after", "post-compact text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("compact", "compact"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));
        await DrainAsync(sut);

        await EnqueueAsync(Tool("tool", "te-late"), _pilotSince.AddMinutes(15));
        await DrainAsync(sut);

        _http.Bodies.Should().BeEmpty("a session whose first start is a compact was already open before it");
    }

    // --resume --fork-session writes a new session id, but the copied lines keep their original timestamps.
    [Fact]
    public async Task ForkedSession_CopiedHistoryAndItsSummary_NeverShip_FreshTurnsDo()
    {
        CaptureTurns(
            Turn("uuid:copy-1", "copied prompt one", _pilotSince.AddMinutes(2)),
            Turn("uuid:copy-2", "copied prompt two", _pilotSince.AddMinutes(3)),
            Turn("uuid:copy-sum", Sentinel, _pilotSince.AddMinutes(4), summary: true),
            Turn("uuid:fresh", "fresh fork text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("fork", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut());

        var sent = Sent();
        sent.Should().NotContain("copied prompt", "the fork's copied history predates its first start");
        sent.Should().NotContain(Sentinel, "the copied summary restates the history it replaced");
        sent.Should().Contain("fresh fork text");
    }

    // A summary with no turn of the session's own before it restates content from before the first start.
    [Fact]
    public async Task ForkedSession_SummaryFirstAndDatedAfterTheStart_ShipsItsIdWithNoText()
    {
        CaptureTurns(
            Turn("uuid:sum", Sentinel, _pilotSince.AddMinutes(11), summary: true),
            Turn("uuid:fresh", "fresh fork text", _pilotSince.AddMinutes(12)));
        await EnqueueAsync(Start("fork", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(13));

        await DrainAsync(CreateSut());

        var sent = Sent();
        sent.Should().NotContain(Sentinel, "no turn of the session's own precedes the summary, so it restates older content");
        sent.Should().Contain(StableTurnId.From("uuid:sum")).And.Contain("fresh fork text");
    }

    // A tool event queued late still carries when it happened; the floor applies to it like to a turn.
    [Fact]
    public async Task ReopenedSession_ToolEventDatedBeforeTheResume_ArrivingLater_NeverShips()
    {
        var sut = CreateSut();
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await DrainAsync(sut);

        await EnqueueAsync(Tool("tool", "te-late"), _pilotSince.AddMinutes(5));
        await DrainAsync(sut);

        Sent().Should().NotContain("te-late", "the tool ran before the resume that let the session in");
    }

    // Guard: a clear start continues the sitting, so it does not let a held session in.
    [Fact]
    public async Task HeldSession_ClearStartAfterEnrolment_NothingShips()
    {
        CaptureTurns(Turn("uuid:after", "post-clear text", _pilotSince.AddMinutes(6)));
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("clear", "clear"), _pilotSince.AddMinutes(5));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(7));
        await EnqueueAsync(Tool("tool", "te-after"), _pilotSince.AddMinutes(8));

        await DrainAsync(CreateSut());

        _http.Bodies.Should().BeEmpty("the session began before enrolment and a clear is not the developer coming back");
    }

    // A session opened in an earlier Pilot period, then used in Local mode, is held again on re-enrolment
    // until the developer resumes it.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task ReEnrolledSession_ItemsAfterReEnrolment_ShipOnlyFromTheResume(int batchSize)
    {
        var period1 = _pilotSince.AddMinutes(-60);
        await EnqueueAsync(Start("start-1", "startup"), period1.AddMinutes(1));
        await DrainAsync(CreateSut(batchSize, period1));
        await EnqueueAsync(Start("start-local", "resume"), period1.AddMinutes(20));
        await DrainAsync(CreateSut(batchSize, period1, AgentMode.Local));

        var sut = CreateSut(batchSize);
        var before = _http.Bodies.Count;
        CaptureTurns(Turn("uuid:between", "between text", _pilotSince.AddMinutes(2)));
        await EnqueueAsync(Tool("tool", "te-between"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Marker("m1"), _pilotSince.AddMinutes(3));
        await DrainAsync(sut);
        _http.Bodies.Skip(before).Should().BeEmpty("the session was used in Local mode and has not been resumed since");

        CaptureTurns(
            Turn("uuid:between", "between text", _pilotSince.AddMinutes(2)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m2"), _pilotSince.AddMinutes(12));
        await DrainAsync(sut);

        var sent = string.Join("\n", _http.Bodies.Skip(before));
        sent.Should().NotContain("between text").And.NotContain("te-between");
        sent.Should().Contain("post-resume text");
    }

    // Positive control: a clear start on a session never seen before is a fresh session, and ships.
    [Fact]
    public async Task NewSession_FirstStartIsClear_ShipsItsTurns()
    {
        CaptureTurns(Turn("uuid:a", "fresh prompt", _pilotSince.AddMinutes(2)));
        await EnqueueAsync(Start("clear", "clear"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(3));

        await DrainAsync(CreateSut());

        Sent().Should().Contain("fresh prompt");
    }

    // Positive control: Claude Code can write the first prompt before the hook stamps the start.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task NewSession_FirstTurnDatedJustBeforeItsStartupStart_StillShips(int batchSize)
    {
        var startAt = _pilotSince.AddMinutes(1);
        CaptureTurns(Turn("uuid:first", "first prompt", startAt.AddMilliseconds(-500)));
        await EnqueueAsync(Start("start", "startup"), startAt);
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(2));

        await DrainAsync(CreateSut(batchSize));

        Sent().Should().Contain("first prompt");
    }

    private string Sent() => string.Join("\n", _http.Bodies);

    private async Task DrainAsync(QueueProcessor sut)
    {
        for (var poll = 0; poll < 10 && await _queue.GetUnshippedCountAsync() > 0; poll++)
            await sut.PollOnceAsync(CancellationToken.None);
    }

    private QueueProcessor CreateSut(int batchSize = 50, DateTime? pilotSince = null, AgentMode mode = AgentMode.Pilot)
    {
        var config = new AgentConfig
        {
            Mode = mode, PilotSince = pilotSince ?? _pilotSince, OverwatchDir = _dir, BatchSize = batchSize
        };
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var api = new ApiClient(new HttpClient(_http) { BaseAddress = new Uri("http://overwatch.invalid/") }, NullLogger<ApiClient>.Instance);
        return new QueueProcessor(
            _queue, api, redactor, _capture, _cursors, Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(), config,
            QueueProcessorLifecycleTests.Fixed(config.Mode), Substitute.For<IHostApplicationLifetime>(),
            NullLogger<QueueProcessor>.Instance);
    }

    private void TrackCursors()
    {
        var cursors = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        _cursors.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => cursors.GetValueOrDefault(ci.ArgAt<string>(0)));
        _cursors.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => cursors[ci.ArgAt<string>(0)] = ci.ArgAt<int>(1));
    }

    private static (string SourceId, string Content, DateTime At, bool Summary) Turn(
        string sourceId, string content, DateTime at, bool summary = false) => (sourceId, content, at, summary);

    private void CaptureTurns(params (string SourceId, string Content, DateTime At, bool Summary)[] turns)
    {
        var captured = turns
            .Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, InputTokens: 10, StartLine: i, IsCompactSummary: t.Summary))
            .ToList();
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Start(string id, string source) => new()
    {
        Id = id, SessionId = "s", EventType = EventType.SessionStart,
        PayloadJson = JsonSerializer.Serialize(new
        {
            sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, source
        })
    };

    private static QueuedEvent Marker(string id) => new()
    {
        Id = id, SessionId = "s", EventType = EventType.ConversationTurn,
        PayloadJson = JsonSerializer.Serialize(new { transcriptPath = Transcript })
    };

    private static QueuedEvent Tool(string id, string itemId) => new()
    {
        Id = id, SessionId = "s", EventType = EventType.ToolExecution,
        PayloadJson = JsonSerializer.Serialize(new
        {
            sessionId = "s",
            toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = DateTime.UtcNow } },
            fileChanges = Array.Empty<object>(),
            conversationTurns = Array.Empty<object>()
        })
    };
}
