using System.Text.Json;
using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;
using static DeveloperOverwatch.Agent.Tests.Services.ContextLines;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Runs the processor over a real queue and transcript, so the Local-mode test sees that a session whose
/// snapshot Pilot would send sends nothing at all.</summary>
public sealed class QueueProcessorContextSnapshotTests : IDisposable
{
    private const string SessionId = "s1";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-qp-context-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly UsageStore _usage;
    private readonly IApiClient _api = Substitute.For<IApiClient>();

    public QueueProcessorContextSnapshotTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "queue.db");
        _queue = new EventQueue(dbPath);
        _usage = new UsageStore(dbPath);
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
    }

    public void Dispose()
    {
        _queue.Dispose();
        _usage.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task PilotMode_SendsTheSnapshot()
    {
        await SeedSessionAsync();

        await RunUntilAsync(CreateSut(AgentMode.Pilot), () => Task.FromResult(EventBodies().Count > 0));

        // The cursor store is a stand-in that keeps nothing, so later polls may send the turn again.
        var body = EventBodies()[0];
        body["contextSnapshot"]!["measuredContextTokens"]!.GetValue<long>().Should().Be(1000);
    }

    [Fact]
    public async Task LocalMode_TheSnapshotNeverReachesTheServer()
    {
        await SeedSessionAsync();

        await RunUntilAsync(CreateSut(AgentMode.Local), async () => await _queue.GetUnshippedCountAsync() == 0);

        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "the marker was handled");
        _api.ReceivedCalls().Should().BeEmpty();
    }

    private List<JsonNode> EventBodies() =>
        _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IApiClient.SendSessionEventsAsync))
            .Select(c => JsonNode.Parse((string)c.GetArguments()[1]!)!)
            .ToList();

    private QueueProcessor CreateSut(AgentMode mode) =>
        new(
            _queue,
            _api,
            new PrivacyRedactor(new AgentConfig().RedactionPatterns),
            new ConversationCapture(Substitute.For<ILogger<ConversationCapture>>()),
            Substitute.For<IConversationCursorStore>(),
            Substitute.For<IGitReader>(),
            _usage,
            new AgentConfig { Mode = mode, OverwatchDir = _dir, PollIntervalSeconds = 1, BatchSize = 500 },
            QueueProcessorLifecycleTests.Fixed(mode),
            Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            Substitute.For<ILogger<QueueProcessor>>());

    /// <summary>A SessionStart in the queue, and the Stop hook's marker in the fallback file, pointing at a transcript
    /// with a current shape and a measured response.</summary>
    private async Task SeedSessionAsync()
    {
        await _queue.EnqueueAsync(new QueuedEvent
        {
            Id = "start-1", SessionId = SessionId, EventType = EventType.SessionStart,
            PayloadJson = """{"repoPath":"C:/repos/widget"}""", CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });

        var transcriptPath = Path.Combine(_dir, "session.jsonl");
        var ts = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        File.WriteAllLines(transcriptPath,
            [Shape("h1", ["sys"]), ApiRequest("h1", ts), Assistant("msg_1", 10, 990, ts: ts)]);

        var marker = JsonSerializer.Serialize(new
        {
            id = "turn-1",
            sessionId = SessionId,
            eventType = nameof(EventType.ConversationTurn),
            payload = JsonSerializer.Serialize(new { transcriptPath }),
            createdAt = DateTime.UtcNow.AddMinutes(-1).ToString("o")
        });
        File.WriteAllText(Path.Combine(_dir, "queue-fallback.jsonl"), marker + "\n");
    }

    private async Task RunUntilAsync(QueueProcessor sut, Func<Task<bool>> done)
    {
        await sut.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline
            && (Directory.EnumerateFiles(_dir, "queue-fallback*").Any() || !await done()))
            await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);
    }
}
