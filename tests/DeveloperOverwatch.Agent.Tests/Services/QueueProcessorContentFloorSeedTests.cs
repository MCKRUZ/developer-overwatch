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

/// <summary>An install upgraded from a queue.db that predates the content floor still holds the floor for a
/// session it already shipped: the session opened at its earliest shipped row, so its turns from before that
/// stay here.</summary>
public sealed class QueueProcessorContentFloorSeedTests : IDisposable
{
    private const string Transcript = "C:/transcripts/content-floor-seed.jsonl";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-seed-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();
    private EventQueue? _queue;

    public QueueProcessorContentFloorSeedTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        _dbPath = Path.Combine(_dir, "queue.db");
        Directory.CreateDirectory(_dir);
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        var cursors = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        _cursors.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => cursors.GetValueOrDefault(ci.ArgAt<string>(0)));
        _cursors.SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(ci => cursors[ci.ArgAt<string>(0)] = ci.ArgAt<int>(1));
    }

    public void Dispose()
    {
        _queue?.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task UpgradedInstall_SessionShippedBeforeTheUpgrade_TurnsBeforeItsFirstShippedRowNeverShip()
    {
        var shippedAt = _pilotSince.AddMinutes(10);
        WriteOldSchemaWithShippedStart(shippedAt);
        _queue = new EventQueue(_dbPath, _clock);
        await _queue.InitializeAsync();

        CaptureTurns(
            ("uuid:before", "text before the shipped start", _pilotSince.AddMinutes(5)),
            ("uuid:after", "text after the shipped start", _pilotSince.AddMinutes(11)));
        await _queue.EnqueueAsync(new QueuedEvent
        {
            Id = "marker", SessionId = "s", EventType = EventType.ConversationTurn,
            PayloadJson = JsonSerializer.Serialize(new { transcriptPath = Transcript }), CreatedAt = _pilotSince.AddMinutes(12)
        });

        var sut = CreateSut(_queue);
        for (var poll = 0; poll < 10 && await _queue.GetUnshippedCountAsync() > 0; poll++)
            await sut.PollOnceAsync(CancellationToken.None);

        var sent = string.Join("\n", _http.Bodies);
        sent.Should().NotContain("text before the shipped start", "the session opened at its earliest shipped row");
        sent.Should().Contain("text after the shipped start");
    }

    /// <summary>The queue as an install from before the floor left it: the events table alone.</summary>
    private void WriteOldSchemaWithShippedStart(DateTime createdAt)
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE events (
                    rowid INTEGER PRIMARY KEY,
                    id TEXT UNIQUE NOT NULL,
                    session_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    shipped INTEGER NOT NULL DEFAULT 0
                );
                INSERT INTO events (id, session_id, event_type, payload_json, created_at, retry_count, shipped)
                VALUES ('start', 's', 'SessionStart', $payload, $createdAt, 0, 1);
                """;
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(new
            {
                sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = createdAt, source = "startup"
            }));
            cmd.Parameters.AddWithValue("$createdAt", DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).ToString("o"));
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    private void CaptureTurns(params (string SourceId, string Content, DateTime At)[] turns)
    {
        var captured = turns.Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, InputTokens: 10, StartLine: i)).ToList();
        _capture.CaptureFromFileAsync(Transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
    }

    private QueueProcessor CreateSut(EventQueue queue)
    {
        var config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = _pilotSince, OverwatchDir = _dir };
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var api = new ApiClient(new HttpClient(_http) { BaseAddress = new Uri("http://overwatch.invalid/") }, NullLogger<ApiClient>.Instance);
        return new QueueProcessor(
            queue, api, redactor, _capture, _cursors, Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(), config,
            QueueProcessorLifecycleTests.Fixed(config.Mode), Substitute.For<IHostApplicationLifetime>(),
            NullLogger<QueueProcessor>.Instance);
    }
}
