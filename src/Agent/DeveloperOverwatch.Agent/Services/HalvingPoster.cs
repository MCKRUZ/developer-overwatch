namespace DeveloperOverwatch.Agent.Services;

/// <summary>How a halving send ended.</summary>
internal enum HalvingStop
{
    /// <summary>Every unit was answered (accepted, duplicate or rejected) through the callbacks.</summary>
    Completed,

    /// <summary>A transient failure, or one unit too large even alone: the units not yet answered wait for
    /// the next poll.</summary>
    Transient,

    /// <summary>The server does not know the session and never will.</summary>
    SessionGone
}

/// <summary>Sends units in one request. A 400 or 413 is the server's verdict on the request, not on each
/// unit in it, so a refused request of several units is split in half and each half sent again, the first
/// fully answered before the second goes. One unit refused alone is the unit's own verdict: a 400 rejects
/// it; a 413 holds it, since it can never be split further, so it waits for the next poll and is never
/// dead. A transient failure or session_not_found ends the send there.</summary>
internal static class HalvingPoster
{
    /// <param name="Send">Posts a request carrying exactly these units.</param>
    /// <param name="OnAnswered">A 200 for these units; the result holds each one's outcome.</param>
    /// <param name="OnRejected">A 400 for this unit alone.</param>
    /// <param name="OnTooLarge">A 413 for this unit alone, once per send.</param>
    public sealed record Callbacks<T>(
        Func<IReadOnlyList<T>, Task<ApiResult>> Send,
        Func<IReadOnlyList<T>, ApiResult, Task> OnAnswered,
        Func<T, ApiResult, Task> OnRejected,
        Action<T> OnTooLarge);

    /// <param name="units">At least one.</param>
    public static async Task<HalvingStop> PostAsync<T>(IReadOnlyList<T> units, Callbacks<T> callbacks)
    {
        var result = await callbacks.Send(units);
        switch (result.Status)
        {
            case PostStatus.Ok:
                await callbacks.OnAnswered(units, result);
                return HalvingStop.Completed;
            case PostStatus.SessionNotFound:
                return HalvingStop.SessionGone;
            case PostStatus.Rejected or PostStatus.TooLarge when units.Count > 1:
                var half = units.Count / 2;
                var first = await PostAsync(units.Take(half).ToList(), callbacks);
                return first == HalvingStop.Completed ? await PostAsync(units.Skip(half).ToList(), callbacks) : first;
            case PostStatus.Rejected:
                await callbacks.OnRejected(units[0], result);
                return HalvingStop.Completed;
            case PostStatus.TooLarge:
                callbacks.OnTooLarge(units[0]);
                return HalvingStop.Transient;
            default:
                return HalvingStop.Transient;
        }
    }

    public static EventOutcome ToOutcome(this HalvingStop stop, EventOutcome whenCompleted) => stop switch
    {
        HalvingStop.Transient => EventOutcome.Transient,
        HalvingStop.SessionGone => EventOutcome.SessionGone,
        _ => whenCompleted
    };
}
