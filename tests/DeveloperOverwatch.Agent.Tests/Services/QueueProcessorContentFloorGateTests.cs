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

/// <summary>What may let a session in: backfill never does, and only the developer coming back to a session
/// (a startup, resume or fork) opens one this machine has already decided on. A period of Local mode is a
/// decision too, whether or not a start was queued in it.</summary>
public sealed class QueueProcessorContentFloorGateTests : IDisposable
{
    private const string Transcript = "C:/transcripts/content-floor-gate.jsonl";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-gate-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorContentFloorGateTests()
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

    // Backfill is usage the developer chose to send; it is not the developer coming back to the session.
    [Fact]
    public async Task ClosedSession_Backfill_SendsItsUsage_ButLetsNoLaterLiveTextIn()
    {
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(CreateSut(_pilotSince));

        await BackfillThenLiveMarkerAsync();

        AssertBackfillSentAndNoLiveText();
        (await _queue.GetAccessAsync("s")).State.Should().Be(AccessState.Closed, "backfill must not open the session");
    }

    [Fact]
    public async Task NewSession_Backfill_SendsItsUsage_ButLetsNoLaterLiveTextIn()
    {
        await BackfillThenLiveMarkerAsync();

        AssertBackfillSentAndNoLiveText();
        (await _queue.GetAccessAsync("s")).State.Should().Be(AccessState.New, "backfill decides nothing about the session");
    }

    // An old hook queues starts with no source; one of them must not let a held session back in.
    [Fact]
    public async Task ClosedSession_StartWithNoSource_LetsNothingIn()
    {
        await EnqueueAsync(Start("start", "startup"), _pilotSince.AddMinutes(-10));
        await DrainAsync(CreateSut(_pilotSince));

        CaptureTurns(Turn("uuid:after", "post-start text", _pilotSince.AddMinutes(6)));
        await EnqueueAsync(Start("no-source", null), _pilotSince.AddMinutes(5));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(7));
        await EnqueueAsync(Tool("tool", "te-after"), _pilotSince.AddMinutes(8));
        await DrainAsync(CreateSut(_pilotSince));

        _http.Bodies.Should().BeEmpty("a start with no source does not show the developer came back to the session");
        (await _queue.GetAccessAsync("s")).State.Should().Be(AccessState.Closed);
    }

    private async Task BackfillThenLiveMarkerAsync()
    {
        var backfillAt = _pilotSince.AddMinutes(5);
        CaptureTurns(
            Turn("uuid:old", "old text", _pilotSince.AddMinutes(-20)),
            Turn("uuid:live", "live text", _pilotSince.AddMinutes(11)));
        await EnqueueAsync(BackfillStart("bf-start"), backfillAt);
        await EnqueueAsync(BackfillMarker("bf-marker"), backfillAt.AddTicks(1));
        await EnqueueAsync(BackfillEnd("bf-end"), backfillAt.AddTicks(2));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut(_pilotSince));
    }

    private void AssertBackfillSentAndNoLiveText()
    {
        var sent = Sent();
        sent.Should().NotContain("live text", "no start the developer made has let the session in");
        sent.Should().NotContain("old text", "backfill never carries text");
        sent.Should().Contain("\"eventId\":\"bf-start\"").And.Contain("\"eventId\":\"bf-end\"",
            "the backfilled session must still complete on the server");
        sent.Should().Contain(StableTurnId.From("uuid:old"), "the backfill's usage ships");
    }

    private string Sent() => string.Join("\n", _http.Bodies);

    private async Task DrainAsync(QueueProcessor sut)
    {
        for (var poll = 0; poll < 10 && await _queue.GetUnshippedCountAsync() > 0; poll++)
            await sut.PollOnceAsync(CancellationToken.None);
    }

    /// <param name="pilotSince">Null is a Pilot install with no enrolment cutoff.</param>
    private QueueProcessor CreateSut(DateTime? pilotSince, AgentMode mode = AgentMode.Pilot, int batchSize = 50)
    {
        var config = new AgentConfig { Mode = mode, PilotSince = pilotSince, OverwatchDir = _dir, BatchSize = batchSize };
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

    private static (string SourceId, string Content, DateTime At) Turn(string sourceId, string content, DateTime at) =>
        (sourceId, content, at);

    private void CaptureTurns(params (string SourceId, string Content, DateTime At)[] turns)
    {
        var captured = turns
            .Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, InputTokens: 10, StartLine: i))
            .ToList();
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, EventType type, object payload) => new()
    {
        Id = id, SessionId = "s", EventType = type, PayloadJson = JsonSerializer.Serialize(payload)
    };

    private static QueuedEvent Start(string id, string? source) => Event(id, EventType.SessionStart, source is null
        ? new { sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow }
        : new { sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, source });

    private static QueuedEvent Marker(string id) => Event(id, EventType.ConversationTurn, new { transcriptPath = Transcript });

    private static QueuedEvent Tool(string id, string itemId) => Event(id, EventType.ToolExecution, new
    {
        sessionId = "s",
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = DateTime.UtcNow } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });

    // The events BackfillRun queues.
    private static QueuedEvent BackfillStart(string id) => Event(id, EventType.SessionStart, new
    {
        sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, isBackfill = true
    });

    private QueuedEvent BackfillMarker(string id) => Event(id, EventType.ConversationTurn, new
    {
        sessionId = "s", transcriptPath = Transcript, at = _pilotSince, usageOnly = true, isBackfill = true,
        from = _pilotSince.AddDays(-1)
    });

    private static QueuedEvent BackfillEnd(string id) => Event(id, EventType.SessionEnd, new
    {
        sessionId = "s", endedAt = DateTime.UtcNow, isBackfill = true
    });
}
