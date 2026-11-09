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

/// <summary>A status the server gives a whole request must never become a dead verdict for every item in it.
/// A refused request of many items is split until each item has its own answer; one item refused alone is
/// rejected (400) or held (413); a transient answer stops the session; session_not_found drops it. Real queue,
/// one poll at a time, over every kind of unit the agent sends.</summary>
public sealed class RefusedRequestSplitTests : IDisposable
{
    public enum Kind { Turns, Tools, Files, Signals, Start, End }

    public enum Expect { Rejected, Held }

    private const string Session = "s";
    private const int Pending = 0, Shipped = 1, Dead = 2;
    private static readonly string[] UnitLists = ["conversationTurns", "toolExecutions", "fileChanges", "toolFailures"];
    private static readonly DateTime T0 = DateTime.UtcNow.AddHours(-1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-split-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly IConversationCursorStore _cursors = Substitute.For<IConversationCursorStore>();
    private readonly CapturingLogger _logger = new();
    private readonly ConcurrentQueue<List<string>> _requests = new();
    private readonly ConcurrentQueue<string> _accepted = new();
    private readonly ConcurrentQueue<string> _sent = new();
    private Func<List<string>, ApiResult> _onEvents = _ => ApiResult.Success;
    private ApiResult _onStart = ApiResult.Success;
    private ApiResult _onEnd = ApiResult.Success;

    public RefusedRequestSplitTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _sent.Enqueue("start"); return _onStart; });
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { _sent.Enqueue("end"); return _onEnd; });
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var units = Units(ci.ArgAt<string>(1));
            if (units.Count == 0)
                return ApiResult.Success;
            _requests.Enqueue(units);
            var result = _onEvents(units);
            if (result.Status == PostStatus.Ok)
                units.ForEach(_accepted.Enqueue);
            return result;
        });
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // Row: a request of many items refused with 400 or 413 is halved, and the first half is answered in full
    // before the second half goes. Every item is then accepted.
    [Theory]
    [InlineData(Kind.Turns, 400)]
    [InlineData(Kind.Turns, 413)]
    [InlineData(Kind.Tools, 400)]
    [InlineData(Kind.Tools, 413)]
    [InlineData(Kind.Signals, 400)]
    [InlineData(Kind.Signals, 413)]
    public async Task ManyItemsRefused_AreHalved_FirstHalfFirst_AndAllAccepted(Kind kind, int status)
    {
        _onEvents = units => units.Count > 2 ? Result(status) : ApiResult.Success;
        var ids = await EnqueueUnitsAsync(kind, 4, badIndex: -1);

        await PollAsync();

        _requests.Select(r => r.Count).Should().Equal(4, 2, 2);
        _requests.ElementAt(1).Should().Equal(_requests.First().Take(2));
        _accepted.Should().HaveCount(4);
        foreach (var id in ids)
            (await StateAsync(id)).Should().Be(Shipped);
        _sent.Should().Contain("end");
    }

    // A 400 for a request of four items, one of them bad: only that one is rejected.
    [Theory]
    [InlineData(Kind.Turns)]
    [InlineData(Kind.Tools)]
    [InlineData(Kind.Signals)]
    public async Task OneBadItemOfFour_OnlyItIsRejected_TheOtherThreeAreAccepted(Kind kind)
    {
        _onEvents = units => units.Any(u => u.Contains("bad")) ? ApiResult.Rejected : ApiResult.Success;
        var ids = await EnqueueUnitsAsync(kind, 4, badIndex: 2);

        await PollAsync();

        _accepted.Should().HaveCount(3).And.NotContain(u => u.Contains("bad"));
        if (kind == Kind.Signals)
            (await StatesAsync(ids)).Should().Equal(Shipped, Shipped, Dead, Shipped);
        if (kind == Kind.Turns)
            await _cursors.Received().SetAsync(Session, 4, Arg.Any<CancellationToken>());
        _sent.Should().Contain("end");
    }

    // Rows: one item refused on its own. 400 is a deterministic reject (a turn moves the cursor past it);
    // 413 can never be split further, so it holds and retries, and is never dead. A start or end refused with
    // session_not_found stays dead.
    [Theory]
    [InlineData(Kind.Turns, 400, Expect.Rejected)]
    [InlineData(Kind.Tools, 400, Expect.Rejected)]
    [InlineData(Kind.Files, 400, Expect.Rejected)]
    [InlineData(Kind.Signals, 400, Expect.Rejected)]
    [InlineData(Kind.Start, 400, Expect.Rejected)]
    [InlineData(Kind.End, 400, Expect.Rejected)]
    [InlineData(Kind.Start, 404, Expect.Rejected)]
    [InlineData(Kind.End, 404, Expect.Rejected)]
    [InlineData(Kind.Turns, 413, Expect.Held)]
    [InlineData(Kind.Tools, 413, Expect.Held)]
    [InlineData(Kind.Files, 413, Expect.Held)]
    [InlineData(Kind.Signals, 413, Expect.Held)]
    [InlineData(Kind.Start, 413, Expect.Held)]
    [InlineData(Kind.End, 413, Expect.Held)]
    public async Task OneItemRefusedAlone_IsRejectedOn400_AndHeldOn413(Kind kind, int status, Expect expect)
    {
        _onEvents = _ => Result(status);
        _onStart = kind == Kind.Start ? Result(status) : ApiResult.Success;
        _onEnd = kind == Kind.End ? Result(status) : ApiResult.Success;
        var id = (await EnqueueUnitsAsync(kind, 1, badIndex: 0)).Single();

        await PollAsync();

        if (expect == Expect.Held)
            await AssertHeldAsync(kind, id);
        else
            await AssertRejectedAsync(kind, id);
    }

    // Rows: a transient answer stops the walk, and session_not_found drops the session. Either way the second
    // half of a split request is never sent.
    [Theory]
    [InlineData(Kind.Turns, 503)]
    [InlineData(Kind.Tools, 503)]
    [InlineData(Kind.Signals, 503)]
    [InlineData(Kind.Turns, 404)]
    [InlineData(Kind.Tools, 404)]
    [InlineData(Kind.Signals, 404)]
    public async Task FirstHalfTransientOrSessionGone_TheSecondHalfIsNeverSent(Kind kind, int status)
    {
        _onEvents = units => units.Count > 2 ? ApiResult.Rejected : Result(status);
        var ids = await EnqueueUnitsAsync(kind, 4, badIndex: -1);

        await PollAsync();

        _requests.Select(r => r.Count).Should().Equal(4, 2);
        _accepted.Should().BeEmpty();
        _sent.Should().NotContain("end");
        var expected = status == 404 ? Dead : Pending;
        foreach (var id in ids.Append("end"))
            (await StateAsync(id)).Should().Be(expected);
        if (kind == Kind.Turns)
            await _cursors.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlwaysTooLargeTurn_HoldsItsCursorAndSession_WhileOtherSessionsKeepShipping()
    {
        _onEvents = units => units.Any(u => u.Contains("turn-bad")) ? Result(413) : ApiResult.Success;
        CaptureTurns(Session, ["turn-0", "turn-bad", "turn-2"]);
        await EnqueueAsync(Event("m", Session, EventType.ConversationTurn, Marker(Session)), T0);
        await EnqueueAsync(Event("after", Session, EventType.ToolExecution, Tools(Session, ["te-after"], [])), T0.AddMinutes(1));
        var sut = CreateSut();

        for (var poll = 0; poll < 6; poll++)
        {
            await EnqueueAsync(Event($"b{poll}", "b", EventType.ToolExecution, Tools("b", [$"te-b{poll}"], [])), T0.AddMinutes(2));
            await sut.PollOnceAsync(CancellationToken.None);
            (await StateAsync($"b{poll}")).Should().Be(Shipped, "another session is never held by this one");
        }

        var attempts = _requests.Count(r => r.Count == 1 && r[0].Contains("turn-bad"));
        attempts.Should().BeGreaterThan(1, "a held turn is retried on later polls");
        _logger.Warnings.Count(w => w.Contains("too large")).Should().Be(attempts, "one warning per poll that tries it");
        _accepted.Should().NotContain(u => u.Contains("turn-2") || u.Contains("te-after"));
        await _cursors.Received().SetAsync(Session, 1, Arg.Any<CancellationToken>());
        await _cursors.DidNotReceive().SetAsync(Session, Arg.Is<int>(line => line > 1), Arg.Any<CancellationToken>());
        (await StatesAsync(["m", "after"])).Should().Equal(Pending, Pending);
    }

    private async Task AssertHeldAsync(Kind kind, string id)
    {
        (await StateAsync(id)).Should().Be(Pending);
        (await _queue.DequeueBatchAsync(10)).Single(e => e.Id == id).RetryCount.Should().Be(1);
        _logger.Warnings.Should().ContainSingle(w => w.Contains("too large"));
        if (kind != Kind.End)
            _sent.Should().NotContain("end");
        if (kind == Kind.Turns)
            await _cursors.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private async Task AssertRejectedAsync(Kind kind, string id)
    {
        if (kind == Kind.Turns)
        {
            (await StateAsync(id)).Should().Be(Shipped, "a rejected turn is definitive: the marker is done");
            await _cursors.Received().SetAsync(Session, 1, Arg.Any<CancellationToken>());
        }
        else
        {
            (await StateAsync(id)).Should().Be(Dead);
        }

        if (kind == Kind.Start)
            _sent.Should().Contain("end", "a refused start leaves the session's items to the server's answer");
        else if (kind != Kind.End)
            (await StateAsync("end")).Should().Be(Shipped);
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    /// <summary>Queues <paramref name="count"/> units of <paramref name="kind"/>, then an end. The unit at
    /// <paramref name="badIndex"/> carries "bad" in its id or text. Returns the queued event ids.</summary>
    private async Task<IReadOnlyList<string>> EnqueueUnitsAsync(Kind kind, int count, int badIndex)
    {
        string Name(string prefix, int i) => i == badIndex ? $"{prefix}-bad" : $"{prefix}-{i}";
        var names = Enumerable.Range(0, count).ToList();
        IReadOnlyList<string> ids;
        switch (kind)
        {
            case Kind.Turns:
                CaptureTurns(Session, names.Select(i => Name("turn", i)).ToArray());
                await EnqueueAsync(Event("m", Session, EventType.ConversationTurn, Marker(Session)), T0);
                ids = ["m"];
                break;
            case Kind.Tools or Kind.Files:
                var tools = kind == Kind.Files ? 0 : (count + 1) / 2;
                var payload = Tools(Session,
                    names.Take(tools).Select(i => Name("te", i)).ToArray(), names.Skip(tools).Select(i => Name("fc", i)).ToArray());
                var type = kind == Kind.Files ? EventType.FileChange : EventType.ToolExecution;
                await EnqueueAsync(Event("e", Session, type, payload), T0);
                ids = ["e"];
                break;
            case Kind.Signals:
                ids = names.Select(i => Name("f", i)).ToList();
                foreach (var (fid, i) in ids.Select((f, i) => (f, i)))
                    await EnqueueAsync(Event(fid, Session, EventType.ToolFailed, $$"""{"toolName":"Bash","at":"{{T0:o}}"}"""), T0.AddSeconds(i));
                break;
            case Kind.Start:
                await EnqueueAsync(Event("start", Session, EventType.SessionStart, StartPayload()), T0);
                ids = ["start"];
                break;
            default:
                ids = ["end"];
                break;
        }

        await EnqueueAsync(Event("end", Session, EventType.SessionEnd), T0.AddMinutes(5));
        return ids;
    }

    private Task PollAsync() => CreateSut().PollOnceAsync(CancellationToken.None);

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig { Mode = AgentMode.Pilot, OverwatchDir = Path.Combine(_dir, "none"), BatchSize = 50 };
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new QueueProcessor(
            _queue, _api, redactor, _capture, _cursors, Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(),
            config, QueueProcessorLifecycleTests.Fixed(config.Mode), Substitute.For<IHostApplicationLifetime>(), _logger);
    }

    private void CaptureTurns(string sessionId, string[] contents)
    {
        var turns = contents.Select((c, i) => new CapturedTurn($"uuid:{sessionId}:{c}", "user", c, T0.AddSeconds(i), StartLine: i)).ToList();
        _capture.CaptureFromFileAsync(Transcript(sessionId), Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(ci => new ConversationCaptureResult(turns.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), turns.Count));
    }

    private async Task<int> StateAsync(string id)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(_dir, "queue.db")};Pooling=False");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT shipped FROM events WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<int>> StatesAsync(IEnumerable<string> ids)
    {
        var states = new List<int>();
        foreach (var id in ids)
            states.Add(await StateAsync(id));
        return states;
    }

    private static ApiResult Result(int status) => status switch
    {
        400 => new ApiResult(PostStatus.Rejected, Reason: "bad request"),
        413 => new ApiResult(PostStatus.TooLarge, Reason: "request too large (413)"),
        404 => ApiResult.NotFound,
        _ => ApiResult.Failed
    };

    private static List<string> Units(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return UnitLists
            .Where(list => root.TryGetProperty(list, out var array) && array.ValueKind == JsonValueKind.Array)
            .SelectMany(list => root.GetProperty(list).EnumerateArray().Select(e => e.GetRawText()))
            .ToList();
    }

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static QueuedEvent Event(string id, string sessionId, EventType type, string payload = "{}") =>
        new() { Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload };

    private static string Transcript(string sessionId) => $"C:/transcripts/{sessionId}.jsonl";

    private static string Marker(string sessionId) => JsonSerializer.Serialize(new { transcriptPath = Transcript(sessionId) });

    private static string StartPayload() =>
        JsonSerializer.Serialize(new { sessionId = Session, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = T0 });

    private static string Tools(string sessionId, string[] toolIds, string[] fileIds) => JsonSerializer.Serialize(new
    {
        sessionId,
        toolExecutions = toolIds.Select(id => new { id, toolName = "Read", timestamp = T0 }),
        fileChanges = fileIds.Select(id => new { id, filePath = $"C:/repo/{id}.cs", changeType = "Modified", timestamp = T0 }),
        conversationTurns = Array.Empty<object>()
    });

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
