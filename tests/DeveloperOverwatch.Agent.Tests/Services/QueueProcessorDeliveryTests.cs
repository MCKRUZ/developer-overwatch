using System.Collections.Concurrent;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Per-item delivery: each queued event is marked by its own outcome, so one bad item never sinks
/// the rest, and nothing is dropped for a failure a retry could fix.</summary>
public sealed class QueueProcessorDeliveryTests
{
    private const string Session = "s-1";
    private const string Transcript = "C:/transcripts/s-1.jsonl";
    private static readonly DateTime T0 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly IEventQueue _queue = Substitute.For<IEventQueue>();
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly IGitReader _git = Substitute.For<IGitReader>();
    private readonly CapturingLogger _logger = new();
    private readonly ConcurrentQueue<string> _shipped = new();
    private readonly ConcurrentQueue<string> _dead = new();
    private readonly ConcurrentQueue<string> _posts = new();
    private Func<string, ApiResult> _respond = _ => ApiResult.Success;

    public QueueProcessorDeliveryTests()
    {
        _queue.MarkShippedAsync(Arg.Do<IReadOnlyList<string>>(ids => ids.ToList().ForEach(_shipped.Enqueue)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _queue.MarkDeadAsync(Arg.Do<IReadOnlyList<string>>(ids => ids.ToList().ForEach(_dead.Enqueue)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var payload = ci.ArgAt<string>(1);
                if (!payload.Contains("\"activityEvents\""))
                    _posts.Enqueue(payload);
                return _respond(payload);
            });
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
    }

    [Fact]
    public async Task StartTransient_NothingElseShips_AndNothingIsDead()
    {
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Failed);

        await RunOneTickAsync(Start("start"), Event("t1", EventType.ToolExecution, ToolExecution("te-1")), Event("end", EventType.SessionEnd));

        await _api.DidNotReceive().SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _shipped.Should().BeEmpty();
        _dead.Should().BeEmpty("a transient start holds the session; it never drops it");
    }

    [Fact]
    public async Task StartRejected_TheSessionsItemsAreDead()
    {
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Rejected);

        await RunOneTickAsync(Start("start"), Event("t1", EventType.ToolExecution, ToolExecution("te-1")), Event("end", EventType.SessionEnd));

        _dead.Should().BeEquivalentTo(["start", "t1", "end"]);
        await _api.DidNotReceive().SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneRejectedToolFailureAmongThree_OnlyThatOneIsDead()
    {
        _respond = _ => Results(accepted: ["f1"], duplicate: ["f3"], rejected: new() { ["f2"] = "toolName is required" });

        await RunOneTickAsync(ToolFailed("f1"), ToolFailed("f2"), ToolFailed("f3"));

        _dead.Should().Equal("f2");
        _shipped.Should().BeEquivalentTo(["f1", "f3"]);
        _logger.Warnings.Should().ContainSingle(w => w.Contains("f2") && w.Contains("toolName is required"));
    }

    [Fact]
    public async Task IdMissingFromResults_StaysPending()
    {
        _respond = _ => Results();

        await RunOneTickAsync(Event("t1", EventType.ToolExecution, ToolExecution("te-1")));

        _shipped.Should().BeEmpty();
        _dead.Should().BeEmpty();
    }

    [Fact]
    public async Task OldServerWithoutResults_EveryItemIsDone()
    {
        await RunOneTickAsync(
            Event("t1", EventType.ToolExecution, ToolExecution("te-1")),
            ToolFailed("f1"),
            Event("a1", EventType.ActivityChanged, $$"""{"state":"waiting","at":"{{T0:o}}"}"""),
            Event("end", EventType.SessionEnd));

        _shipped.Should().BeEquivalentTo(["t1", "f1", "a1", "end"]);
        _dead.Should().BeEmpty();
    }

    [Fact]
    public async Task FourHundredFiftyTurns_ShipInThreePostsOfAtMost200()
    {
        CaptureTurns(450);

        await RunOneTickAsync(Marker("m1"));

        _posts.Select(p => TurnsIn(p).Count).Should().Equal(200, 200, 50);
        _shipped.Should().Contain("m1");
        Received.InOrder(() =>
        {
            _cursors.SetAsync(Session, 200, Arg.Any<CancellationToken>());
            _cursors.SetAsync(Session, 400, Arg.Any<CancellationToken>());
            _cursors.SetAsync(Session, 450, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RejectedTurnInSecondChunk_CursorMovesPastIt_ItIsLogged_AndLaterTurnsShip()
    {
        CaptureTurns(450);
        string? rejectedId = null;
        _respond = payload =>
        {
            var turns = TurnsIn(payload);
            var bad = turns.Where(t => t.Content == "turn 250").Select(t => t.Id).ToList();
            rejectedId ??= bad.FirstOrDefault();
            return Results(accepted: turns.Select(t => t.Id).Except(bad).ToList(),
                rejected: bad.ToDictionary(id => id, _ => "role is required"));
        };

        await RunOneTickAsync(Marker("m1"));

        _posts.Should().HaveCount(3);
        rejectedId.Should().NotBeNull();
        _logger.Warnings.Should().ContainSingle(w => w.Contains(rejectedId!) && w.Contains("role is required"));
        await _cursors.Received(1).SetAsync(Session, 450, Arg.Any<CancellationToken>());
        _shipped.Should().Contain("m1", "every captured turn has a definitive outcome");
        _dead.Should().BeEmpty();
    }

    [Fact]
    public async Task TransientSecondChunk_KeepsTheCursorWhereTheFirstChunkLeftIt()
    {
        CaptureTurns(450);
        var turnPosts = 0;
        _respond = payload => TurnsIn(payload).Count > 0 && ++turnPosts == 2 ? ApiResult.Failed : ApiResult.Success;

        await RunOneTickAsync(Marker("m1"));

        _posts.Should().HaveCount(2, "the third chunk waits for the next poll");
        await _cursors.Received(1).SetAsync(Session, 200, Arg.Any<CancellationToken>());
        await _cursors.DidNotReceive().SetAsync(Session, Arg.Is<int>(line => line > 200), Arg.Any<CancellationToken>());
        _shipped.Should().BeEmpty();
        _dead.Should().BeEmpty();
    }

    [Fact]
    public async Task RedeliveredEnd_ComesBackDuplicate_AndIsDone()
    {
        string? sent = null;
        _api.SendSessionEndAsync(Session, Arg.Do<string>(p => sent = p), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);

        await RunOneTickAsync(Event("end-1", EventType.SessionEnd, """{"sessionId":"s-1","endedAt":"2026-11-01T10:00:00Z"}"""));

        Read(sent!, "eventId").Should().Be("end-1", "the server dedupes a redelivered end by this id");
        _shipped.Should().Equal("end-1");
    }

    [Fact]
    public async Task RequestLevel400_OnlyThatPostsItemsAreDead()
    {
        _respond = payload => payload.Contains("te-1") ? new ApiResult(PostStatus.Rejected, Reason: "bad request") : ApiResult.Success;

        await RunOneTickAsync(
            Event("t1", EventType.ToolExecution, ToolExecution("te-1")),
            Event("t2", EventType.ToolExecution, ToolExecution("te-2")));

        _dead.Should().Equal("t1");
        _shipped.Should().Contain("t2");
        _logger.Warnings.Should().ContainSingle(w => w.Contains("bad request"));
    }

    [Fact]
    public async Task Normalization_InvalidEmailsBecomeUnknown_AndLengthsAreClamped()
    {
        var emails = new[] { "", "   ", "no-at", "@host", "user@", "a@b@c", "dev@example.com" };
        var starts = new ConcurrentQueue<string>();
        _api.SendSessionStartAsync(Arg.Do<string>(starts.Enqueue), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        _git.GetOriginUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/org/" + new string('r', 600));
        var events = emails
            .Select((email, i) => Start($"start-{i}", $"s-{i}", email))
            .Append(Event("t1", EventType.ToolExecution, ToolExecution("te-1", new string('x', 300))))
            .ToArray();

        await RunOneTickAsync(events);

        starts.Select(p => Read(p, "developerEmail")).Should().BeEquivalentTo(
            ["unknown@localhost", "unknown@localhost", "unknown@localhost", "unknown@localhost",
             "unknown@localhost", "unknown@localhost", "dev@example.com"]);
        starts.Should().AllSatisfy(p => Read(p, "projectKey")!.Length.Should().Be(512));
        var toolName = JsonDocument.Parse(_posts.Single()).RootElement.GetProperty("toolExecutions")[0].GetProperty("toolName").GetString();
        toolName.Should().HaveLength(255);
    }

    [Fact]
    public async Task EventQueue_StartState_ShippedWinsOverPending_AndAllDeadIsDead()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ow-start-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var queue = new EventQueue(Path.Combine(dir, "queue.db"));
        try
        {
            await queue.InitializeAsync();
            foreach (var (id, session) in new[] { ("a1", "a"), ("a2", "a"), ("b1", "b"), ("c1", "c") })
                await queue.EnqueueAsync(Event(id, EventType.SessionStart, sessionId: session));
            await queue.MarkShippedAsync(["a1"]);
            await queue.MarkDeadAsync(["c1"]);

            (await queue.GetStartStateAsync("a")).Should().Be(StartState.Shipped, "a resumed session's earlier start is on the server");
            (await queue.GetStartStateAsync("b")).Should().Be(StartState.Pending);
            (await queue.GetStartStateAsync("c")).Should().Be(StartState.Dead);
            (await queue.GetStartStateAsync("none")).Should().Be(StartState.Unknown);
        }
        finally
        {
            queue.Dispose();
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private async Task RunOneTickAsync(params QueuedEvent[] events)
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(events.ToList(), []);
        var config = new AgentConfig
        {
            PollIntervalSeconds = 1,
            BatchSize = 50,
            GitFallbackThreshold = 100,
            OverwatchDir = Path.Combine(Path.GetTempPath(), $"ow-qp-delivery-{Guid.NewGuid():N}")
        };
        var sut = new QueueProcessor(
            _queue, _api, PassThroughRedactor(), Substitute.For<IGitFallbackWriter>(), _capture, _cursors, _git,
            Substitute.For<IUsageStore>(), config, QueueProcessorLifecycleTests.Fixed(config.Mode),
            Substitute.For<IHostApplicationLifetime>(), _logger);

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(1500);
        await sut.StopAsync(CancellationToken.None);
    }

    private static IPrivacyRedactor PassThroughRedactor()
    {
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return redactor;
    }

    private void CaptureTurns(int count)
    {
        var turns = Enumerable.Range(0, count)
            .Select(i => new CapturedTurn($"uuid:{i}", "user", $"turn {i}", T0.AddSeconds(i), StartLine: i))
            .ToList();
        _capture.CaptureFromFileAsync(Transcript, 0, false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult(turns, count));
    }

    private static QueuedEvent Event(string id, EventType type, string payload = "{}", string sessionId = Session) => new()
    {
        Id = id,
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = DateTime.UtcNow
    };

    private static QueuedEvent Start(string id, string sessionId = Session, string email = "dev@example.com") =>
        Event(id, EventType.SessionStart,
            JsonSerializer.Serialize(new { sessionId, developerEmail = email, repoPath = "C:/repo", startedAt = T0 }), sessionId);

    private static QueuedEvent Marker(string id) =>
        Event(id, EventType.ConversationTurn, JsonSerializer.Serialize(new { transcriptPath = Transcript }));

    private static QueuedEvent ToolFailed(string id) =>
        Event(id, EventType.ToolFailed, $$"""{"toolName":"Bash","at":"{{T0:o}}"}""");

    private static string ToolExecution(string itemId, string toolName = "Read") => JsonSerializer.Serialize(new
    {
        sessionId = Session,
        toolExecutions = new[] { new { id = itemId, toolName, timestamp = T0 } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });

    private static ApiResult Results(
        IReadOnlyList<string>? accepted = null, IReadOnlyList<string>? duplicate = null, Dictionary<string, string>? rejected = null) =>
        new(PostStatus.Ok, new ItemResults(
            (accepted ?? []).ToHashSet(), (duplicate ?? []).ToHashSet(), rejected ?? new Dictionary<string, string>()));

    private static List<(string Id, string? Content)> TurnsIn(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("conversationTurns", out var turns)
            ? turns.EnumerateArray().Select(t => (t.GetProperty("id").GetString()!, t.GetProperty("content").GetString())).ToList()
            : [];
    }

    private static string? Read(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(property).GetString();
    }

    private sealed class CapturingLogger : ILogger<QueueProcessor>
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => _warnings.ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                _warnings.Enqueue(formatter(state, exception));
        }
    }
}
