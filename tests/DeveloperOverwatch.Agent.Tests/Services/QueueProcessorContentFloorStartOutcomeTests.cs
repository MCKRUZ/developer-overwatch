using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>What the server answers to the start that would open a session decides the session: one it does not
/// know closes the session here, so nothing after it leaves; a redelivery it already holds opens it like any
/// accepted start.</summary>
public sealed class QueueProcessorContentFloorStartOutcomeTests : IDisposable
{
    private const string Transcript = "C:/transcripts/content-floor-outcome.jsonl";
    private const string StartPath = "/api/sessions/start";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-outcome-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorContentFloorStartOutcomeTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "queue.db");
        _queue = new EventQueue(_dbPath, _clock);
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
    public async Task NewSession_OpeningStartAnsweredSessionNotFound_ClosesTheSession_AndNothingLaterIsSent()
    {
        _http.Reply = (path, _) => path == StartPath
            ? (HttpStatusCode.NotFound, """{"code":"session_not_found"}""")
            : null;
        var sut = CreateSut();
        await EnqueueAsync(Start("start"), _pilotSince.AddMinutes(1));
        await DrainAsync(sut);

        (await _queue.GetAccessAsync("s")).Should().Be(new SessionAccess(AccessState.Closed, null));
        ShippedState("start").Should().Be(DeadState, "the server refused it, so it is never sent again");

        CaptureTurns(("uuid:after", "post-start text", _pilotSince.AddMinutes(3)));
        await EnqueueAsync(Tool("tool", "te-after"), _pilotSince.AddMinutes(2));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(4));
        await DrainAsync(sut);

        _http.Bodies.Should().ContainSingle("only the refused start was ever sent")
            .Which.Should().Contain("\"eventId\":\"start\"");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "the closed session's items are settled, not held");
    }

    // A start the server already holds is a redelivery of one it accepted, so it opens the session like one.
    [Fact]
    public async Task NewSession_OpeningStartAnsweredDuplicate_OpensTheSession_AndLaterItemsShip()
    {
        _http.Reply = (path, _) => path == StartPath
            ? (HttpStatusCode.OK, JsonSerializer.Serialize(new { sessionId = "s", outcome = "duplicate" }))
            : null;
        var sut = CreateSut();
        await EnqueueAsync(Start("start"), _pilotSince.AddMinutes(1));
        await DrainAsync(sut);

        (await _queue.GetAccessAsync("s")).Should().Be(new SessionAccess(AccessState.Open, _pilotSince));

        CaptureTurns(("uuid:after", "post-start text", _pilotSince.AddMinutes(3)));
        await EnqueueAsync(Tool("tool", "te-after"), _pilotSince.AddMinutes(2));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(4));
        await DrainAsync(sut);

        string.Join("\n", _http.Bodies).Should().Contain("te-after").And.Contain("post-start text");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    private const long DeadState = 2;

    private long ShippedState(string id)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT shipped FROM events WHERE id = $id";
        select.Parameters.AddWithValue("$id", id);
        return (long)select.ExecuteScalar()!;
    }

    private async Task DrainAsync(QueueProcessor sut)
    {
        for (var poll = 0; poll < 10 && await _queue.GetUnshippedCountAsync() > 0; poll++)
            await sut.PollOnceAsync(CancellationToken.None);
    }

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = _pilotSince, OverwatchDir = _dir };
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

    private static QueuedEvent Start(string id) => Event(id, EventType.SessionStart, new
    {
        sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, source = "startup"
    });

    private static QueuedEvent Marker(string id) => Event(id, EventType.ConversationTurn, new { transcriptPath = Transcript });

    private static QueuedEvent Tool(string id, string itemId) => Event(id, EventType.ToolExecution, new
    {
        sessionId = "s",
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = DateTime.UtcNow } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });
}
