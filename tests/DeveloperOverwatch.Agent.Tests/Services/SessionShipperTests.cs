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

/// <summary>How one session's queued events reach the server, one poll at a time over a real queue: what is
/// sent, in what order, and what is left pending when the server does not settle an item.</summary>
public sealed class SessionShipperTests : IDisposable
{
    private const string Session = "s";
    private static readonly DateTime T0 = DateTime.UtcNow.AddHours(-1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-shipper-{Guid.NewGuid():N}");
    private readonly EventQueue _queue;
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly ConcurrentQueue<string> _sent = new();

    public SessionShipperTests()
    {
        Directory.CreateDirectory(_dir);
        _queue = new EventQueue(Path.Combine(_dir, "queue.db"));
        _queue.InitializeAsync().GetAwaiter().GetResult();
        _api.SendSessionStartAsync(Arg.Do<string>(p => _sent.Enqueue("start:" + Read(p, "eventId"))), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success);
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Do<string>(p => _sent.Enqueue("end:" + Read(p, "eventId"))), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Do<string>(p => _sent.Enqueue("events:" + p)), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success);
    }

    public void Dispose()
    {
        _queue.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // A 200 whose results leave an item out settles nothing for it. That is a retry, not a quiet skip:
    // the item counts a retry, the poll backs off, and nothing after it (here the end) is sent.
    [Theory]
    [InlineData(EventType.ToolExecution)]
    [InlineData(EventType.ToolFailed)]
    public async Task ItemTheServerDidNotAnswer_IsRetried_WithBackoff_AndTheEndWaits(EventType type)
    {
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApiResult(PostStatus.Ok, new ItemResults(new HashSet<string>(), new HashSet<string>(), new Dictionary<string, string>())));
        var item = type == EventType.ToolExecution ? Event("t1", type, ToolExecution("te-1")) : Event("t1", type, ToolFailed());
        await EnqueueAsync(item, T0);
        await EnqueueAsync(Event("end", EventType.SessionEnd), T0.AddMinutes(1));

        var result = await CreateSut().PollOnceAsync(CancellationToken.None);

        result.Should().Be(QueueProcessor.PollResult.BackOff);
        _sent.Should().NotContain(s => s.StartsWith("end:"));
        var pending = await _queue.DequeueBatchAsync(10);
        pending.Select(e => e.Id).Should().Equal("t1", "end");
        pending.Single(e => e.Id == "t1").RetryCount.Should().Be(1);
    }

    private QueueProcessor CreateSut()
    {
        var config = new AgentConfig
        {
            Mode = AgentMode.Pilot,
            OverwatchDir = Path.Combine(_dir, "no-fallback-file-here"),
            BatchSize = 50
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

    private static QueuedEvent Event(string id, EventType type, string payload = "{}") =>
        new() { Id = id, SessionId = Session, EventType = type, PayloadJson = payload };

    private static string ToolFailed() => $$"""{"toolName":"Bash","at":"{{T0:o}}"}""";

    private static string ToolExecution(string itemId) => JsonSerializer.Serialize(new
    {
        sessionId = Session,
        toolExecutions = new[] { new { id = itemId, toolName = "Read", timestamp = T0 } },
        fileChanges = Array.Empty<object>(),
        conversationTurns = Array.Empty<object>()
    });

    private static string? Read(string json, string property)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
