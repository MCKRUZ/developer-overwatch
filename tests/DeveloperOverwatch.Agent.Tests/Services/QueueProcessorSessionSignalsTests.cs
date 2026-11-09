using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Activity states and failed tool calls ride the <c>/events</c> payload. Tool failures
/// have no ids, so the server counts every one it receives: each must be sent exactly once.</summary>
public sealed class QueueProcessorSessionSignalsTests
{
    private const string SessionId = "ow-1";
    private static readonly DateTime T1 = new(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = T1.AddMinutes(1);

    private readonly IEventQueue _queue = Substitute.For<IEventQueue>();
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly List<string> _sentPayloads = [];

    public QueueProcessorSessionSignalsTests()
    {
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { _sentPayloads.Add(ci.ArgAt<string>(1)); return ApiResult.Success; });
    }

    private QueueProcessor CreateSut(AgentMode mode)
    {
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var config = new AgentConfig
        {
            Mode = mode,
            PollIntervalSeconds = 1,
            BatchSize = 50,
            OverwatchDir = Path.Combine(Path.GetTempPath(), $"ow-sig-{Guid.NewGuid():N}")
        };
        return new QueueProcessor(
            _queue, _api, redactor, Substitute.For<IConversationCapture>(),
            Substitute.For<IConversationCursorStore>(), Substitute.For<IGitReader>(),
            Substitute.For<IUsageStore>(), config, QueueProcessorLifecycleTests.Fixed(mode),
            Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(), Substitute.For<ILogger<QueueProcessor>>());
    }

    private static QueuedEvent Event(EventType type, string payload, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = SessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = createdAt
    };

    private async Task RunOneTickAsync(AgentMode mode, params QueuedEvent[] events)
    {
        _queue.DequeueBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(events.ToList(), []);
        var sut = CreateSut(mode);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await sut.StartAsync(cts.Token);
        await Task.Delay(1500);
        await sut.StopAsync(CancellationToken.None);
    }

    private JsonElement SignalsPayload() =>
        JsonDocument.Parse(_sentPayloads.Single(p => p.Contains("activityEvents"))).RootElement;

    private static IEnumerable<(string State, DateTime At)> Activity(JsonElement payload) =>
        payload.GetProperty("activityEvents").EnumerateArray()
            .Select(a => (a.GetProperty("state").GetString()!, a.GetProperty("at").GetDateTime().ToUniversalTime()));

    [Fact]
    public async Task Pilot_ActivityChangedAndToolFailed_MappedIntoOnePayload()
    {
        var longName = new string('x', 300);
        var events = new[]
        {
            Event(EventType.ActivityChanged, $"{{\"state\":\"waiting\",\"at\":\"{T1:o}\"}}", T2),
            Event(EventType.ActivityChanged, $"{{\"state\":\"sleeping\",\"at\":\"{T1:o}\"}}", T2),
            Event(EventType.ToolFailed, $"{{\"toolName\":\"Bash\",\"at\":\"{T2:o}\"}}", T2),
            Event(EventType.ToolFailed, $"{{\"toolName\":\"{longName}\",\"at\":\"{T2:o}\"}}", T2)
        };

        await RunOneTickAsync(AgentMode.Pilot, events);

        _sentPayloads.Should().ContainSingle("signal events are never shipped one by one");
        var payload = SignalsPayload();
        payload.GetProperty("sessionId").GetString().Should().Be(SessionId);
        Activity(payload).Should().Equal(("waiting", T1));
        var failures = payload.GetProperty("toolFailures").EnumerateArray().ToList();
        failures.Select(f => f.GetProperty("toolName").GetString()).Should().Equal("Bash", new string('x', 255));
        failures[0].GetProperty("at").GetDateTime().ToUniversalTime().Should().Be(T2);
        await _queue.Received(1).MarkShippedAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Count == events.Length), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pilot_ToolExecutionMeansActive_StopMarkerMeansIdle()
    {
        await RunOneTickAsync(AgentMode.Pilot,
            Event(EventType.ToolExecution, "{\"toolExecutions\":[]}", T1),
            // A marker naming no transcript captures nothing, but still says the turn ended.
            Event(EventType.ConversationTurn, "{}", T2));

        Activity(SignalsPayload()).Should().Equal(("active", T1), ("idle", T2));
        SignalsPayload().GetProperty("toolFailures").GetArrayLength().Should().Be(0);
    }

    // The server rejects a tool failure without an id, and uses it to drop a resent failure.
    [Fact]
    public async Task Pilot_ToolFailed_CarriesTheQueuedEventId()
    {
        var failed = Event(EventType.ToolFailed, $"{{\"toolName\":\"Bash\",\"at\":\"{T1:o}\"}}", T1);

        await RunOneTickAsync(AgentMode.Pilot, failed);

        SignalsPayload().GetProperty("toolFailures")[0].GetProperty("id").GetString().Should().Be(failed.Id);
    }

    [Fact]
    public async Task Pilot_StopMarkerCarryingAt_IsIdleAtThatTime()
    {
        // Backfill queues its marker long after the session ended, and dates it with "at".
        await RunOneTickAsync(AgentMode.Pilot, Event(EventType.ConversationTurn, $"{{\"at\":\"{T1:o}\"}}", T2));

        Activity(SignalsPayload()).Should().Equal(("idle", T1));
    }

    [Fact]
    public async Task Pilot_EarlierEventInGroupFails_ToolFailuresNotSentAndNothingShipped()
    {
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                _sentPayloads.Add(ci.ArgAt<string>(1));
                return ci.ArgAt<string>(1).Contains("\"tool\"") ? ApiResult.Failed : ApiResult.Success;
            });

        await RunOneTickAsync(AgentMode.Pilot,
            Event(EventType.ToolExecution, "{\"tool\":\"read\"}", T1),
            Event(EventType.ToolFailed, $"{{\"toolName\":\"Bash\",\"at\":\"{T2:o}\"}}", T2));

        _sentPayloads.Should().NotContain(p => p.Contains("toolFailures"),
            "a transient failure holds the rest of the session until the next poll");
        await _queue.DidNotReceive().MarkShippedAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Local_SignalEvents_SendNothingAndAreMarkedHandled()
    {
        var events = new[]
        {
            Event(EventType.ActivityChanged, $"{{\"state\":\"active\",\"at\":\"{T1:o}\"}}", T1),
            Event(EventType.ToolFailed, $"{{\"toolName\":\"Bash\",\"at\":\"{T2:o}\"}}", T2)
        };

        await RunOneTickAsync(AgentMode.Local, events);

        _api.ReceivedCalls().Should().BeEmpty();
        await _queue.Received(1).MarkShippedAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
    }
}
