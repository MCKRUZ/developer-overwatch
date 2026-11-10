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

/// <summary>How one session's queued events reach the server, one poll at a time over a real queue: what is
/// sent, in what order, and what is left pending when the server does not settle an item.</summary>
public sealed class SessionShipperTests : IDisposable
{
    private const string Session = "s";
    private static readonly DateTime T0 = DateTime.UtcNow.AddHours(-1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-shipper-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ConcurrentQueue<string> _sent = new();
    private Func<int, ApiResult> _onStart = _ => ApiResult.Success;
    private Func<int, ApiResult> _onEnd = _ => ApiResult.Success;
    private Func<string, ApiResult> _onEvents = _ => ApiResult.Success;

    public SessionShipperTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        _queue.InitializeAsync().GetAwaiter().GetResult();
        // The session is on the server already, from a start shipped and purged before these tests' events.
        _queue.OpenAsync(Session).GetAwaiter().GetResult();
        var starts = 0;
        var ends = 0;
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _sent.Enqueue("start:" + Read(ci.ArgAt<string>(0), "eventId"));
            return _onStart(++starts);
        });
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _sent.Enqueue("end:" + Read(ci.ArgAt<string>(1), "eventId"));
            return _onEnd(++ends);
        });
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var payload = ci.ArgAt<string>(1);
            if (ToolIdIn(payload) is { } toolId)
                _sent.Enqueue("tool:" + toolId);
            return _onEvents(payload);
        });
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // A 200 whose results leave an item out settles nothing for it. That is a retry, not a quiet skip:
    // the item counts a retry, and nothing after it (here the end) is sent.
    [Theory]
    [InlineData(EventType.ToolExecution)]
    [InlineData(EventType.ToolFailed)]
    public async Task ItemTheServerDidNotAnswer_IsRetried_AndTheEndWaits(EventType type)
    {
        _onEvents = _ => new ApiResult(PostStatus.Ok, new ItemResults(new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>()));
        var item = type == EventType.ToolExecution ? Event("t1", type, ToolExecution("te-1")) : Event("t1", type, ToolFailed());
        await EnqueueAsync(item, T0);
        await EnqueueAsync(Event("end", EventType.SessionEnd), T0.AddMinutes(1));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().NotContain(s => s.StartsWith("end:"));
        var pending = await _queue.DequeueBatchAsync(10);
        pending.Select(e => e.Id).Should().Equal("t1", "end");
        pending.Single(e => e.Id == "t1").RetryCount.Should().Be(1);
    }

    // A session that ended and was resumed: the end happened first, so the server must see it first. Sent
    // after the resume start, it would close the session the user is still working in.
    [Fact]
    public async Task EndThenResumeStart_TheEndReachesTheServerBeforeTheStart_AcrossATransientPoll()
    {
        _onEnd = n => n == 1 ? ApiResult.Failed : ApiResult.Success;
        await EnqueueAsync(Event("E1", EventType.SessionEnd), T0);
        await EnqueueAsync(Start("S2"), T0.AddMinutes(1));
        var sut = CreateSut();

        await sut.PollOnceAsync(CancellationToken.None);
        _sent.Should().Equal("end:E1");

        await sut.PollOnceAsync(CancellationToken.None);
        _sent.Should().Equal("end:E1", "end:E1", "start:S2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // Each case drops the events of a session the server will not take, and each must stop at the next
    // start: a resumed session is a fresh chance, and its start and later items must still ship.
    [Fact]
    public async Task SessionGone_DropsOnlyUpToTheNextStart()
    {
        _onEvents = payload => ToolIdIn(payload) == "te-1" ? ApiResult.NotFound : ApiResult.Success;
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0);
        await EnqueueAsync(Start("S2"), T0.AddMinutes(1));
        await EnqueueAsync(Event("t2", EventType.ToolExecution, ToolExecution("te-2")), T0.AddMinutes(2));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("tool:te-1", "start:S2", "tool:te-2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SessionGoneOnTheSignalsBeforeAnEnd_TheDroppedEndIsNeverSent()
    {
        _onEvents = _ => ApiResult.NotFound;
        await EnqueueAsync(Event("f1", EventType.ToolFailed, ToolFailed()), T0);
        await EnqueueAsync(Event("E1", EventType.SessionEnd), T0.AddMinutes(1));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().NotContain(s => s.StartsWith("end:"));
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SessionGoneOnTheSignalsBeforeAStart_TheStartIsStillSent()
    {
        _onEvents = _ => ApiResult.NotFound;
        await EnqueueAsync(Event("f1", EventType.ToolFailed, ToolFailed()), T0);
        await EnqueueAsync(Start("S2"), T0.AddMinutes(1));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("start:S2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // A refused start does not show the session is missing from the server: an earlier start shipped and was
    // purged, and the session stays open. Its items are sent, and session_not_found drops them up to the next start.
    [Fact]
    public async Task RejectedStart_TheServerDecidesItsItems_UpToTheNextStart()
    {
        _onStart = n => n == 1 ? ApiResult.Rejected : ApiResult.Success;
        _onEvents = payload => ToolIdIn(payload) == "te-1" ? ApiResult.NotFound : ApiResult.Success;
        await EnqueueAsync(Start("S1"), T0);
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0.AddMinutes(1));
        await EnqueueAsync(Event("E1", EventType.SessionEnd), T0.AddMinutes(2));
        await EnqueueAsync(Start("S2"), T0.AddMinutes(3));
        await EnqueueAsync(Event("t2", EventType.ToolExecution, ToolExecution("te-2")), T0.AddMinutes(4));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("start:S1", "tool:te-1", "start:S2", "tool:te-2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // The refused start is remembered as dead, which must not close the session on the next poll either.
    [Fact]
    public async Task RejectedResume_OfASessionTheServerKnows_ItsItemsShip_InThisPollAndTheNext()
    {
        _onStart = _ => ApiResult.Rejected;
        await EnqueueAsync(Start("R"), T0);
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0.AddMinutes(1));
        var sut = CreateSut();
        await sut.PollOnceAsync(CancellationToken.None);

        await EnqueueAsync(Event("t2", EventType.ToolExecution, ToolExecution("te-2")), T0.AddMinutes(2));
        await sut.PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("start:R", "tool:te-1", "tool:te-2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task StartDeadInAnEarlierPoll_TheServerDecidesItsItems_UpToTheNextStart()
    {
        _onEvents = payload => ToolIdIn(payload) == "te-1" ? ApiResult.NotFound : ApiResult.Success;
        await EnqueueAsync(Start("S1"), T0);
        await _queue.MarkDeadAsync(["S1"]);
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0.AddMinutes(1));
        await EnqueueAsync(Event("t1b", EventType.ToolExecution, ToolExecution("te-1b")), T0.AddMinutes(2));
        await EnqueueAsync(Start("S2"), T0.AddMinutes(3));
        await EnqueueAsync(Event("t2", EventType.ToolExecution, ToolExecution("te-2")), T0.AddMinutes(4));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("tool:te-1", "start:S2", "tool:te-2");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    // The rescue never reaches a session held on this machine: its items stay here.
    [Fact]
    public async Task RejectedResume_OfAClosedSession_ItsItemsAreNeverSent()
    {
        _onStart = _ => ApiResult.Rejected;
        await EnqueueAsync(Start("S1"), T0);
        await _queue.MarkClosedAsync((await _queue.DequeueBatchAsync(10)).Single(e => e.Id == "S1"));
        await EnqueueAsync(Start("R"), T0.AddMinutes(1));
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0.AddMinutes(2));
        var sut = CreateSut();
        await sut.PollOnceAsync(CancellationToken.None);

        await EnqueueAsync(Event("t2", EventType.ToolExecution, ToolExecution("te-2")), T0.AddMinutes(3));
        await sut.PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("start:R");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EndNeverOvertakesAnOlderStart_WhileThatStartIsTransient()
    {
        _onStart = n => n == 1 ? ApiResult.Failed : ApiResult.Success;
        await EnqueueAsync(Start("S1"), T0);
        await EnqueueAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")), T0.AddMinutes(1));
        await EnqueueAsync(Event("E1", EventType.SessionEnd), T0.AddMinutes(2));
        var sut = CreateSut();

        await sut.PollOnceAsync(CancellationToken.None);
        _sent.Should().Equal("start:S1");

        await sut.PollOnceAsync(CancellationToken.None);
        _sent.Should().Equal("start:S1", "start:S1", "tool:te-1", "end:E1");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig
        {
            Mode = AgentMode.Pilot,
            OverwatchDir = Path.Combine(_dir, "no-fallback-file-here"),
            BatchSize = 50
        };
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var capture = Substitute.For<IConversationCapture>();
        capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        return new QueueProcessor(
            _queue, _api, redactor, capture, Substitute.For<IConversationCursorStore>(), Substitute.For<IGitReader>(),
            Substitute.For<IUsageStore>(), config, QueueProcessorLifecycleTests.Fixed(config.Mode),
            Substitute.For<IHostApplicationLifetime>(), NullLogger<QueueProcessor>.Instance);
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, EventType type, string payload = "{}") =>
        new() { Id = id, SessionId = Session, EventType = type, PayloadJson = payload };

    private static QueuedEvent Start(string id) =>
        Event(id, EventType.SessionStart,
            JsonSerializer.Serialize(new { sessionId = Session, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = T0 }));

    private static string? ToolIdIn(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("toolExecutions", out var tools) && tools.GetArrayLength() > 0
            ? tools[0].GetProperty("id").GetString()
            : null;
    }

    private static string ToolFailed() => $$"""{"toolName":"Bash","at":"{{T0:o}}"}""";

    private static string ToolExecution(string itemId) => JsonSerializer.Serialize(new
    {
        sessionId = Session,
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = T0 } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });

    private static string? Read(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
