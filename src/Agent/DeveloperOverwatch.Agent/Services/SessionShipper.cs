using System.Diagnostics;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Delivers one session's queued events in the order they happened and marks each by its own
/// outcome. Starts, ends, tool, file and turn events are sent where they fall. Activity and tool failures
/// gather and go in one POST before each start or end, and once at the end of the walk. A request the
/// server refuses as a whole is split until each item has its own answer (see <see cref="HalvingPoster"/>):
/// an item rejected alone is dead, and one too large even alone is held, never dead. Once the server no
/// longer knows the session, its events are dropped up to its next start; a refused start leaves that to
/// the server's answer to the items after it. A
/// session whose start was handled locally keeps its items here, unsent, until a later new-session or resume
/// start ships; a compact or clear start stays here with it. The
/// first transient failure holds the rest of the session until the next poll.</summary>
internal sealed class SessionShipper
{
    private const string SessionUnknown = "the server does not know the session";

    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitReader _gitRemote;
    private readonly IUsageStore _usageStore;
    private readonly ConversationShipper _conversations;
    private readonly ToolEventShipper _tools;
    private readonly SignalShipper _signals;
    private readonly ILogger _logger;
    private readonly DateTime? _pilotSince;

    /// <param name="pilotSince">A start's title written before Pilot enrolment is not sent.</param>
    public SessionShipper(
        IEventQueue queue, IApiClient api, IPrivacyRedactor redactor, IGitReader gitRemote,
        IUsageStore usageStore, ConversationShipper conversations, ILogger logger, DateTime? pilotSince)
    {
        _pilotSince = pilotSince;
        _queue = queue;
        _api = api;
        _redactor = redactor;
        _gitRemote = gitRemote;
        _usageStore = usageStore;
        _conversations = conversations;
        _tools = new ToolEventShipper(api, redactor, logger);
        _signals = new SignalShipper(api, logger);
        _logger = logger;
    }

