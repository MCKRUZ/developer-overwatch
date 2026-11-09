using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Delivers one session's queued events and marks each by its own outcome. The start goes first,
/// and nothing else ships until it is definitive. The end goes last, once everything else from this poll
/// is definitive. The first transient failure holds the rest of the session until the next poll.</summary>
internal sealed class SessionShipper
{
    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitReader _gitRemote;
    private readonly IUsageStore _usageStore;
    private readonly ConversationShipper _conversations;
    private readonly ILogger _logger;

    public SessionShipper(
        IEventQueue queue, IApiClient api, IPrivacyRedactor redactor, IGitReader gitRemote,
        IUsageStore usageStore, ConversationShipper conversations, ILogger logger)
    {
        _queue = queue;
        _api = api;
        _redactor = redactor;
        _gitRemote = gitRemote;
        _usageStore = usageStore;
        _conversations = conversations;
        _logger = logger;
    }

    /// <summary>Sends nothing anywhere. The session's project and its usage are kept in queue.db, and every
    /// event is marked shipped, meaning handled, so the queue drains.</summary>
    public async Task HandleLocallyAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            if (evt.EventType == EventType.SessionStart)
                await RecordSessionProjectAsync(sessionId, evt, ct);
            else if (evt.EventType == EventType.ConversationTurn)
                await _conversations.RecordLocallyAsync(sessionId, evt, ct);
        }

        await _queue.MarkShippedAsync(events.Select(e => e.Id).ToList(), ct);
    }

    /// <returns>True when something was transient, so the caller backs off.</returns>
    public async Task<bool> ShipAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        var run = new SessionRun(_queue, events);
        await ShipInOrderAsync(sessionId, events, run, ct);

        if (run.Transient)
        {
            foreach (var evt in run.Open())
                await _queue.IncrementRetryAsync(evt.Id, ct);
        }

        return run.Transient;
    }

    private async Task ShipInOrderAsync(string sessionId, IReadOnlyList<QueuedEvent> events, SessionRun run, CancellationToken ct)
    {
        if (!await ShipStartsAsync(sessionId, OfType(events, EventType.SessionStart), run, ct))
            return;

        foreach (var evt in events.Where(e => e.EventType is EventType.ToolExecution or EventType.FileChange or EventType.ConversationTurn))
        {
            var outcome = await ShipItemAsync(sessionId, evt, ct);
            if (!await ApplyAsync(sessionId, run, outcome, [(evt, outcome)], ct))
                return;
        }

        if (!await ShipSignalsAsync(sessionId, events, run, ct))
            return;

        // The end waits until nothing else of the session from this poll is still pending.
        if (run.Open().Any(e => e.EventType != EventType.SessionEnd))
            return;

        foreach (var end in OfType(events, EventType.SessionEnd))
        {
            var payload = AgentPayload.WithEventId(_redactor.Redact(end.PayloadJson), end.Id);
            var outcome = OutcomeOf(sessionId, await _api.SendSessionEndAsync(sessionId, payload, ct), [], 1);
            if (!await ApplyAsync(sessionId, run, outcome, [(end, outcome)], ct))
                return;
        }
    }

    /// <returns>False when the session's other items must not ship now.</returns>
    private async Task<bool> ShipStartsAsync(
        string sessionId, IReadOnlyList<QueuedEvent> starts, SessionRun run, CancellationToken ct)
    {
        if (starts.Count == 0)
            return await GateOnQueuedStartAsync(sessionId, run, ct);

        var accepted = false;
        foreach (var start in starts)
        {
            var projectKey = await RecordSessionProjectAsync(sessionId, start, ct);
            var payload = SessionStartPayload.Enrich(_redactor.Redact(start.PayloadJson), projectKey, start.Id);
            var result = await _api.SendSessionStartAsync(payload, ct);
            if (result.Status == PostStatus.Transient)
            {
                run.Transient = true;
                return false;
            }

            if (result.Status == PostStatus.Ok)
            {
                accepted = true;
                await run.MarkShippedAsync([start], ct);
                continue;
            }

            _logger.LogWarning("Server refused start {EventId} of session {SessionId} ({Status}): {Reason}",
                start.Id, sessionId, result.Status, result.Reason);
            await run.MarkDeadAsync([start], ct);
        }

        // A resumed session's earlier start may already be on the server.
        if (accepted || await _queue.GetStartStateAsync(sessionId, ct) == StartState.Shipped)
            return true;

        _logger.LogWarning("The start of session {SessionId} never reached the server; dropping its {Count} queued events",
            sessionId, run.Open().Count);
        await run.MarkDeadAsync(run.Open(), ct);
        return false;
    }

    /// <summary>Asks this queue whether the session's start reached the server. A start that is shipped,
    /// or none at all (purged, or never queued), lets the items go: the server answers session_not_found
    /// if it lacks the session.</summary>
    private async Task<bool> GateOnQueuedStartAsync(string sessionId, SessionRun run, CancellationToken ct)
    {
        switch (await _queue.GetStartStateAsync(sessionId, ct))
        {
            case StartState.Pending:
                return false;
            case StartState.Dead:
                _logger.LogWarning("The start of session {SessionId} never reached the server; dropping its {Count} queued events",
                    sessionId, run.Open().Count);
                await run.MarkDeadAsync(run.Open(), ct);
                return false;
            default:
                return true;
        }
    }

    private async Task<EventOutcome> ShipItemAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        if (evt.EventType == EventType.ConversationTurn)
            return await _conversations.ShipAsync(sessionId, evt, ct);

        var payload = AgentPayload.NormalizeToolEvent(_redactor.Redact(evt.PayloadJson));
        var result = await _api.SendSessionEventsAsync(sessionId, payload, ct);
        return OutcomeOf(sessionId, result, AgentPayload.ItemIds(payload), 1);
    }

    /// <summary>One POST for the session's activity states and tool failures. The payload holds only
    /// states, tool names, ids and times: nothing to redact.</summary>
    private async Task<bool> ShipSignalsAsync(string sessionId, IReadOnlyList<QueuedEvent> events, SessionRun run, CancellationToken ct)
    {
        if (SessionSignals.BuildPayload(sessionId, events) is not { } payload)
            return true;

        var signalEvents = events.Where(e => SessionSignals.IsSignalEvent(e.EventType)).ToList();
        var result = await _api.SendSessionEventsAsync(sessionId, payload, ct);
        var whole = OutcomeOf(sessionId, result, [], signalEvents.Count);
        // Activity carries no id and is accepted with the POST; in a 200, a tool failure has its own outcome.
        var each = signalEvents
            .Select(e => (e, e.EventType == EventType.ToolFailed && result.Status == PostStatus.Ok
                ? OutcomeOf(sessionId, result, [e.Id], 0)
                : whole))
            .ToList();
        return await ApplyAsync(sessionId, run, whole, each, ct);
    }

    /// <summary>The outcome for an event carrying <paramref name="itemIds"/>. A 400 is logged here once
    /// per POST, naming how many events it drops.</summary>
    private EventOutcome OutcomeOf(string sessionId, ApiResult result, IReadOnlyList<string> itemIds, int eventsInPost)
    {
        switch (result.Status)
        {
            case PostStatus.Transient:
                return EventOutcome.Transient;
            case PostStatus.SessionNotFound:
                return EventOutcome.SessionGone;
            case PostStatus.Rejected:
                if (eventsInPost > 0)
                    _logger.LogWarning("Server rejected a request for session {SessionId}; dropping its {Count} events: {Reason}",
                        sessionId, eventsInPost, result.Reason);
                return EventOutcome.Dead;
            // One event in the POST: it can never fit, so it alone is dropped.
            case PostStatus.TooLarge when eventsInPost == 1:
                _logger.LogWarning("Server refused an event of session {SessionId} as too large; dropping it: {Reason}",
                    sessionId, result.Reason);
                return EventOutcome.Dead;
            case PostStatus.TooLarge:
                return EventOutcome.Transient;
        }

        var outcomes = itemIds.Select(id => (Id: id, Outcome: result.OutcomeOf(id))).ToList();
        var unanswered = outcomes.Where(o => o.Outcome is null).Select(o => o.Id).ToList();
        if (unanswered.Count > 0)
        {
            _logger.LogWarning("Server's results left out items {ItemIds} of session {SessionId}; retrying them",
                string.Join(", ", unanswered), sessionId);
            return EventOutcome.Transient;
        }
        if (outcomes.FirstOrDefault(o => o.Outcome == ItemOutcome.Rejected) is not { Id: { } rejectedId })
            return EventOutcome.Done;

        _logger.LogWarning("Server rejected item {ItemId} of session {SessionId}; dropping it: {Reason}",
            rejectedId, sessionId, result.RejectionReason(rejectedId));
        return EventOutcome.Dead;
    }

    /// <summary>Marks each event by its outcome. False when the session must stop for this poll, which an
    /// event left transient also forces.</summary>
    /// <param name="whole">The POST's own outcome: transient or session-gone settles every event in it.</param>
    private async Task<bool> ApplyAsync(
        string sessionId, SessionRun run, EventOutcome whole,
        IReadOnlyList<(QueuedEvent Event, EventOutcome Outcome)> each, CancellationToken ct)
    {
        if (whole == EventOutcome.SessionGone)
        {
            _logger.LogWarning("Server does not know session {SessionId}; dropping its {Count} queued events",
                sessionId, run.Open().Count);
            await run.MarkDeadAsync(run.Open(), ct);
            return false;
        }

        if (whole == EventOutcome.Transient)
        {
            run.Transient = true;
            return false;
        }

        await run.MarkShippedAsync(each.Where(o => o.Outcome == EventOutcome.Done).Select(o => o.Event).ToList(), ct);
        await run.MarkDeadAsync(each.Where(o => o.Outcome == EventOutcome.Dead).Select(o => o.Event).ToList(), ct);
        if (each.All(o => o.Outcome != EventOutcome.Transient))
            return true;

        run.Transient = true;
        return false;
    }

    private async Task<string?> RecordSessionProjectAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var projectKey = await SessionStartPayload.ResolveProjectKeyAsync(evt.PayloadJson, _gitRemote, ct);
        await _usageStore.SetSessionProjectAsync(sessionId, projectKey, ct);
        return projectKey;
    }

    private static List<QueuedEvent> OfType(IEnumerable<QueuedEvent> events, EventType type) =>
        events.Where(e => e.EventType == type).ToList();

    /// <summary>The session's events from this poll, and which of them are already marked.</summary>
    private sealed class SessionRun(IEventQueue queue, IReadOnlyList<QueuedEvent> events)
    {
        private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

        public bool Transient { get; set; }

        public IReadOnlyList<QueuedEvent> Open() => events.Where(e => !_marked.Contains(e.Id)).ToList();

        public Task MarkShippedAsync(IReadOnlyList<QueuedEvent> toMark, CancellationToken ct) =>
            MarkAsync(toMark, queue.MarkShippedAsync, ct);

        public Task MarkDeadAsync(IReadOnlyList<QueuedEvent> toMark, CancellationToken ct) =>
            MarkAsync(toMark, queue.MarkDeadAsync, ct);

        private async Task MarkAsync(
            IReadOnlyList<QueuedEvent> toMark, Func<IReadOnlyList<string>, CancellationToken, Task> mark, CancellationToken ct)
        {
            var ids = new List<string>();
            foreach (var evt in toMark)
            {
                if (_marked.Add(evt.Id))
                    ids.Add(evt.Id);
            }

            if (ids.Count > 0)
                await mark(ids, ct);
        }
    }
}
