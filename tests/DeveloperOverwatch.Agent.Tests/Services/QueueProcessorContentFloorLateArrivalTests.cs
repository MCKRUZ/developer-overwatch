using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>A hook can queue an event after the agent has already opened its session at a later resume. The event
/// happened in the stretch the session was held, so every sender must settle it without sending it, not only the
/// turn and tool senders.</summary>
public sealed class QueueProcessorContentFloorLateArrivalTests : IDisposable
{
    private readonly DateTime _pilotSince;
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-floor-late-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly QueueProcessorPollTests.RecordingHandler _http = new();

    public QueueProcessorContentFloorLateArrivalTests()
    {
        _pilotSince = _clock.Now.UtcDateTime.AddMinutes(-30);
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        _queue.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    public static TheoryData<EventType, string> SignalAndFileEvents => new()
    {
        { EventType.ToolFailed, "late-tool-name" },
        { EventType.ActivityChanged, "waiting" },
        { EventType.FileChange, "C:/repo/late.cs" }
    };

    [Theory]
    [MemberData(nameof(SignalAndFileEvents))]
    public async Task OpenedAtAResume_EventFromTheHeldStretchArrivingLate_IsSettledAndNeverSent(EventType type, string content)
    {
        var sut = await OpenAtResumeAsync();
        var before = _http.Bodies.Count;

        await EnqueueAsync(Late("late", type, _pilotSince.AddMinutes(5)), _pilotSince.AddMinutes(5));
        await DrainAsync(sut);

        string.Join("\n", _http.Bodies.Skip(before)).Should().NotContain(content, "it happened while the session was held");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "an event below the floor is settled, not held");
    }

    // The control: the same event from after the resume ships, so the test above fails for the floor, not the fixture.
    [Theory]
    [MemberData(nameof(SignalAndFileEvents))]
    public async Task OpenedAtAResume_EventFromAfterTheResumeArrivingLate_IsSent(EventType type, string content)
    {
        var sut = await OpenAtResumeAsync();
        var before = _http.Bodies.Count;

        await EnqueueAsync(Late("late", type, _pilotSince.AddMinutes(15)), _pilotSince.AddMinutes(15));
        await DrainAsync(sut);

        string.Join("\n", _http.Bodies.Skip(before)).Should().Contain(content);
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    /// <summary>A startup before enrolment closes the session; a resume at pilot+10 opens it from there.</summary>
    private async Task<QueueProcessor> OpenAtResumeAsync()
    {
        var sut = CreateSut();
        await EnqueueAsync(Start("startup", "startup"), _pilotSince.AddMinutes(-10));
        await EnqueueAsync(Start("resume", "resume"), _pilotSince.AddMinutes(10));
        await DrainAsync(sut);

        (await _queue.GetAccessAsync("s")).Should().Be(new SessionAccess(AccessState.Open, _pilotSince.AddMinutes(10)));
        return sut;
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
        var capture = Substitute.For<IConversationCapture>();
        capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        var api = new ApiClient(new HttpClient(_http) { BaseAddress = new Uri("http://overwatch.invalid/") }, NullLogger<ApiClient>.Instance);
        return new QueueProcessor(
            _queue, api, redactor, capture, Substitute.For<IConversationCursorStore>(), Substitute.For<IGitReader>(),
            Substitute.For<IUsageStore>(), config, QueueProcessorLifecycleTests.Fixed(config.Mode),
            Substitute.For<IHostApplicationLifetime>(), NullLogger<QueueProcessor>.Instance);
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, EventType type, object payload) => new()
    {
        Id = id, SessionId = "s", EventType = type, PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
    };

    private static QueuedEvent Start(string id, string source) => Event(id, EventType.SessionStart, new
    {
        sessionId = "s", developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = DateTime.UtcNow, source
    });

    /// <summary>The event as its hook queues it, dated <paramref name="at"/> in its payload too.</summary>
    private static QueuedEvent Late(string id, EventType type, DateTime at) => type switch
    {
        EventType.ToolFailed => Event(id, type, new { toolName = "late-tool-name", at = at.ToString("o") }),
        EventType.ActivityChanged => Event(id, type, new { state = "waiting", at = at.ToString("o") }),
        EventType.FileChange => Event(id, type, new
        {
            sessionId = "s",
            toolExecutions = Array.Empty<object>(),
            fileChanges = new[] { new { id = "fc-late", filePath = "C:/repo/late.cs", changeType = "Modified", timestamp = at } },
            conversationTurns = Array.Empty<object>()
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
