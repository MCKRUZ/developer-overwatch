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

/// <summary>A session open at enrolment is captured only from its next new-session or resume start. A
/// compaction summary is dated at compaction but restates the prompts before it, and compaction fires a
/// SessionStart; neither may carry such a session's earlier text off this machine.</summary>
public sealed class QueueProcessorCompactionTests : IDisposable
{
    private const string Transcript = "C:/transcripts/compaction.jsonl";
    private const string Sentinel = "SUMMARY-SENTINEL";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-compact-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorCompactionTests()
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

    [Fact]
    public async Task LocalSession_CompactedAfterEnrolment_SendsNothing_AndStaysLocal()
    {
        var sut = CreateSut();
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(sut);

        CaptureTurns(
            Turn("uuid:pre", "pre-enrolment prompt", _pilotSince.AddMinutes(-5)),
            Turn("uuid:sum", Sentinel, _pilotSince.AddMinutes(5), summary: true),
            Turn("uuid:post", "post-compaction text", _pilotSince.AddMinutes(6)));
        await EnqueueAsync(Start("compact", "compact"), _pilotSince.AddMinutes(5));
        await EnqueueAsync(Marker("marker"), _pilotSince.AddMinutes(8));
        await DrainAsync(sut);

        string.Join("\n", _http.Bodies).Should().NotContain(Sentinel);
        _http.Bodies.Should().BeEmpty();
        (await _queue.GetStartStateAsync("s")).Should().Be(StartState.Local);
    }

    [Fact]
    public async Task ResumedSession_SummaryOfPreEnrolmentPrompts_SendsItsUsage_AndNoText()
    {
        var sut = CreateSut();
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(sut);

        CaptureTurns(
            Turn("uuid:pre", "pre-enrolment prompt", _pilotSince.AddMinutes(-5)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)),
            Turn("uuid:sum", Sentinel, _pilotSince.AddMinutes(13), summary: true));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Start("compact", "compact"), _pilotSince.AddMinutes(12));
        await EnqueueAsync(Marker("marker"), _pilotSince.AddMinutes(14));
        await DrainAsync(sut);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain(StableTurnId.From("uuid:sum")).And.NotContain(Sentinel);
        sent.Should().Contain("post-resume text");
    }

    // Guards against over-blocking: a session wholly from after enrolment ships its summary as text.
    [Fact]
    public async Task PostEnrolmentSession_Compacted_ShipsTheSummaryText()
    {
        CaptureTurns(
            Turn("uuid:a", "fresh prompt", _pilotSince.AddMinutes(2)),
            Turn("uuid:sum", Sentinel, _pilotSince.AddMinutes(4), summary: true));
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Start("compact", "compact"), _pilotSince.AddMinutes(3));
        await EnqueueAsync(Marker("marker"), _pilotSince.AddMinutes(5));

        await DrainAsync(CreateSut());

        string.Join("\n", _http.Bodies).Should().Contain(Sentinel).And.Contain("fresh prompt");
    }

    // A compact start is not the next start a Local marker is bounded by: only the resume is.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task LocalSession_CompactThenResume_TurnsBeforeTheResumeAreNeverSent(int batchSize)
    {
        var sut = CreateSut(batchSize);
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(sut);

        CaptureTurns(
            Turn("uuid:between", "between text", _pilotSince.AddMinutes(5)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Marker("m1"), _pilotSince.AddMinutes(2));
        await EnqueueAsync(Start("compact", "compact"), _pilotSince.AddMinutes(4));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m2"), _pilotSince.AddMinutes(12));
        await DrainAsync(sut);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("post-resume text").And.NotContain("between text");
    }

    // A marker queued before enrolment is handled locally, and is bounded by the resume, not by enrolment.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task PreEnrolmentMarker_ThenResume_TurnsBeforeTheResumeAreNeverSent(int batchSize)
    {
        CaptureTurns(
            Turn("uuid:pre", "pre-enrolment prompt", _pilotSince.AddMinutes(-8)),
            Turn("uuid:between", "between text", _pilotSince.AddMinutes(5)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Marker("m0"), _pilotSince.AddMinutes(-5));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m2"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut(batchSize));

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("post-resume text").And.NotContain("between text").And.NotContain("pre-enrolment prompt");
    }

    // No marker runs between enrolment and the resume, so nothing moved the cursor past the turns before it.
    [Theory]
    [InlineData(50)]
    [InlineData(1)]
    public async Task LocalSession_NoMarkerBeforeTheResume_TurnsBeforeTheResumeAreNeverSent(int batchSize)
    {
        var sut = CreateSut(batchSize);
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(sut);

        CaptureTurns(
            Turn("uuid:pre", "pre-enrolment prompt", _pilotSince.AddMinutes(-5)),
            Turn("uuid:between", "between text", _pilotSince.AddMinutes(5)),
            Turn("uuid:after", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));
        await DrainAsync(sut);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().Contain("post-resume text").And.NotContain("between text").And.NotContain("pre-enrolment prompt");
    }

    // Guards against over-blocking: only a session that was Local has a reopen floor.
    [Fact]
    public async Task PostEnrolmentSession_Resumed_ShipsItsTurnsFromBeforeTheResume()
    {
        CaptureTurns(
            Turn("uuid:a", "before resume text", _pilotSince.AddMinutes(2)),
            Turn("uuid:b", "post-resume text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut());

        string.Join("\n", _http.Bodies).Should().Contain("before resume text").And.Contain("post-resume text");
    }

    [Theory]
    [InlineData("""{"sessionId":"s"}""", true)]
    [InlineData("""{"sessionId":"s","source":null}""", true)]
    [InlineData("""{"source":"startup"}""", true)]
    [InlineData("""{"source":"Resume"}""", true)]
    [InlineData("""{"source":"clear"}""", false)]
    [InlineData("""{"source":"compact"}""", false)]
    [InlineData("""{"source":"something-new"}""", false)]
    [InlineData("""{"source":"unknown"}""", false)]
    [InlineData("""{"source":""}""", false)]
    [InlineData("""{"source":7}""", false)]
    [InlineData("not json", false)]
    [InlineData("""["startup"]""", false)]
    public void MayReopen_AllowsOnlyANewSessionOrAResume(string payload, bool expected) =>
        SessionStartPayload.MayReopen(payload).Should().Be(expected);

    private async Task DrainAsync(QueueProcessor sut)
    {
        for (var poll = 0; poll < 10 && await _queue.GetUnshippedCountAsync() > 0; poll++)
            await sut.PollOnceAsync(CancellationToken.None);
    }

    private QueueProcessor CreateSut(int batchSize = 50)
    {
        var config = new AgentConfig
        {
            Mode = AgentMode.Pilot, PilotSince = _pilotSince, OverwatchDir = _dir, BatchSize = batchSize
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
}
