using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class QueueProcessorTests
{
    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitFallbackWriter _gitFallback;
    private readonly AgentConfig _config;
    private readonly QueueProcessor _sut;

    public QueueProcessorTests()
    {
        _queue = Substitute.For<IEventQueue>();
        _api = Substitute.For<IApiClient>();
        _redactor = Substitute.For<IPrivacyRedactor>();
        _gitFallback = Substitute.For<IGitFallbackWriter>();

        _redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

        _config = new AgentConfig
        {
            PollIntervalSeconds = 1,
            BatchSize = 50,
            GitFallbackThreshold = 100
        };

        var logger = Substitute.For<ILogger<QueueProcessor>>();
        _sut = new QueueProcessor(_queue, _api, _redactor, _gitFallback, _config, logger);
    }

    private static QueuedEvent CreateEvent(
        string sessionId = "session-1",
        EventType type = EventType.ToolExecution,
        string payload = "{\"tool\":\"read\"}") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = DateTime.UtcNow
    };

    private async Task RunOneTickAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await _sut.StartAsync(cts.Token);
        await Task.Delay(1500);
        await _sut.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ProcessBatch_ShipsEvents_MarksAsShipped()
    {
        var evt = CreateEvent();
        var batch = new List<QueuedEvent> { evt };
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(batch, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionEventsAsync(
            evt.SessionId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _queue.Received(1).MarkShippedAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Contains(evt.Id)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_EmptyQueue_DoesNothing()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _api.DidNotReceive().SendSessionStartAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEventsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEndAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().MarkShippedAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_ApiFailure_IncrementsRetry()
    {
        var evt = CreateEvent();
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _queue.GetUnshippedCountAsync(Arg.Any<CancellationToken>())
            .Returns(1);

        await RunOneTickAsync();

        await _queue.Received(1).IncrementRetryAsync(evt.Id, Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().MarkShippedAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_RedactsBeforeShipping()
    {
        var rawPayload = "{\"secret\":\"sk-1234567890abcdef\"}";
        var redactedPayload = "{\"secret\":\"[REDACTED]\"}";
        var evt = CreateEvent(payload: rawPayload);

        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _redactor.Redact(rawPayload).Returns(redactedPayload);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        _redactor.Received(1).Redact(rawPayload);
        await _api.Received(1).SendSessionEventsAsync(
            evt.SessionId,
            redactedPayload,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SessionStartEvent_CallsSendSessionStart()
    {
        var evt = CreateEvent(type: EventType.SessionStart, payload: "{\"init\":true}");
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionStartAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionStartAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.DidNotReceive().SendSessionEventsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_SessionEndEvent_CallsSendSessionEnd()
    {
        var evt = CreateEvent(type: EventType.SessionEnd, payload: "{\"end\":true}");
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt }, []);
        _api.SendSessionEndAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received(1).SendSessionEndAsync(
            evt.SessionId,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_MultipleSessionsInBatch_GroupsCorrectly()
    {
        var evt1 = CreateEvent(sessionId: "session-A");
        var evt2 = CreateEvent(sessionId: "session-B");
        var evt3 = CreateEvent(sessionId: "session-A");

        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent> { evt1, evt2, evt3 }, []);
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        await RunOneTickAsync();

        await _api.Received().SendSessionEventsAsync(
            "session-A", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _api.Received().SendSessionEventsAsync(
            "session-B", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_InitializesQueueOnStart()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _queue.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessBatch_PurgesOldEventsAfterBatch()
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<QueuedEvent>());

        await RunOneTickAsync();

        await _queue.Received().PurgeShippedAsync(Arg.Any<TimeSpan>());
    }
}
