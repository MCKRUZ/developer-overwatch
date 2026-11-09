using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit.Abstractions;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The delivery contract's invariants, proved end to end: the real queue, ApiClient and processor
/// against a fake server with the real server's idempotency rules, under injected faults, including the
/// agent dying between a send and its mark. Every accepted item must be stored exactly once, nothing may
/// be lost but deliberate rejections, nothing from before enrolment may leave the machine, and the queue
/// must drain once the faults stop.</summary>
public sealed class DeliveryInvariantTests : IDisposable
{
    private const int Seed = 20261109;
    private const int FaultPolls = 30;
    private const int MaxPolls = 200;
    private const double FaultRate = 0.5;

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime PilotSince = Now.AddMinutes(-30);

    private readonly ITestOutputHelper _output;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-invariant-{Guid.NewGuid():N}");
    private readonly FakeServer _server = new();
    private readonly HttpClient _http;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly Dictionary<string, List<CapturedTurn>> _transcripts = new(StringComparer.Ordinal);
    private readonly AgentConfig _config;
    private EventQueue? _queue;
    private DateTime _preClock = Now.AddMinutes(-50);
    private DateTime _postClock = Now.AddMinutes(-20);

    public DeliveryInvariantTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
        _http = new HttpClient(_server, disposeHandler: false) { BaseAddress = new Uri("http://overwatch.invalid/") };
        _config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = PilotSince, OverwatchDir = _dir, BatchSize = 4 };
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _capture.CaptureFromFileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var all = _transcripts[ci.ArgAt<string>(0)];
                return new ConversationCaptureResult(all.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), all.Count);
            });
    }

    public void Dispose()
    {
        _queue?.Dispose();
        _http.Dispose();
        _server.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task ManyPollsUnderFaults_StoreEveryItemOnce_LoseOnlyRejections_SendNothingPreEnrolment_AndDrain()
    {
        var watch = Stopwatch.StartNew();
        var agent = await StartAgentAsync();
        var seeded = await SeedAsync();

        var polls = 0;
        var deaths = 0;
        while (polls < MaxPolls)
        {
            _server.Poll = ++polls;
            try
            {
                await agent.PollOnceAsync(CancellationToken.None);
            }
            catch (AgentDiedException)
            {
                deaths++;
                agent = await StartAgentAsync();
            }

            if (polls > FaultPolls && await _queue!.GetUnshippedCountAsync() == 0)
                break;
        }

        _output.WriteLine($"{polls} polls, {_server.Requests.Count} requests, {deaths} deaths, {watch.ElapsedMilliseconds} ms; " +
            string.Join(", ", _server.Injected.Select(f => $"{f.Key}={f.Value}")));

        // Every fault kind fired, so the run proves something under each one.
        _server.Injected.Keys.Should().BeEquivalentTo(Enum.GetValues<Fault>().Where(f => f != Fault.None));
        deaths.Should().BeGreaterThan(0);

        // Exactly the expected ids, per kind: a missing id is a lost item; an extra one is an unstable id resent.
        foreach (var (kind, ids) in seeded.Expected)
            _server.StoredIds(kind).Should().BeEquivalentTo(ids, $"the server must hold exactly the expected {kind} ids");
        _server.StoredIds("failure").Should().HaveCount(seeded.Expected["failure"].Count);

        var sent = string.Join("\n", _server.Requests);
        foreach (var forbidden in seeded.Forbidden)
            sent.Should().NotContain(forbidden, "nothing from before pilotSince may leave the machine");

        _server.Requests.Where(r => r.Contains("/events\n")).Should().OnlyContain(r => CarriesSomething(r),
            "an /events POST that carries no item and no activity state is a wasted request");

        (await _queue!.GetUnshippedCountAsync()).Should().Be(0);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    private sealed record Seeded(Dictionary<string, HashSet<string>> Expected, IReadOnlyList<string> Forbidden);

    private async Task<Seeded> SeedAsync()
    {
        // A: everything after the cutoff; one of its turns is rejected on every attempt.
        await PostAsync(Start("a-start", "sess-a"));
        await PostAsync(Tool("a-tool-1", "sess-a", "te-a1"));
        await PostAsync(Failure("a-fail-1", "sess-a"));
        await PostAsync(Tool("a-tool-2", "sess-a", "te-a2"));
        await PostAsync(Failure("a-fail-2", "sess-a"));
        var aTurns = Turns("sess-a", "uuid:a-turn-", 5, pre: false);
        await PostAsync(Marker("a-marker", "sess-a"));
        await PostAsync(Activity("a-activity", "sess-a"));
        await PostAsync(End("a-end", "sess-a"));
        var rejectedTurn = aTurns[2];

        // B: after the cutoff; one tool execution is rejected on every attempt.
        await PostAsync(Start("b-start", "sess-b"));
        await PostAsync(Tool("b-tool-1", "sess-b", "te-b1"));
        await PostAsync(Tool("b-tool-2", "sess-b", "te-b2"));
        await PostAsync(End("b-end", "sess-b"));

        // C: every item from before enrolment. None of it may be sent.
        var cTurns = Turns("sess-c", "uuid:c-turn-", 2, pre: true);
        await PreAsync(Start("c-start", "sess-c"));
        await PreAsync(Tool("c-tool", "sess-c", "te-c1"));
        await PreAsync(Failure("c-fail", "sess-c"));
        await PreAsync(Marker("c-marker", "sess-c"));
        await PreAsync(End("c-end", "sess-c"));

        // D: its start predates enrolment, so the server never learns the session and refuses its items.
        await PreAsync(Start("d-start", "sess-d"));
        Turns("sess-d", "uuid:d-turn-", 1, pre: false);
        await PostAsync(Tool("d-tool", "sess-d", "te-d1"));
        await PostAsync(Marker("d-marker", "sess-d"));
        await PostAsync(End("d-end", "sess-d"));

        _server.RejectedIds.UnionWith([rejectedTurn, "te-b2"]);

        var expected = new Dictionary<string, HashSet<string>>
        {
            ["start"] = ["a-start", "b-start"],
            ["end"] = ["a-end", "b-end"],
            ["tool"] = ["te-a1", "te-a2", "te-b1"],
            ["file"] = [],
            ["failure"] = ["a-fail-1", "a-fail-2"],
            ["turn"] = aTurns.Where(id => id != rejectedTurn).ToHashSet()
        };
        string[] forbidden = ["sess-c", "c-start", "te-c1", "c-fail", "c-end", "secret sess-c", .. cTurns, "d-start"];
        return new Seeded(expected, forbidden);
    }

    /// <summary>A fresh processor over the same queue file, as after the agent process dies and restarts.</summary>
    private async Task<QueueProcessor> StartAgentAsync()
    {
        _queue?.Dispose();
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        await _queue.InitializeAsync();
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new QueueProcessor(
            _queue, new ApiClient(_http, NullLogger<ApiClient>.Instance), redactor, _capture,
            new ConversationCursorStore(_dir), Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(), _config,
            QueueProcessorLifecycleTests.Fixed(AgentMode.Pilot), Substitute.For<IHostApplicationLifetime>(),
            NullLogger<QueueProcessor>.Instance);
    }

    /// <returns>The stable ids the turns are sent under.</returns>
    private List<string> Turns(string sessionId, string sourcePrefix, int count, bool pre)
    {
        var turns = Enumerable.Range(0, count)
            .Select(i => new CapturedTurn($"{sourcePrefix}{i}", "user", $"secret {sessionId} turn {i}", NextTime(pre), StartLine: i))
            .ToList();
        _transcripts[TranscriptOf(sessionId)] = turns;
        return turns.Select(t => StableTurnId.From(t.SourceId)).ToList();
    }

    private Task PostAsync(QueuedEvent evt) => EnqueueAsync(evt, NextTime(pre: false));

    private Task PreAsync(QueuedEvent evt) => EnqueueAsync(evt, NextTime(pre: true));

    private DateTime NextTime(bool pre) => pre ? (_preClock = _preClock.AddSeconds(1)) : (_postClock = _postClock.AddSeconds(1));

    private Task EnqueueAsync(QueuedEvent evt, DateTime createdAt) =>
        _queue!.EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = createdAt
        });

    private static string TranscriptOf(string sessionId) => $"C:/transcripts/{sessionId}.jsonl";

    private static QueuedEvent Event(string id, string sessionId, EventType type, string payload) =>
        new() { Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload };

    private static QueuedEvent Start(string id, string sessionId) => Event(id, sessionId, EventType.SessionStart,
        JsonSerializer.Serialize(new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = Now }));

    private static QueuedEvent End(string id, string sessionId) =>
        Event(id, sessionId, EventType.SessionEnd, JsonSerializer.Serialize(new { sessionId, endedAt = Now }));

    private static QueuedEvent Marker(string id, string sessionId) =>
        Event(id, sessionId, EventType.ConversationTurn, JsonSerializer.Serialize(new { transcriptPath = TranscriptOf(sessionId) }));

    private static QueuedEvent Failure(string id, string sessionId) =>
        Event(id, sessionId, EventType.ToolFailed, $$"""{"toolName":"Bash","at":"{{Now:o}}"}""");

    private static QueuedEvent Activity(string id, string sessionId) =>
        Event(id, sessionId, EventType.ActivityChanged, $$"""{"state":"waiting","at":"{{Now:o}}"}""");

    private static QueuedEvent Tool(string id, string sessionId, string itemId) => Event(id, sessionId, EventType.ToolExecution,
        JsonSerializer.Serialize(new
        {
            sessionId,
            toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = Now } },
            fileChanges = Array.Empty<object>(),
            conversationTurns = Array.Empty<object>()
        }));

    private static bool CarriesSomething(string request)
    {
        using var doc = JsonDocument.Parse(request[(request.IndexOf('\n') + 1)..]);
        return new[] { "conversationTurns", "toolExecutions", "fileChanges", "toolFailures", "activityEvents" }
            .Sum(list => doc.RootElement.TryGetProperty(list, out var items) ? items.GetArrayLength() : 0) > 0;
    }

    private sealed class AgentDiedException() : Exception("injected: the agent died between send and mark");

    private enum Fault
    {
        None,
        CommitThenTimeout,
        ServerError,
        NotFoundWithoutCode,
        CommitThenDie
    }

    /// <summary>Holds what the real server would store, with its idempotency rules: turns, tool executions,
    /// file changes and tool failures by id; starts and ends by eventId. An unknown session is
    /// session_not_found, and chosen ids are rejected inside a 200 on every attempt.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private static readonly (string Kind, string List)[] ItemLists =
            [("turn", "conversationTurns"), ("tool", "toolExecutions"), ("file", "fileChanges"), ("failure", "toolFailures")];

        private readonly Random _random = new(Seed);
        private readonly HashSet<string> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _stored = new(StringComparer.Ordinal);

        public int Poll { get; set; }

        public HashSet<string> RejectedIds { get; } = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public Dictionary<Fault, int> Injected { get; } = [];

        public IReadOnlyList<string> StoredIds(string kind) => _stored.GetValueOrDefault(kind) ?? [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = await request.Content!.ReadAsStringAsync(ct);
            Requests.Add($"{path}\n{body}");

            var fault = NextFault();
            if (fault == Fault.ServerError)
                return Json(HttpStatusCode.InternalServerError, "{}");
            if (fault == Fault.NotFoundWithoutCode)
                return Json(HttpStatusCode.NotFound, "{}");

            var response = Handle(path, body);
            if (fault == Fault.CommitThenTimeout)
                throw new TaskCanceledException("injected: committed, then timed out");
            if (fault == Fault.CommitThenDie)
                throw new AgentDiedException();
            return response;
        }

        private Fault NextFault()
        {
            if (Poll > FaultPolls || _random.NextDouble() >= FaultRate)
                return Fault.None;
            var fault = (Fault)_random.Next(1, 5);
            Injected[fault] = Injected.GetValueOrDefault(fault) + 1;
            return fault;
        }

        private HttpResponseMessage Handle(string path, string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (path == "/api/sessions/start")
            {
                var started = root.GetProperty("sessionId").GetString()!;
                _sessions.Add(started);
                return SessionOutcome(started, Store("start", root.GetProperty("eventId").GetString()!));
            }

            var sessionId = path.Split('/')[3];
            if (!_sessions.Contains(sessionId))
                return Json(HttpStatusCode.NotFound, """{"code":"session_not_found"}""");
            if (path.EndsWith("/end", StringComparison.Ordinal))
                return SessionOutcome(sessionId, Store("end", root.GetProperty("eventId").GetString()!));

            var accepted = new List<string>();
            var duplicate = new List<string>();
            var rejected = new List<object>();
            foreach (var (kind, list) in ItemLists)
            {
                if (!root.TryGetProperty(list, out var items))
                    continue;
                foreach (var id in items.EnumerateArray().Select(i => i.GetProperty("id").GetString()!))
                {
                    if (RejectedIds.Contains(id))
                        rejected.Add(new { id, reason = "invalid item" });
                    else if (Store(kind, id))
                        accepted.Add(id);
                    else
                        duplicate.Add(id);
                }
            }

            return Json(HttpStatusCode.OK,
                JsonSerializer.Serialize(new { eventsIngested = accepted.Count, results = new { accepted, duplicate, rejected } }));
        }

        /// <returns>True when the id is new to the store.</returns>
        private bool Store(string kind, string id)
        {
            var ids = _stored.TryGetValue(kind, out var existing) ? existing : _stored[kind] = [];
            if (ids.Contains(id))
                return false;
            ids.Add(id);
            return true;
        }

        private static HttpResponseMessage SessionOutcome(string sessionId, bool isNew) =>
            Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { sessionId, outcome = isNew ? "accepted" : "duplicate" }));

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
