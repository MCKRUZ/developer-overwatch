using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>How the processor, over a real queue, reacts to a mode change, a session the server does not
/// know, session starts held by local mode, and several sessions failing in one poll.</summary>
public sealed class QueueProcessorLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-qp-life-{Guid.NewGuid():N}");
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly IHostApplicationLifetime _lifetime = Substitute.For<IHostApplicationLifetime>();
    private readonly EventQueue _queue;

    public QueueProcessorLifecycleTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(DbPath);
        _queue.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    /// <summary>A mode source that always answers <paramref name="mode"/>, so a processor keeps running.</summary>
    internal static IAgentModeSource Fixed(AgentMode mode)
    {
        var source = Substitute.For<IAgentModeSource>();
        source.CurrentMode().Returns(mode);
        return source;
    }

    // A machine switched to Local must stop shipping now, not at the next logon.
    [Fact]
    public async Task Pilot_ConfigNowLocal_ShipsNothingAndStops()
    {
        File.WriteAllText(ConfigPath, """{"mode":"Local"}""");
        await EnqueueAsync("e1", "s1", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, new ConfigFileModeSource(ConfigPath)));

        _api.ReceivedCalls().Should().BeEmpty();
        _lifetime.Received(1).StopApplication();
        (await _queue.GetUnshippedCountAsync()).Should().Be(1, "nothing was processed");
    }

    // A half-saved config.json says nothing about the mode: ship nothing, but keep running.
    [Fact]
    public async Task Pilot_ConfigUnparseable_ShipsNothingAndKeepsRunning()
    {
        File.WriteAllText(ConfigPath, "{ this is not json");
        await EnqueueAsync("e1", "s1", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, new ConfigFileModeSource(ConfigPath)));

        _api.ReceivedCalls().Should().BeEmpty();
        _lifetime.DidNotReceive().StopApplication();
    }

    [Fact]
    public async Task Pilot_ModeBrieflyUnreadable_SkipsThatPollThenShips()
    {
        await _queue.OpenAsync("s1");
        var source = Substitute.For<IAgentModeSource>();
        source.CurrentMode().Returns(_ => throw new IOException("config.json is locked"), _ => AgentMode.Pilot);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        await EnqueueAsync("e1", "s1", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, source), millis: 2600);

        // The event itself, then the activity signal it implies.
        await _api.Received(1).SendSessionEventsAsync("s1", "{}", Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
        _lifetime.DidNotReceive().StopApplication();
    }

    // Enrolment needs a restart too; the session-start hook starts the agent again with the new config.
    [Fact]
    public async Task Local_ConfigNowPilot_Stops()
    {
        File.WriteAllText(ConfigPath, """{"mode":"Pilot"}""");

        await RunAsync(CreateSut(AgentMode.Local, new ConfigFileModeSource(ConfigPath)));

        _lifetime.Received(1).StopApplication();
    }

    // A 404 means the server does not know the session, which no retry can fix.
    [Fact]
    public async Task Pilot_EventsReturn404_GroupIsMarkedDeadAndNeverDequeuedAgain()
    {
        await _queue.OpenAsync("ghost");
        await _queue.OpenAsync("known");
        _api.SendSessionEventsAsync("ghost", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.NotFound);
        _api.SendSessionEventsAsync("known", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        await EnqueueAsync("g1", "ghost", EventType.ToolExecution, ageMinutes: 3);
        await EnqueueAsync("g2", "ghost", EventType.ToolExecution, ageMinutes: 3);
        await EnqueueAsync("k1", "known", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)), millis: 2600);

        await _api.Received(1).SendSessionEventsAsync("ghost", Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
        _lifetime.DidNotReceive().StopApplication();
    }

    // One failing session must not delay the others in the same poll.
    [Fact]
    public async Task Pilot_TwoSessionsFail_BothAreTriedInTheSamePoll()
    {
        await _queue.OpenAsync("a");
        await _queue.OpenAsync("b");
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Failed);
        await EnqueueAsync("a1", "a", EventType.ToolExecution, ageMinutes: 2);
        await EnqueueAsync("b1", "b", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)));

        await _api.Received(1).SendSessionEventsAsync("a", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received(1).SendSessionEventsAsync("b", Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(2, "failed events are retried later");
    }

    // Pilot capture starts at enrolment. Local-era data, starts included, never leaves the machine
    // except through an explicit usage-only backfill.
    [Fact]
    public async Task LocalThenPilot_ShipsNoLocalEraSessionStart()
    {
        await EnqueueAsync("start", "s1", EventType.SessionStart, """{"repoPath":"C:/repos/widget"}""", ageMinutes: 3);
        await EnqueueAsync("tool", "s1", EventType.ToolExecution, ageMinutes: 2);

        await RunAsync(CreateSut(AgentMode.Local, Fixed(AgentMode.Local)));
        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "local mode handles every event");

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)));

        _api.ReceivedCalls().Should().BeEmpty();
    }

    // The end ships last, once everything else from the poll is definitive. The signals POST timed out,
    // so the end waits for the next poll, and the tool failure is not lost.
    [Fact]
    public async Task Pilot_SignalsTimedOut_EndWaitsForThem_AndBothShipNextPoll()
    {
        await _queue.OpenAsync("s1");
        _api.SendSessionEndAsync("s1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        var signalPayloads = new List<string>();
        _api.SendSessionEventsAsync("s1", Arg.Do<string>(signalPayloads.Add), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Failed, ApiResult.Success);
        await EnqueueAsync("end", "s1", EventType.SessionEnd, ageMinutes: 3);
        await EnqueueAsync("fail", "s1", EventType.ToolFailed,
            $"{{\"toolName\":\"Bash\",\"at\":\"{DateTime.UtcNow.AddMinutes(-2):o}\"}}", ageMinutes: 2);

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)), millis: 4200);

        await _api.Received(1).SendSessionEndAsync("s1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        signalPayloads.Should().HaveCount(2).And.AllSatisfy(p => p.Should().Contain("Bash"));
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
        (await RowCountAsync()).Should().Be(2, "shipped, not purged as dead");
    }

    // A 400 is deterministic: retrying can never succeed, and the oldest group would block the queue.
    [Fact]
    public async Task Pilot_EventsRejectedWith400_GroupIsDead_AndOtherGroupsShipInTheSamePoll()
    {
        await _queue.OpenAsync("bad");
        await _queue.OpenAsync("good");
        _api.SendSessionEventsAsync("bad", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Rejected);
        _api.SendSessionEventsAsync("good", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        await EnqueueAsync("b1", "bad", EventType.ToolExecution, ageMinutes: 3);
        await EnqueueAsync("g1", "good", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)));

        // Each POST stands alone: the tool execution's and the activity signals' are both rejected.
        await _api.Received(2).SendSessionEventsAsync("bad", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received().SendSessionEventsAsync("good", Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeadEvents_PurgeWithShipped()
    {
        await EnqueueAsync("dead", "s1", EventType.ToolExecution, ageMinutes: 2 * 24 * 60);
        await _queue.MarkDeadAsync(["dead"]);

        await _queue.PurgeUnshippedAsync(TimeSpan.FromDays(1));
        (await RowCountAsync()).Should().Be(1, "a dead event is not an unshipped one");

        await _queue.PurgeShippedAsync(TimeSpan.FromHours(24));
        (await RowCountAsync()).Should().Be(0, "the dead event is dropped like a shipped one");
    }

    private string DbPath => Path.Combine(_dir, "queue.db");

    private string ConfigPath => Path.Combine(_dir, "config.json");

    private QueueProcessor CreateSut(AgentMode mode, IAgentModeSource modeSource)
    {
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var config = new AgentConfig
        {
            Mode = mode,
            PollIntervalSeconds = 1,
            BatchSize = 50,
            OverwatchDir = _dir
        };
        return new QueueProcessor(
            _queue, _api, redactor, Substitute.For<IConversationCapture>(),
            Substitute.For<IConversationCursorStore>(), Substitute.For<IGitReader>(), Substitute.For<IUsageStore>(),
            config, modeSource, _lifetime, Substitute.For<ILogger<QueueProcessor>>());
    }

    private static async Task RunAsync(QueueProcessor sut, int millis = 1500)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.StartAsync(cts.Token);
        await Task.Delay(millis);
        await sut.StopAsync(CancellationToken.None);
    }

    private Task EnqueueAsync(string id, string sessionId, EventType type, string payload = "{}", int ageMinutes = 1) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = id,
            SessionId = sessionId,
            EventType = type,
            PayloadJson = payload,
            CreatedAt = DateTime.UtcNow.AddMinutes(-ageMinutes)
        });

    private async Task<long> RowCountAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DbPath}");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
