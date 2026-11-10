using System.Net;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Runs the processor over a real queue, usage store and transcript parser, so the
/// tests see what actually reaches the network and what actually lands in queue.db.</summary>
public sealed class QueueProcessorUsageTests : IDisposable
{
    private const string SessionId = "s1";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-qp-usage-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly UsageStore _usage;
    private readonly IGitReader _gitRemote = Substitute.For<IGitReader>();

    public QueueProcessorUsageTests()
    {
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "queue.db");
        _queue = new EventQueue(dbPath);
        _usage = new UsageStore(dbPath);
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _gitRemote.GetOriginUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://github.com/Acme/Widget.git");
    }

    public void Dispose()
    {
        _queue.Dispose();
        _usage.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task LocalMode_SendsNothing_AndRecordsUsageLocally()
    {
        var handler = new CountingHandler();
        var api = new ApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://overwatch.invalid/") },
            Substitute.For<ILogger<ApiClient>>());
        await SeedSessionAsync();
        for (var i = 0; i < 150; i++)
            await EnqueueAsync($"tool-{i:D3}", "other-session", EventType.ToolExecution, "{}", DateTime.UtcNow.AddMinutes(-2));

        var sut = CreateSut(AgentMode.Local, api);
        await RunUntilAsync(sut, async () => await _queue.GetUnshippedCountAsync() == 0);

        handler.Calls.Should().Be(0);
        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "local mode marks events handled once recorded");
        File.Exists(Path.Combine(_dir, "queue-fallback.jsonl")).Should().BeFalse("the hook fallback file is still imported");
        await AssertUsageRecordedAsync();
    }

    // In the pilot, a failed send must not lose the machine's own record of the usage.
    [Fact]
    public async Task PilotMode_SendFails_StillRecordsUsageLocally()
    {
        var api = Substitute.For<IApiClient>();
        api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Success);
        api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ApiResult.Failed);
        await SeedSessionAsync();

        var sut = CreateSut(AgentMode.Pilot, api);
        await RunUntilAsync(sut, async () => (await UsageNowAsync()).Count > 0);

        await AssertUsageRecordedAsync();
        // The SessionStart ships on the first poll, while the fallback marker is still settling after its claim.
        (await _queue.GetUnshippedCountAsync()).Should().Be(1, "the marker whose send failed stays queued for retry");
    }

    private QueueProcessor CreateSut(AgentMode mode, IApiClient api) =>
        new(
            _queue,
            api,
            new PrivacyRedactor(new AgentConfig().RedactionPatterns),
            new ConversationCapture(Substitute.For<ILogger<ConversationCapture>>()),
            Substitute.For<IConversationCursorStore>(),
            _gitRemote,
            _usage,
            new AgentConfig
            {
                Mode = mode,
                OverwatchDir = _dir,
                PollIntervalSeconds = 1,
                BatchSize = 500
            },
            QueueProcessorLifecycleTests.Fixed(mode),
            Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(),
            Substitute.For<ILogger<QueueProcessor>>());

    /// <summary>A SessionStart in the queue, and the Stop hook's ConversationTurn marker in the
    /// jsonl fallback file — where hooks really write it — pointing at a one-response transcript.</summary>
    private async Task SeedSessionAsync()
    {
        await EnqueueAsync("start-1", SessionId, EventType.SessionStart,
            """{"repoPath":"C:/repos/widget"}""", DateTime.UtcNow.AddMinutes(-3));

        var transcriptPath = Path.Combine(_dir, "session.jsonl");
        var ts = DateTime.UtcNow.AddMinutes(-1).ToString("o");
        File.WriteAllText(transcriptPath,
            $$$"""{"type":"assistant","uuid":"u-1","message":{"id":"msg_1","role":"assistant","model":"claude-opus-5-5","content":[{"type":"text","text":"hello"}],"usage":{"input_tokens":10,"output_tokens":20,"cache_creation_input_tokens":30,"cache_read_input_tokens":40}},"timestamp":"{{{ts}}}"}""" + "\n");

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

    private async Task AssertUsageRecordedAsync()
    {
        var row = (await UsageNowAsync()).Should().ContainSingle().Subject;
        row.SessionId.Should().Be(SessionId);
        row.Model.Should().Be("claude-opus-5-5");
        row.InputTokens.Should().Be(10);
        row.OutputTokens.Should().Be(20);
        row.CacheCreationTokens.Should().Be(30);
        row.CacheReadTokens.Should().Be(40);
        row.ProjectKey.Should().Be("github.com/acme/widget");
    }

    private Task<IReadOnlyList<UsageRecord>> UsageNowAsync() =>
        _usage.GetUsageAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

    private Task EnqueueAsync(string id, string sessionId, EventType type, string payload, DateTime createdAt) =>
        _queue.EnqueueAsync(new QueuedEvent
        {
            Id = id,
            SessionId = sessionId,
            EventType = type,
            PayloadJson = payload,
            CreatedAt = createdAt
        });

    /// <summary>Also waits until the hook fallback file is imported, which takes more than one poll: a
    /// claimed file waits <see cref="FallbackEventImporter.ClaimSettleTime"/> before it is read.</summary>
    private async Task RunUntilAsync(QueueProcessor sut, Func<Task<bool>> done)
    {
        await sut.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline
            && (Directory.EnumerateFiles(_dir, "queue-fallback*").Any() || !await done()))
            await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
