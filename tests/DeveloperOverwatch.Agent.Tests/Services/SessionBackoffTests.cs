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

/// <summary>One session the server keeps failing must not hold up the others: not by filling the batch, and
/// not by its backoff. Real queue, one poll at a time.</summary>
public sealed class SessionBackoffTests : IDisposable
{
    private const int BatchSize = 3;
    private static readonly DateTime T0 = DateTime.UtcNow.AddHours(-1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-backoff-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ConcurrentQueue<string> _sent = new();

    public SessionBackoffTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        _queue.InitializeAsync().GetAwaiter().GetResult();
        // Backoff is not about the content floor: both sessions are open as if a start had shipped.
        _queue.OpenAsync("a").GetAwaiter().GetResult();
        _queue.OpenAsync("b").GetAwaiter().GetResult();
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            _sent.Enqueue("start:" + Property(ci.ArgAt<string>(0), "eventId"));
            return ApiResult.Failed;
        });
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            using var doc = JsonDocument.Parse(ci.ArgAt<string>(1));
            if (doc.RootElement.TryGetProperty("toolExecutions", out var tools) && tools.GetArrayLength() > 0)
                _sent.Enqueue("tool:" + tools[0].GetProperty("id").GetString());
            return ApiResult.Success;
        });
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task StuckSessionWithAFullBatchOfOlderRows_TheOtherSessionShipsInTheSamePoll()
    {
        await EnqueueAsync(Start("a-start", "a"), T0);
        await EnqueueAsync(Tool("a-t1", "a"), T0.AddMinutes(1));
        await EnqueueAsync(Tool("a-t2", "a"), T0.AddMinutes(2));
        await EnqueueAsync(Tool("b-t1", "b"), T0.AddMinutes(10));

        await CreateSut().PollOnceAsync(CancellationToken.None);

        _sent.Should().Equal("start:a-start", "tool:b-t1");
    }

    // At the 5 s poll, the third failure in a row backs a session off 8 s: it sits out one poll. The
    // other session's new rows ship on that very poll, and no poll waits for the stuck session.
    [Fact]
    public async Task StuckSessionSitsOutItsOwnBackoff_WhileTheOtherSessionShipsEveryPoll()
    {
        await EnqueueAsync(Start("a-start", "a"), T0);
        await EnqueueAsync(Tool("b-t1", "b"), T0.AddMinutes(1));
        var sut = CreateSut();
        for (var poll = 0; poll < 3; poll++)
            (await sut.PollOnceAsync(CancellationToken.None)).Should().Be(QueueProcessor.PollResult.Continue);
        _sent.Count(s => s == "start:a-start").Should().Be(3);

        await EnqueueAsync(Tool("b-t2", "b"), T0.AddMinutes(2));
        await sut.PollOnceAsync(CancellationToken.None);

        _sent.Should().EndWith("tool:b-t2");
        _sent.Count(s => s == "start:a-start").Should().Be(3);

        await sut.PollOnceAsync(CancellationToken.None);
        _sent.Count(s => s == "start:a-start").Should().Be(4);
    }

    // A session swept dead or purged never reaches Succeeded, so its entry must not outlive it.
    [Fact]
    public void SessionNotRetriedLongAfterItsBackoff_IsForgotten()
    {
        var backoff = new SessionBackoff(pollIntervalSeconds: 1);
        backoff.Failed("gone");

        for (var poll = 0; poll < 200; poll++)
            backoff.NextPoll();

        backoff.Tracked.Should().Be(0);
    }

    [Fact]
    public void SessionFailingEveryRetry_KeepsEscalating()
    {
        var backoff = new SessionBackoff(pollIntervalSeconds: 1);
        for (var failure = 0; failure < 8; failure++)
        {
            backoff.Failed("stuck");
            while (backoff.NextPoll().Contains("stuck")) { }
        }

        backoff.Failed("stuck");
        Enumerable.Range(0, 59).All(_ => backoff.NextPoll().Contains("stuck")).Should().BeTrue();
    }

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig
        {
            Mode = AgentMode.Pilot,
            OverwatchDir = Path.Combine(_dir, "no-fallback-file-here"),
            BatchSize = BatchSize,
            PollIntervalSeconds = 5
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

    private static QueuedEvent Start(string id, string sessionId) => new()
    {
        Id = id, SessionId = sessionId, EventType = EventType.SessionStart,
        PayloadJson = JsonSerializer.Serialize(new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = T0 })
    };

    private static QueuedEvent Tool(string id, string sessionId) => new()
    {
        Id = id, SessionId = sessionId, EventType = EventType.ToolExecution,
        PayloadJson = JsonSerializer.Serialize(new
        {
            sessionId,
            toolExecutions = new[] { new { id, toolName = "Read", timestamp = T0 } },
            fileChanges = Array.Empty<object>(),
            conversationTurns = Array.Empty<object>()
        })
    };

    private static string? Property(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }
}