    /// <summary>Sends nothing anywhere. The session's project and its usage are kept in queue.db. Starts are
    /// marked Local, so the session's later items stay here too; every other event is marked shipped,
    /// meaning handled, so the queue drains.</summary>
    public async Task HandleLocallyAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            if (evt.EventType == EventType.SessionStart)
                await RecordSessionProjectAsync(sessionId, evt, ct);
            else if (evt.EventType == EventType.ConversationTurn)
                await RecordTurnsLocallyAsync(sessionId, evt, ct);
        }

        var starts = events.Where(e => e.EventType == EventType.SessionStart).ToList();
        await _queue.MarkLocalAsync(starts.Select(e => e.Id).ToList(), ct);
        await _queue.MarkShippedAsync(events.Except(starts).Select(e => e.Id).ToList(), ct);
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
                await SweepAsync(sessionId, run, SessionUnknown, ct);
            else if (evt.EventType != EventType.SessionStart && run.Gate == StartState.Local)
                await KeepLocalAsync(sessionId, run, evt, ct);
            else if (run.Gate == StartState.Local && !SessionStartPayload.MayReopen(evt.PayloadJson))
                await KeepStartLocalAsync(sessionId, run, evt, ct);
            else if (!await StepAsync(sessionId, run, evt, ct))
                return;
        }

        await FlushSignalsAsync(sessionId, run, ct);
    }

    /// <summary>An item of a session that began before enrolment, queued after it: handled here like the
    /// session's earlier items.</summary>
    private async Task KeepLocalAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        if (evt.EventType == EventType.ConversationTurn)
            await RecordTurnsLocallyAsync(sessionId, evt, ct);

        await run.MarkShippedAsync([evt], ct);
    }

    /// <summary>A turn marker kept here reads the transcript as it is now, which may already hold turns from
    /// after the session's next start, so its cursors pass only the turns before that start (or, with none
    /// queued, before the marker), and a later resume does not send them.</summary>
    private async Task RecordTurnsLocallyAsync(string sessionId, QueuedEvent marker, CancellationToken ct)
    {
        var resumedAt = await _queue.GetNextStartAtAsync(sessionId, marker.CreatedAt, ct);
        await _conversations.RecordLocallyAsync(sessionId, marker, ct, keepFrom: resumedAt ?? marker.CreatedAt);
    }

    /// <summary>A compact or clear start of a session kept here is the same sitting going on, not the
    /// developer coming back to it, so it stays here like the session's first start.</summary>
    private async Task KeepStartLocalAsync(string sessionId, SessionRun run, QueuedEvent start, CancellationToken ct)
    {
        await RecordSessionProjectAsync(sessionId, start, ct);
        await run.MarkLocalAsync([start], ct);
    }

    /// <returns>False when the session must stop for this poll.</returns>
    private async Task<bool> StepAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case EventType.SessionStart:
                return await FlushSignalsAsync(sessionId, run, ct) && await ShipStartAsync(sessionId, run, evt, ct);
            case EventType.SessionEnd:
                return await FlushSignalsAsync(sessionId, run, ct)
                    && (run.IsMarked(evt) || await ShipEndAsync(sessionId, run, evt, ct));
            case EventType.ToolExecution or EventType.FileChange or EventType.ConversationTurn:
                run.Signals.Add(evt);
                var outcome = await ShipItemAsync(sessionId, evt, ct);
                return await ApplyAsync(sessionId, run, new SettledPost(outcome, [(evt, outcome)]), ct);
            case EventType.ActivityChanged or EventType.ToolFailed:
                run.Signals.Add(evt);
                return true;
            default:
                throw new UnreachableException($"Unhandled event type {evt.EventType}");
        }
    }

    /// <summary>An accepted start lets the session's later items ship. A rejected one, or one the server
    /// answers session_not_found, is dead and leaves the gate as it was: an earlier start may have reached
    /// the server, so its items are sent and the server's answer to them decides, unless the session is
    /// Local. A transient start, or one too large to send, holds the session. A start that reopens a Local
    /// session sends only a title written from that start on.</summary>
    private async Task<bool> ShipStartAsync(string sessionId, SessionRun run, QueuedEvent start, CancellationToken ct)
    {
        var projectKey = await RecordSessionProjectAsync(sessionId, start, ct);
        var titleFrom = run.Gate == StartState.Local ? Later(_pilotSince, start.CreatedAt) : _pilotSince;
        var payload = SessionStartPayload.Enrich(_redactor.Redact(start.PayloadJson), projectKey, start.Id, titleFrom);
        var outcome = await SendAloneAsync(sessionId, start, () => _api.SendSessionStartAsync(payload, ct));
        switch (outcome)
        {
            case EventOutcome.Transient:
                run.Transient = true;
                return false;
            case EventOutcome.Done:
                await run.MarkShippedAsync([start], ct);
                run.Gate = StartState.Shipped;
                return true;
            case EventOutcome.SessionGone:
                _logger.LogWarning("Server refused start {EventId} of session {SessionId}: {Why}", start.Id, sessionId, SessionUnknown);
                break;
        }

        await run.MarkDeadAsync([start], ct);
        return true;
    }

    private async Task<bool> ShipEndAsync(string sessionId, SessionRun run, QueuedEvent end, CancellationToken ct)
    {
        var payload = AgentPayload.WithEventId(_redactor.Redact(end.PayloadJson), end.Id);
        var outcome = await SendAloneAsync(sessionId, end, () => _api.SendSessionEndAsync(sessionId, payload, ct));
        return await ApplyAsync(sessionId, run, new SettledPost(outcome, [(end, outcome)]), ct);
    }

    /// <summary>A start or end is a request of one item: a 400 rejects it, and a 413 holds it.</summary>
    private async Task<EventOutcome> SendAloneAsync(string sessionId, QueuedEvent evt, Func<Task<ApiResult>> send)
    {
        var outcome = EventOutcome.Done;
        var stop = await HalvingPoster.PostAsync([evt], new HalvingPoster.Callbacks<QueuedEvent>(
            _ => send(),
            (_, _) => Task.CompletedTask,
            (_, result) =>
            {
                _logger.LogWarning("Server rejected {EventType} {EventId} of session {SessionId}; dropping it: {Reason}",
                    evt.EventType, evt.Id, sessionId, result.Reason);
                outcome = EventOutcome.Dead;
                return Task.CompletedTask;
            },
            _ => _logger.LogWarning("Server refused {EventType} {EventId} of session {SessionId} as too large; holding it for the next poll",
                evt.EventType, evt.Id, sessionId)));
        return stop.ToOutcome(outcome);
    }

    /// <summary>A reopened session's turns from before it reopened are not sent, even when no marker moved
    /// its cursors past them while it was Local.</summary>
    private async Task<EventOutcome> ShipItemAsync(string sessionId, QueuedEvent evt, CancellationToken ct) =>
        evt.EventType == EventType.ConversationTurn
            ? await _conversations.ShipAsync(sessionId, evt, ct, await _queue.GetReopenedAtAsync(sessionId, ct))
            : await _tools.ShipAsync(sessionId, evt, ct);

    private async Task<bool> FlushSignalsAsync(string sessionId, SessionRun run, CancellationToken ct)
    {
        var contributors = run.Signals.ToList();
        run.Signals.Clear();
        return await ApplyAsync(sessionId, run, await _signals.FlushAsync(sessionId, contributors, ct), ct);
    }

    /// <summary>Marks each event the send settled. Then a session-gone send drops the session's open events up
    /// to its next start. False when the session must stop for this poll: the send stopped transient, or left
    /// an event transient.</summary>
    private async Task<bool> ApplyAsync(string sessionId, SessionRun run, SettledPost settled, CancellationToken ct)
    {
        var each = settled.Each;
        await run.MarkShippedAsync(each.Where(o => o.Outcome == EventOutcome.Done).Select(o => o.Event).ToList(), ct);
        await run.MarkDeadAsync(each.Where(o => o.Outcome == EventOutcome.Dead).Select(o => o.Event).ToList(), ct);
        if (settled.Whole == EventOutcome.SessionGone)
        {
            await SweepAsync(sessionId, run, SessionUnknown, ct);
            return true;
        }

        if (settled.Whole != EventOutcome.Transient && each.All(o => o.Outcome != EventOutcome.Transient))
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

    private static DateTime Later(DateTime? a, DateTime b) =>
        a is { } first && first.ToUniversalTime() > b.ToUniversalTime() ? first : b;

    /// <summary>The session's events from this poll in the order they happened, the walk's position in
    /// them, and which of them are already marked.</summary>
    /// <param name="gate">What the session's settled starts outside this poll say.</param>
    private sealed class SessionRun(IEventQueue queue, IReadOnlyList<QueuedEvent> events, StartState gate)
    {
        private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

        public IReadOnlyList<QueuedEvent> Events => events;

        public int At { get; set; }

        /// <summary>Shipped once a start ships. Dead once the server answers session_not_found, and it drops
        /// the session's items up to its next start; Local handles them here.</summary>
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

        public Task MarkLocalAsync(IReadOnlyList<QueuedEvent> toMark, CancellationToken ct) =>
            MarkAsync(toMark, queue.MarkLocalAsync, ct);

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
