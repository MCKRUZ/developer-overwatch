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

/// <summary>A session open at enrolment is captured only from its reopening start, and so is its title: a
/// <c>/rename</c> done after enrolment but before the resume belongs to the stretch that stays here.</summary>
public sealed class QueueProcessorResumeTitleTests : IDisposable
{
    private const string Title = "RENAMED-TITLE";

    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-resume-title-{Guid.NewGuid():N}");
    private readonly string _transcript;
    private readonly EventQueue _queue;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorResumeTitleTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        _transcript = Path.Combine(_dir, "s.jsonl");
        Directory.CreateDirectory(Path.Combine(_dir, "s"));
        File.WriteAllText(_transcript, "{}");
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _capture.CaptureFromFileAsync(_transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(
                [new CapturedTurn("uuid:after", "user", "post-resume text", _pilotSince.AddMinutes(11), StartLine: 0)], 1));
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

    [Theory]
    [InlineData(5, false)]
    [InlineData(10.5, true)]
    public async Task LocalSession_Resumed_SendsTheTitleOnlyWhenWrittenAfterTheResume(double writtenMinutes, bool sent)
    {
        var writtenAt = _pilotSince.AddMinutes(writtenMinutes);
        WriteTitle(writtenAt);
        await EnqueueAsync(Start("start", "startup", null), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("resume", "resume", writtenAt), _pilotSince.AddMinutes(10));
        await EnqueueAsync(Marker("m"), _pilotSince.AddMinutes(12));

        await DrainAsync(CreateSut());

        var startBody = _http.Bodies.Should().ContainSingle(b => b.Contains("\"eventId\":\"resume\"")).Subject;
        var markerBody = _http.Bodies.Should().ContainSingle(b => b.Contains("post-resume text")).Subject;
        startBody.Contains(Title).Should().Be(sent);
        markerBody.Contains(Title).Should().Be(sent);
    }

    private void WriteTitle(DateTime writtenAt)
    {
        var path = Path.Combine(_dir, "s", "custom-title.json");
        File.WriteAllText(path, $$"""{"customTitle":"{{Title}}"}""");
        File.SetLastWriteTimeUtc(path, writtenAt);
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
