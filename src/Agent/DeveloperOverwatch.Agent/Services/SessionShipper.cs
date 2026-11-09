using System.Diagnostics;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Delivers one session's queued events in the order they happened and marks each by its own
/// outcome. Starts, ends, tool, file and turn events are sent where they fall. Activity and tool failures
/// gather and go in one POST before each start or end, and once at the end of the walk. Once a start is
/// refused, or the server no longer knows the session, its events are dropped up to its next start. The
/// first transient failure holds the rest of the session until the next poll.</summary>
internal sealed class SessionShipper
{
    private const string StartNeverArrived = "its start never reached the server";
    private const string SessionUnknown = "the server does not know the session";

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
        var ordered = events.OrderBy(e => e.CreatedAt).ThenBy(e => e.RowId).ToList();
        var run = new SessionRun(_queue, ordered, await _queue.GetStartStateAsync(sessionId, ct));
        await WalkAsync(sessionId, run, ct);

        if (run.Transient)
        {
            foreach (var evt in run.Open())
                await _queue.IncrementRetryAsync(evt.Id, ct);
        }

        return run.Transient;
    }

    private async Task WalkAsync(string sessionId, SessionRun run, CancellationToken ct)
    {
        for (run.At = 0; run.At < run.Events.Count; run.At++)
        {
            var evt = run.Events[run.At];
            if (run.IsMarked(evt))
                continue;

            if (evt.EventType != EventType.SessionStart && run.Gate == StartState.Dead)
                await SweepAsync(sessionId, run, StartNeverArrived, ct);
            else if (!await StepAsync(sessionId, run, evt, ct))
                return;
        }

        await FlushSignalsAsync(sessionId, run, ct);
    }

    /// <returns>False when the session must stop for this poll.</returns>
    private async Task<bool> StepAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case EventType.SessionStart:
                return await FlushSignalsAsync(sessionId, run, ct) && await ShipStartAsync(sessionId, run, evt, ct);
            case EventType.SessionEnd:
                return await FlushSignalsAsync(sessionId, run, ct) && await ShipEndAsync(sessionId, run, evt, ct);
            case EventType.ToolExecution or EventType.FileChange or EventType.ConversationTurn:
                run.Signals.Add(evt);
                var outcome = await ShipItemAsync(sessionId, evt, ct);
                return await ApplyAsync(sessionId, run, outcome, [(evt, outcome)], ct);
            case EventType.ActivityChanged or EventType.ToolFailed:
                run.Signals.Add(evt);
                return true;
            default:
                throw new UnreachableException($"Unhandled event type {evt.EventType}");
        }
    }

    /// <summary>An accepted start lets the session's later items ship. A refused one drops them up to the
    /// next start, unless an earlier start of this session already reached the server.</summary>
    private async Task<bool> ShipStartAsync(string sessionId, SessionRun run, QueuedEvent start, CancellationToken ct)
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
            await run.MarkShippedAsync([start], ct);
            run.Gate = StartState.Shipped;
            return true;
        }

        _logger.LogWarning("Server refused start {EventId} of session {SessionId} ({Status}): {Reason}",
            start.Id, sessionId, result.Status, result.Reason);
        await run.MarkDeadAsync([start], ct);
        if (run.Gate != StartState.Shipped)
            run.Gate = StartState.Dead;
        return true;
    }

    private async Task<bool> ShipEndAsync(string sessionId, SessionRun run, QueuedEvent end, CancellationToken ct)
    {
        var payload = AgentPayload.WithEventId(_redactor.Redact(end.PayloadJson), end.Id);
        var outcome = OutcomeOf(sessionId, await _api.SendSessionEndAsync(sessionId, payload, ct), [], 1);
        return await ApplyAsync(sessionId, run, outcome, [(end, outcome)], ct);
    }

    private async Task<EventOutcome> ShipItemAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        if (evt.EventType == EventType.ConversationTurn)
            return await _conversations.ShipAsync(sessionId, evt, ct);

        var payload = AgentPayload.NormalizeToolEvent(_redactor.Redact(evt.PayloadJson));
        var result = await _api.SendSessionEventsAsync(sessionId, payload, ct);
        return OutcomeOf(sessionId, result, AgentPayload.ItemIds(payload), 1);
    }

    /// <summary>One POST for the activity states and tool failures gathered since the last one. The payload
    /// holds only states, tool names, ids and times: nothing to redact.</summary>
    private async Task<bool> FlushSignalsAsync(string sessionId, SessionRun run, CancellationToken ct)
    {
        var contributors = run.Signals.ToList();
        run.Signals.Clear();
        if (SessionSignals.BuildPayload(sessionId, contributors) is not { } payload)
            return true;

        var signalEvents = contributors.Where(e => SessionSignals.IsSignalEvent(e.EventType)).ToList();
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
            await SweepAsync(sessionId, run, SessionUnknown, ct);
            return true;
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

    /// <summary>Drops the session's open events up to its next start, which the walk still sends: a resumed
    /// session is a fresh chance to reach the server.</summary>
    private async Task SweepAsync(string sessionId, SessionRun run, string why, CancellationToken ct)
    {
        var dropped = run.OpenBeforeNextStart();
        run.Signals.Clear();
        run.Gate = StartState.Dead;
        _logger.LogWarning("Dropping {Count} queued events of session {SessionId} up to its next start: {Why}",
            dropped.Count, sessionId, why);
        await run.MarkDeadAsync(dropped, ct);
    }

    private async Task<string?> RecordSessionProjectAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var projectKey = await SessionStartPayload.ResolveProjectKeyAsync(evt.PayloadJson, _gitRemote, ct);
        await _usageStore.SetSessionProjectAsync(sessionId, projectKey, ct);
        return projectKey;
    }

    /// <summary>The session's events from this poll in the order they happened, the walk's position in
    /// them, and which of them are already marked.</summary>
    /// <param name="gate">What the session's settled starts outside this poll say.</param>
    private sealed class SessionRun(IEventQueue queue, IReadOnlyList<QueuedEvent> events, StartState gate)
    {
        private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

        public IReadOnlyList<QueuedEvent> Events => events;

        public int At { get; set; }

        /// <summary>What the latest start settled. Dead drops the session's items up to its next start.</summary>
        public StartState Gate { get; set; } = gate;

        /// <summary>Activity and tool failures gathered since the last signals POST, with the tool and turn
        /// events activity is derived from.</summary>
        public List<QueuedEvent> Signals { get; } = [];

        public bool Transient { get; set; }

        public bool IsMarked(QueuedEvent evt) => _marked.Contains(evt.Id);

        public IReadOnlyList<QueuedEvent> Open() => events.Where(e => !IsMarked(e)).ToList();

        /// <summary>Open events before the first start at or after <see cref="At"/>. Signals gathered
        /// earlier are open and belong to the same stretch, so they are included.</summary>
        public IReadOnlyList<QueuedEvent> OpenBeforeNextStart()
        {
            var bound = At;
            while (bound < events.Count && events[bound].EventType != EventType.SessionStart)
                bound++;
            return events.Take(bound).Where(e => !IsMarked(e)).ToList();
        }

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
