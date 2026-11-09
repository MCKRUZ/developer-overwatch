using System.Diagnostics;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>What one seeded run left behind, for the assertions.</summary>
internal sealed record InvariantResult(
    int Seed,
    int Polls,
    int ExitPoll,
    int Deaths,
    TimeSpan Elapsed,
    InvariantFakeServer Server,
    InvariantWorkload Workload,
    IReadOnlySet<string> PendingAtExit,
    IReadOnlySet<string> PendingAfterSoak,
    int SoakHeldAttempts)
{
    public IReadOnlyDictionary<InvariantFault, int> Fired => Server.Plan.Fired;

    public string Summary() =>
        $"seed {Seed}: {Polls} polls (drained at {ExitPoll}), {Server.Attempts.Count} requests, {Deaths} deaths, " +
        $"{Elapsed.TotalMilliseconds:F0} ms; fired: " +
        string.Join(", ", Fired.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}"));
}

/// <summary>One seeded run of the real queue, ApiClient and processor against the fake server: the agent
/// polls under faults, dies and restarts, the clock jumps a day so every shipped row is purged, sess-d's
/// late items arrive, and the run continues until only the held items are pending, then soaks past the
/// longest backoff.</summary>
internal sealed class InvariantRun : IDisposable
{
    private const int FaultPolls = 30;
    private const int JumpPoll = 10;
    private const int MaxPolls = 400;
    private const int SoakPolls = 15;
    private const int MaxItems = 50;
    private const double FaultRate = 0.5;
    private const double CursorDeathRate = 0.2;

    private readonly int _seed;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-invariant-{Guid.NewGuid():N}");
    private readonly MutableTimeProvider _clock;
    private readonly FaultPlan _plan;
    private readonly InvariantFakeServer _server;
    private readonly HttpClient _http;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly Dictionary<string, List<CapturedTurn>> _transcripts = new(StringComparer.Ordinal);
    private readonly AgentConfig _config;
    private readonly InvariantWorkload _workload;
    private EventQueue? _queue;

    private InvariantRun(int seed)
    {
        _seed = seed;
        Directory.CreateDirectory(_dir);
        var now = DateTime.UtcNow;
        var pilotSince = now.AddMinutes(-30);
        _clock = new MutableTimeProvider(new DateTimeOffset(now));
        _plan = new FaultPlan(seed, FaultPolls, FaultRate, CursorDeathRate);
        _server = new InvariantFakeServer(_plan, InvariantWorkload.PoisonId, InvariantWorkload.HeldId, MaxItems);
        _http = new HttpClient(_server, disposeHandler: false) { BaseAddress = new Uri("http://overwatch.invalid/") };
        _config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = pilotSince, OverwatchDir = _dir, BatchSize = 4 };
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _capture.CaptureFromFileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var all = _transcripts[ci.ArgAt<string>(0)];
                return new ConversationCaptureResult(all.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), all.Count);
            });
        _workload = new InvariantWorkload(() => _queue!, _transcripts, pilotSince);
    }

    public static async Task<InvariantResult> RunAsync(int seed)
    {
        using var run = new InvariantRun(seed);
        return await run.ExecuteAsync();
    }

    public void Dispose()
    {
        _queue?.Dispose();
        _http.Dispose();
        _server.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    private async Task<InvariantResult> ExecuteAsync()
    {
        var watch = Stopwatch.StartNew();
        var agent = await StartAgentAsync();
        await _workload.SeedAsync();

        var polls = 0;
        var deaths = 0;
        while (polls < MaxPolls)
        {
            _plan.Poll = ++polls;
            if (polls == JumpPoll)
                await JumpADayAsync();
            (agent, deaths) = await PollAsync(agent, deaths);
            if (polls > FaultPolls && polls > JumpPoll && (await PendingAsync()).SetEquals(_workload.HeldPending))
                break;
        }

        var exitPoll = polls;
        var pendingAtExit = await PendingAsync();
        for (var i = 0; i < SoakPolls; i++)
        {
            _plan.Poll = ++polls;
            (agent, deaths) = await PollAsync(agent, deaths);
        }

        var soakHeldAttempts = _server.Attempts.Count(a => a.Poll > exitPoll && a.Items.Any(item => item.Id == InvariantWorkload.HeldId));
        return new InvariantResult(_seed, polls, exitPoll, deaths, watch.Elapsed, _server, _workload,
            pendingAtExit, await PendingAsync(), soakHeldAttempts);
    }

    /// <returns>The agent to poll next: a fresh one when this one died.</returns>
    private async Task<(QueueProcessor Agent, int Deaths)> PollAsync(QueueProcessor agent, int deaths)
    {
        try
        {
            await agent.PollOnceAsync(CancellationToken.None);
            return (agent, deaths);
        }
        catch (AgentDiedException)
        {
            return (await StartAgentAsync(), deaths + 1);
        }
    }

    /// <summary>A day passes: every shipped row is purged, and sess-d's late items are queued.</summary>
    private async Task JumpADayAsync()
    {
        _clock.Now = _clock.Now.AddHours(25);
        await _queue!.PurgeShippedAsync(TimeSpan.FromHours(24));
        await _queue.PurgeUnshippedAsync(TimeSpan.FromDays(30));
        await _workload.SeedLateAsync();
    }

    /// <summary>Read-only: dequeuing marks nothing.</summary>
    private async Task<HashSet<string>> PendingAsync() =>
        (await _queue!.DequeueBatchAsync(500)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>A fresh processor over the same queue file, as after the agent process dies and restarts.</summary>
    private async Task<QueueProcessor> StartAgentAsync()
    {
        _queue?.Dispose();
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), _clock);
        await _queue.InitializeAsync();
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return new QueueProcessor(
            _queue, new ApiClient(_http, NullLogger<ApiClient>.Instance), redactor, _capture,
            new DyingCursorStore(new ConversationCursorStore(_dir), _plan), Substitute.For<IGitReader>(),
            Substitute.For<IUsageStore>(), _config, QueueProcessorLifecycleTests.Fixed(AgentMode.Pilot),
            Substitute.For<IHostApplicationLifetime>(), NullLogger<QueueProcessor>.Instance);
    }
}
