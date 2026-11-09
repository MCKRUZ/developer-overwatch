using DeveloperOverwatch.Agent.Models;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Delivery is at-least-once, so the server must store every event type once however often it
/// arrives. The switch has no default arm: a new <see cref="EventType"/> fails to compile (CS8509, an
/// error under TreatWarningsAsErrors) until someone states the key the server dedupes it by.</summary>
public sealed class EventTypeIdempotencyKeyTests
{
#pragma warning disable CS8524 // Unnamed values such as (EventType)99 are never queued.
    private static string ServerIdempotencyKey(EventType type) => type switch
    {
        EventType.SessionStart => "the start's event id, recorded in ProcessedAgentEvents",
        EventType.ToolExecution => "ToolExecution.Id, checked per session",
        EventType.FileChange => "FileChange.Id, checked per session",
        EventType.ConversationTurn => "the turn id (stable hash of SourceId), checked across all sessions",
        EventType.SessionEnd => "an already-ended session makes it a no-op; its event id is recorded in ProcessedAgentEvents",
        EventType.ActivityChanged => "the latest state by raw At wins",
        EventType.ToolFailed => "SessionToolFailure.Id",
    };
#pragma warning restore CS8524

    [Fact]
    public void EveryEventType_NamesTheKeyTheServerDedupesItBy() =>
        Enum.GetValues<EventType>().Should().AllSatisfy(type => ServerIdempotencyKey(type).Should().NotBeNullOrWhiteSpace());
}
