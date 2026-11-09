using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>What one delivery attempt settled for a queued event.</summary>
internal enum EventOutcome
{
    /// <summary>Every item the event carries was accepted or was a duplicate: mark it shipped.</summary>
    Done,

    /// <summary>The server rejected it deterministically: mark it dead.</summary>
    Dead,

    /// <summary>Network, timeout, server fault, or a 200 whose results left one of its items out: leave it
    /// pending, hold the session's remaining items for this poll, and back off.</summary>
    Transient,

    /// <summary>The server does not know the session and never will: the session's pending items are dead.</summary>
    SessionGone
}

/// <summary>What one send settled.</summary>
/// <param name="Whole">Transient or session-gone when the send stopped before every event had an answer.</param>
/// <param name="Each">Each event's own outcome; events the send never reached are transient.</param>
internal sealed record SettledPost(EventOutcome Whole, IReadOnlyList<(QueuedEvent Event, EventOutcome Outcome)> Each);
