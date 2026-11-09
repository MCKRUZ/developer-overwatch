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
