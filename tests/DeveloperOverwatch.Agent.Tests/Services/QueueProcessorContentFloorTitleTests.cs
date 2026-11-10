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

/// <summary>The content floor applies to a session's title as to its turns: a <c>/rename</c> done while the
/// session was held here names the held stretch, so no start or marker sent later may carry it, whichever
/// start sends it and whether or not the shipped rows were purged in between.</summary>
public sealed class QueueProcessorContentFloorTitleTests : IDisposable
{
    private const string Title = "HELD-TITLE";
    private const string Sentinel = "SUMMARY-SENTINEL";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-title-{Guid.NewGuid():N}");
    private readonly string _transcript;
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorContentFloorTitleTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        _transcript = Path.Combine(_dir, "s.jsonl");
        Directory.CreateDirectory(Path.Combine(_dir, "s"));
        File.WriteAllText(_transcript, "{}");
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        _queue.InitializeAsync().GetAwaiter().GetResult();
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
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // The resume drops the held title; the compact after it is a later start of the same opened session.
    [Fact]
    public async Task HeldSession_RenamedBeforeTheResume_NoLaterStartOrMarkerCarriesTheTitle()
    {
        var writtenAt = _pilotSince.AddMinutes(5);
        WriteTitle(writtenAt);
        CaptureTurns(("uuid:after", "post-resume text", _pilotSince.AddMinutes(11), false));
        await EnqueueAsync(Start("start", "startup", null), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("resume", "resume", writtenAt), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Start("compact", "compact", writtenAt), _pilotSince.AddMinutes(20));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(21));

        await DrainAsync(CreateSut());

        Sent().Should().NotContain(Title, "the title was written while the session was held, before the resume");
        Sent().Should().Contain("post-resume text");
    }

    // The floor must outlive the purge of the shipped resume start.
    [Fact]
    public async Task HeldSession_RenamedBeforeTheResume_AfterTheShippedPurge_NoLaterStartCarriesTheTitle()
    {
        var sut = CreateSut();
        var writtenAt = _pilotSince.AddMinutes(5);
        WriteTitle(writtenAt);
        CaptureTurns(("uuid:after", "post-resume text", _pilotSince.AddMinutes(11), false));
        await EnqueueAsync(Start("start", "startup", null), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("resume", "resume", writtenAt), _pilotSince.AddMinutes(10));
        await DrainAsync(sut);

        await _queue.PurgeShippedAsync(TimeSpan.Zero);
        await EnqueueAsync(Start("compact", "compact", writtenAt), _pilotSince.AddMinutes(20));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(21));
        await DrainAsync(sut);

        Sent().Should().NotContain(Title, "the purge removed the shipped resume, not the session's floor");
        Sent().Should().Contain("post-resume text");
    }

    // Positive control: a session wholly from after enrolment ships its turns, its summary text and its title.
    [Fact]
    public async Task NewSession_Startup_ShipsTurnsSummaryTextAndTitle()
    {
        WriteTitle(_pilotSince.AddMinutes(3));
        CaptureTurns(
            ("uuid:a", "fresh prompt", _pilotSince.AddMinutes(2), false),
            ("uuid:sum", Sentinel, _pilotSince.AddMinutes(4), true));
        await EnqueueAsync(Start("start", "startup", null), _pilotSince.AddMinutes(1));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(5));

        await DrainAsync(CreateSut());

        Sent().Should().Contain("fresh prompt").And.Contain(Sentinel).And.Contain(Title);
    }

    private string Sent() => string.Join("\n", _http.Bodies);

    private void WriteTitle(DateTime writtenAt)
    {
        var path = Path.Combine(_dir, "s", "custom-title.json");
        File.WriteAllText(path, $$"""{"customTitle":"{{Title}}"}""");
        File.SetLastWriteTimeUtc(path, writtenAt);
    }

    private void CaptureTurns(params (string SourceId, string Content, DateTime At, bool Summary)[] turns)
    {
        var captured = turns
            .Select((t, i) => new CapturedTurn(t.SourceId, "user", t.Content, t.At, InputTokens: 10, StartLine: i, IsCompactSummary: t.Summary))
            .ToList();
        _capture.CaptureFromFileAsync(_transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(captured.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), captured.Count));
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

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Start(string id, string source, DateTime? titleWrittenAt) => new()
    {
        Id = id, SessionId = "s", EventType = EventType.SessionStart,
        PayloadJson = JsonSerializer.Serialize(new
        {
            sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, source,
            title = titleWrittenAt is null ? null : Title, titleWrittenAt = titleWrittenAt?.ToString("o")
        })
    };

    private QueuedEvent Marker(string id) => new()
    {
        Id = id, SessionId = "s", EventType = EventType.ConversationTurn,
        PayloadJson = JsonSerializer.Serialize(new { transcriptPath = _transcript })
    };
}
