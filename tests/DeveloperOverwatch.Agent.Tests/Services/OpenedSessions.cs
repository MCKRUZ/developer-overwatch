using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>For suites whose tests are not about the content floor: opens a session as an accepted start
/// would, so its items ship without the test queueing that start.</summary>
internal static class OpenedSessions
{
    /// <param name="from">The session's open_from; by default it has no floor beyond enrolment.</param>
    public static async Task OpenAsync(this IEventQueue queue, string sessionId, DateTime? from = null)
    {
        var opening = new QueuedEvent
        {
            Id = $"opened-{sessionId}", SessionId = sessionId, EventType = EventType.SessionStart, PayloadJson = "{}"
        };
        await queue.MarkOpenedAsync(opening, from ?? DateTime.MinValue);
    }
}
