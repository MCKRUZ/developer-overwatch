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

    // A machine switched to Local must stop shipping now, not at the next logon. An unreadable config
    // fails closed to Local.
    [Theory]
    [InlineData("""{"mode":"Local"}""")]
    [InlineData("{ this is not json")]
    public async Task Pilot_ConfigNowLocalOrUnreadable_ShipsNothingAndStops(string configJson)
    {
        File.WriteAllText(ConfigPath, configJson);
        await EnqueueAsync("e1", "s1", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, new ConfigFileModeSource(ConfigPath)));

        _api.ReceivedCalls().Should().BeEmpty();
        _lifetime.Received(1).StopApplication();
        (await _queue.GetUnshippedCountAsync()).Should().Be(1, "nothing was processed");
    }

    [Fact]
    public async Task Pilot_ModeSourceThrows_IsTreatedAsLocal()
    {
        var source = Substitute.For<IAgentModeSource>();
        source.CurrentMode().Returns(_ => throw new IOException("config.json is locked"));
        await EnqueueAsync("e1", "s1", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, source));

        _api.ReceivedCalls().Should().BeEmpty();
        _lifetime.Received(1).StopApplication();
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
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Failed);
        await EnqueueAsync("a1", "a", EventType.ToolExecution, ageMinutes: 2);
        await EnqueueAsync("b1", "b", EventType.ToolExecution);

        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)));

        await _api.Received(1).SendSessionEventsAsync("a", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received(1).SendSessionEventsAsync("b", Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _queue.GetUnshippedCountAsync()).Should().Be(2, "failed events are retried later");
    }

    // Local holds session starts so that, once the machine is enrolled, the server learns of each session
    // before any of its later events, which would otherwise 404.
    [Fact]
    public async Task Local_HoldsSessionStarts_AndThePilotAgentShipsThemOnStart()
    {
        await EnqueueAsync("start", "s1", EventType.SessionStart, """{"repoPath":"C:/repos/widget"}""", ageMinutes: 3);
        await EnqueueAsync("tool", "s1", EventType.ToolExecution, ageMinutes: 2);

        await RunAsync(CreateSut(AgentMode.Local, Fixed(AgentMode.Local)));

        _api.ReceivedCalls().Should().BeEmpty();
        (await _queue.DequeueBatchAsync(100)).Should().BeEmpty("held starts must never fill local mode's batches");
        (await RowCountAsync()).Should().Be(2);

        var shippedStarts = new List<string>();
        _api.SendSessionStartAsync(Arg.Do<string>(shippedStarts.Add), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        await RunAsync(CreateSut(AgentMode.Pilot, Fixed(AgentMode.Pilot)));

        shippedStarts.Should().ContainSingle().Which.Should().Contain("C:/repos/widget");
        await _api.DidNotReceiveWithAnyArgs().SendSessionEventsAsync(default!, default!, default);
    }

    [Fact]
    public async Task DeadEvents_PurgeWithShipped_HeldEvents_PurgeWithUnshipped()
    {
        await EnqueueAsync("dead", "s1", EventType.ToolExecution, ageMinutes: 2 * 24 * 60);
        await EnqueueAsync("held", "s1", EventType.SessionStart, ageMinutes: 2 * 24 * 60);
        await _queue.MarkDeadAsync(["dead"]);
        await _queue.MarkHeldAsync(["held"]);

        await _queue.PurgeShippedAsync(TimeSpan.FromHours(24));
        (await RowCountAsync()).Should().Be(1, "the dead event is dropped like a shipped one");

        await _queue.PurgeUnshippedAsync(TimeSpan.FromDays(1));
        (await RowCountAsync()).Should().Be(0, "a held event is bounded like any unshipped one");
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
            GitFallbackThreshold = 100,
            OverwatchDir = _dir
        };
        return new QueueProcessor(
            _queue, _api, redactor, Substitute.For<IGitFallbackWriter>(), Substitute.For<IConversationCapture>(),
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
