using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Sends the activity states and tool failures gathered since the last signals POST. The units are
/// the contributing events: ActivityChanged and ToolFailed, plus the tool and turn events activity is derived
/// from. A refused request is split by contributor (see <see cref="HalvingPoster"/>). Only signal events are
/// settled here; a derived activity's event has its own outcome, so rejecting that activity marks nothing.
/// The payload holds only states, tool names, ids and times: nothing to redact.</summary>
internal sealed class SignalShipper(IApiClient api, ILogger logger)
{
    /// <summary>A contributor dated below the session's floor is left out; a signal event among them is Done.</summary>
    public async Task<SettledPost> FlushAsync(OpenSession session, IReadOnlyList<QueuedEvent> contributors, CancellationToken ct)
    {
        var sessionId = session.SessionId;
        var units = contributors
            .Where(e => session.Admits(e.CreatedAt) && SessionSignals.BuildPayload(sessionId, [e]) is not null)
            .ToList();
        // A signal event that carries nothing to send, or may not send it, has nothing to wait for.
        var outcomes = contributors
            .Where(e => SessionSignals.IsSignalEvent(e.EventType) && !units.Contains(e))
            .ToDictionary(e => e, _ => EventOutcome.Done);

        var stop = units.Count == 0 ? HalvingStop.Completed : await HalvingPoster.PostAsync(units, new HalvingPoster.Callbacks<QueuedEvent>(
            subset => api.SendSessionEventsAsync(sessionId, SessionSignals.BuildPayload(sessionId, subset)!, ct),
            (subset, result) =>
            {
                foreach (var evt in subset.Where(e => SessionSignals.IsSignalEvent(e.EventType)))
                    outcomes[evt] = Answered(sessionId, evt, result);
                return Task.CompletedTask;
            },
            (evt, result) =>
            {
                Rejected(sessionId, evt, result.Reason, outcomes);
                return Task.CompletedTask;
            },
            evt => logger.LogWarning(
                "Server refused the signals of event {EventId} of session {SessionId} as too large even alone; holding them for the next poll",
                evt.Id, sessionId)));

        var each = contributors
            .Where(e => SessionSignals.IsSignalEvent(e.EventType))
            .Select(e => (e, outcomes.GetValueOrDefault(e, EventOutcome.Transient)))
            .ToList();
        return new SettledPost(stop.ToOutcome(EventOutcome.Done), each);
    }

    /// <summary>Activity carries no id and is accepted with the POST; a tool failure has its own outcome.</summary>
    private EventOutcome Answered(string sessionId, QueuedEvent evt, ApiResult result)
    {
        if (evt.EventType != EventType.ToolFailed)
            return EventOutcome.Done;

        switch (result.OutcomeOf(evt.Id))
        {
            case ItemOutcome.Accepted or ItemOutcome.Duplicate:
                return EventOutcome.Done;
            case ItemOutcome.Rejected:
                logger.LogWarning("Server rejected item {ItemId} of session {SessionId}; dropping it: {Reason}",
                    evt.Id, sessionId, result.RejectionReason(evt.Id));
                return EventOutcome.Dead;
            default:
                logger.LogWarning("Server's results left out items {ItemIds} of session {SessionId}; retrying them",
                    evt.Id, sessionId);
                return EventOutcome.Transient;
        }
    }

    private void Rejected(string sessionId, QueuedEvent evt, string? reason, Dictionary<QueuedEvent, EventOutcome> outcomes)
    {
        if (!SessionSignals.IsSignalEvent(evt.EventType))
        {
            logger.LogWarning("Server rejected the activity derived from event {EventId} of session {SessionId}: {Reason}",
                evt.Id, sessionId, reason);
            return;
        }

        logger.LogWarning("Server rejected signal event {EventId} of session {SessionId}; dropping it: {Reason}",
            evt.Id, sessionId, reason);
        outcomes[evt] = EventOutcome.Dead;
    }
}
