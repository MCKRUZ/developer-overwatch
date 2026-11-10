extern alias agent;

namespace DeveloperOverwatch.API.Tests.Delivery;

using System.Diagnostics;
using agent::DeveloperOverwatch.Agent.Models;
using agent::DeveloperOverwatch.Agent.Services;
using DeveloperOverwatch.API.Tests.Controllers;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

/// <summary>What the database held after one seeded run.</summary>
internal sealed record StoredState(
    IReadOnlyList<Session> Sessions,
    IReadOnlyList<ConversationTurn> Turns,
    IReadOnlyList<string> ToolIds,
    IReadOnlyList<string> FileIds,
    IReadOnlyList<string> FailureIds);

/// <summary>What one seeded run left behind, for the assertions.</summary>
internal sealed record RealServerResult(
    int Seed, int Polls, int Restarts, TimeSpan Elapsed, RealServerFaultHandler Handler,
    RealServerWorkload Workload, IReadOnlySet<string> PendingAtExit, StoredState Stored)
{
    public string Summary() =>
        $"seed {Seed}: {Polls} polls, {Handler.Requests} requests, {Restarts} restarts, {Elapsed.TotalMilliseconds:F0} ms; fired: " +
        string.Join(", ", Handler.Fired.OrderBy(f => f.Key).Select(f => $"{f.Key}={f.Value}"));
}

/// <summary>One seeded run of the real agent (queue, ApiClient, processor) against the real API and its
/// database in memory: the agent polls under injected faults, dies and restarts, and keeps polling until
/// the faults stop and its queue is empty.</summary>
internal sealed class RealServerRun : IDisposable
{
    private const int FaultPolls = 30;
    private const int MaxPolls = 300;

    private readonly int _seed;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-real-server-{Guid.NewGuid():N}");
    private readonly RealServerFactory _factory = new();
    private readonly RealServerFaultHandler _handler;
    private readonly HttpClient _http;
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();
    private readonly Dictionary<string, List<CapturedTurn>> _transcripts = new(StringComparer.Ordinal);
    private readonly AgentConfig _config;
    private readonly RealServerWorkload _workload;
    private EventQueue? _queue;

    private RealServerRun(int seed)
    {
        _seed = seed;
        Directory.CreateDirectory(_dir);
        var pilotSince = DateTime.UtcNow.AddMinutes(-30);
        _handler = new RealServerFaultHandler(seed, FaultPolls) { InnerHandler = _factory.Server.CreateHandler() };
        _http = new HttpClient(_handler) { BaseAddress = _factory.Server.BaseAddress };
        _http.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);
        _config = new AgentConfig { Mode = AgentMode.Pilot, PilotSince = pilotSince, OverwatchDir = _dir, BatchSize = 4 };
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _capture.CaptureFromFileAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var all = _transcripts[ci.ArgAt<string>(0)];
                return new ConversationCaptureResult(all.Where(t => t.StartLine >= ci.ArgAt<int>(1)).ToList(), all.Count);
            });
        _workload = new RealServerWorkload(() => _queue!, _transcripts, pilotSince);
    }

    public static async Task<RealServerResult> RunAsync(int seed)
    {
        using var run = new RealServerRun(seed);
        return await run.ExecuteAsync();
    }

    public void Dispose()
    {
        _queue?.Dispose();
        _http.Dispose();
        _factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    private async Task<RealServerResult> ExecuteAsync()
    {
        var watch = Stopwatch.StartNew();
        var agent = await StartAgentAsync();
        await _workload.SeedAsync();

        var polls = 0;
        var restarts = 0;
        while (polls < MaxPolls)
        {
            _handler.Poll = ++polls;
            try
            {
                await agent.PollOnceAsync(CancellationToken.None);
            }
            catch (AgentDiedException)
            {
                agent = await StartAgentAsync();
                restarts++;
            }

            if (polls > FaultPolls && (await PendingAsync()).Count == 0)
                break;
        }

        return new RealServerResult(_seed, polls, restarts, watch.Elapsed, _handler, _workload,
            await PendingAsync(), await ReadStoredAsync());
    }

    /// <summary>Read-only: dequeuing marks nothing.</summary>
    private async Task<HashSet<string>> PendingAsync() =>
        (await _queue!.DequeueBatchAsync(500)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    private async Task<StoredState> ReadStoredAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
        return new StoredState(
            await db.Sessions.AsNoTracking().ToListAsync(),
            await db.ConversationTurns.AsNoTracking().ToListAsync(),
            await db.ToolExecutions.Select(t => t.Id).ToListAsync(),
            await db.FileChanges.Select(f => f.Id).ToListAsync(),
            await db.SessionToolFailures.Select(f => f.Id).ToListAsync());
    }

    /// <summary>A fresh processor over the same queue file and cursors, as after the agent process dies.</summary>
    private async Task<QueueProcessor> StartAgentAsync()
    {
        _queue?.Dispose();
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"), TimeProvider.System);
        await _queue.InitializeAsync();
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var mode = Substitute.For<IAgentModeSource>();
        mode.CurrentMode().Returns(AgentMode.Pilot);
        return new QueueProcessor(
            _queue, new ApiClient(_http, NullLogger<ApiClient>.Instance), redactor, _capture,
            new ConversationCursorStore(_dir), Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(),
            _config, mode, Substitute.For<IHostApplicationLifetime>(), NullLogger<QueueProcessor>.Instance);
    }
}
