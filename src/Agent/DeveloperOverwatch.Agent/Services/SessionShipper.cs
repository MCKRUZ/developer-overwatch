using System.Diagnostics;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Delivers one session's queued events in the order they happened and marks each by its own
/// outcome. Nothing of a session ships until one of its starts opens it (see
/// <see cref="SessionStartPayload.MayOpen"/>), and then only content at or above its floor
/// (<see cref="OpenSession"/>); a start that may not open it closes it, and its items stay here. Backfill alone
/// ships before then, as usage with no content, and opens nothing. Starts,
/// ends, tool, file and turn events are sent where they fall. Activity and tool failures gather and go in one
/// POST before each start or end, and once at the end of the walk. A request the server refuses as a whole is
/// split until each item has its own answer (see <see cref="HalvingPoster"/>): an item rejected alone is dead,
/// and one too large even alone is held, never dead. Once the server no longer knows the session, its events
/// are dropped up to its next start. The first transient failure holds the rest of the session until the next
/// poll.</summary>
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

    /// <param name="pilotSince">Enrolment: no session's floor is below it.</param>
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

    /// <summary>Sends nothing anywhere. The session's project and its usage are kept in queue.db. Each start
    /// closes the session, so its later items stay here too until a later start opens it; every other event is
    /// marked shipped, meaning handled, so the queue drains.</summary>
    public async Task HandleLocallyAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            if (evt.EventType == EventType.SessionStart)
            {
                await RecordSessionProjectAsync(sessionId, evt, ct);
                await _queue.MarkClosedAsync(evt, ct);
            }
            else if (evt.EventType == EventType.ConversationTurn)
            {
                await RecordTurnsLocallyAsync(sessionId, evt, ct);
            }
        }

        await _queue.MarkShippedAsync(
            events.Where(e => e.EventType != EventType.SessionStart).Select(e => e.Id).ToList(), ct);
    }

    /// <returns>True when something was transient, so the caller backs off.</returns>
    public async Task<bool> ShipAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        var ordered = events.OrderBy(e => e.CreatedAt).ThenBy(e => e.RowId).ToList();
        var access = await _queue.GetAccessAsync(sessionId, ct);
        var session = access is { State: AccessState.Open, OpenFrom: { } openFrom }
            ? new OpenSession(sessionId, UtcInstant.Later(_pilotSince, openFrom))
            : null;
        var run = new SessionRun(_queue, ordered, access.State, session);
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
            if (!run.IsMarked(evt) && !await NextAsync(sessionId, run, evt, ct))
                return;
        }

        await FlushSignalsAsync(run, ct);
    }

    /// <summary>Dead is set only by a send of an open session, so a session no start has opened is never dead.</summary>
    /// <returns>False when the session must stop for this poll.</returns>
    private async Task<bool> NextAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        if (run.Session is null && IsBackfill(evt))
            return await ShipBackfillAsync(sessionId, run, evt, ct);

        if (evt.EventType == EventType.SessionStart)
        {
            if (run.Session is not null || SessionStartPayload.MayOpen(evt.PayloadJson, run.State))
                return await StepAsync(sessionId, run, evt, ct);
            await CloseAsync(sessionId, run, evt, ct);
        }
        else if (run.Dead)
            await SweepAsync(sessionId, run, SessionUnknown, ct);
        else if (run.Session is null)
            await KeepLocalAsync(sessionId, run, evt, ct);
        else
            return await StepAsync(sessionId, run, evt, ct);

        return true;
    }

    /// <summary>Backfill of a session no start has opened is usage the developer chose to send, not the developer
    /// coming back to it. It ships through a lane whose floor admits nothing, so no title, text or signal goes with
    /// it, and it neither opens nor closes the session. Each event settles by its own outcome.</summary>
    private async Task<bool> ShipBackfillAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        var lane = new OpenSession(sessionId, DateTime.MaxValue);
        var outcome = evt.EventType switch
        {
            EventType.SessionStart => await SendStartAsync(sessionId, evt, lane, ct),
            EventType.SessionEnd => await SendEndAsync(sessionId, evt, ct),
            _ => await _conversations.ShipAsync(lane, evt, ct)
        };

        if (outcome == EventOutcome.Transient)
        {
            run.Transient = true;
            return false;
        }

        if (outcome == EventOutcome.Done)
            await run.MarkShippedAsync([evt], ct);
        else
            await run.MarkDeadAsync([evt], ct);
        return true;
    }

    /// <summary>A backfill marker is read as <see cref="ConversationMarker"/> reads it, so the lane takes exactly
    /// the markers that carry no text.</summary>
    private static bool IsBackfill(QueuedEvent evt) => evt.EventType switch
    {
        EventType.SessionStart or EventType.SessionEnd => AgentPayload.IsBackfill(evt.PayloadJson),
        EventType.ConversationTurn => ConversationMarker.Parse(evt.PayloadJson) is { Backfill: true },
        _ => false
    };

    /// <summary>A start that may not open the session: a compact or clear of a session already decided here is
    /// the same sitting going on, not the developer coming back to it.</summary>
    private async Task CloseAsync(string sessionId, SessionRun run, QueuedEvent start, CancellationToken ct)
    {
        await RecordSessionProjectAsync(sessionId, start, ct);
        await run.MarkClosedAsync(start, ct);
        run.State = AccessState.Closed;
    }

    /// <summary>An item of a session no start has opened: handled here like the session's earlier items.</summary>
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

    /// <summary>Only an item at or above the floor joins the signals; each sender settles a lower one itself.</summary>
    /// <returns>False when the session must stop for this poll.</returns>
    private async Task<bool> StepAsync(string sessionId, SessionRun run, QueuedEvent evt, CancellationToken ct)
    {
        switch (evt.EventType)
        {
            case EventType.SessionStart:
                return await FlushSignalsAsync(run, ct) && await ShipStartAsync(sessionId, run, evt, ct);
            case EventType.SessionEnd:
                return await FlushSignalsAsync(run, ct)
                    && (run.IsMarked(evt) || await ShipEndAsync(run.Session!, run, evt, ct));
            case EventType.ToolExecution or EventType.FileChange or EventType.ConversationTurn:
                var session = run.Session!;
                if (session.Admits(evt.CreatedAt))
                    run.Signals.Add(evt);
                var outcome = await ShipItemAsync(session, evt, ct);
                return await ApplyAsync(sessionId, run, new SettledPost(outcome, [(evt, outcome)]), ct);
            case EventType.ActivityChanged or EventType.ToolFailed:
                run.Signals.Add(evt);
                return true;
            default:
                throw new UnreachableException($"Unhandled event type {evt.EventType}");
        }
    }

    /// <summary>An accepted start opens the session if it was not open, and lets its later items ship. A
    /// rejected one, or one the server answers session_not_found, is dead, and if it was to open the session, the
    /// session is closed too, since the start still shows the session existed by then, and only a later startup or
    /// resume can open it. A transient start, or one too large to send, holds the session. The title goes only when it was
    /// written at or above the floor the session has, or would have once this start opens it.</summary>
    private async Task<bool> ShipStartAsync(string sessionId, SessionRun run, QueuedEvent start, CancellationToken ct)
    {
        var openFrom = SessionStartPayload.OpenFrom(start.PayloadJson, start.CreatedAt, run.State, _pilotSince);
        var titleSession = run.Session ?? new OpenSession(sessionId, UtcInstant.Later(_pilotSince, openFrom));
        switch (await SendStartAsync(sessionId, start, titleSession, ct))
        {
            case EventOutcome.Transient:
                run.Transient = true;
                return false;
            case EventOutcome.Done:
                await SettleShippedStartAsync(sessionId, run, start, openFrom, ct);
                return true;
            case EventOutcome.SessionGone:
                _logger.LogWarning("Server refused start {EventId} of session {SessionId}: {Why}", start.Id, sessionId, SessionUnknown);
                break;
        }

        if (run.Session is null)
        {
            await run.MarkDeadAndClosedAsync(start, ct);
            run.State = AccessState.Closed;
        }
        else
        {
            await run.MarkDeadAsync([start], ct);
        }

        return true;
    }

    /// <param name="titleSession">The title goes only when this session admits when it was written.</param>
    private async Task<EventOutcome> SendStartAsync(
        string sessionId, QueuedEvent start, OpenSession titleSession, CancellationToken ct)
    {
        var projectKey = await RecordSessionProjectAsync(sessionId, start, ct);
        var payload = SessionStartPayload.Enrich(_redactor.Redact(start.PayloadJson), projectKey, start.Id, titleSession);
        return await SendAloneAsync(sessionId, start, () => _api.SendSessionStartAsync(payload, ct));
    }

    private async Task SettleShippedStartAsync(
        string sessionId, SessionRun run, QueuedEvent start, DateTime openFrom, CancellationToken ct)
    {
        if (run.Session is null)
        {
            var effective = await run.MarkOpenedAsync(start, openFrom, ct);
            run.Session = new OpenSession(sessionId, UtcInstant.Later(_pilotSince, effective));
            run.State = AccessState.Open;
        }
        else
        {
            await run.MarkShippedAsync([start], ct);
        }

        run.Dead = false;
    }

    private async Task<bool> ShipEndAsync(OpenSession session, SessionRun run, QueuedEvent end, CancellationToken ct)
    {
        var outcome = await SendEndAsync(session.SessionId, end, ct);
        return await ApplyAsync(session.SessionId, run, new SettledPost(outcome, [(end, outcome)]), ct);
    }

    private Task<EventOutcome> SendEndAsync(string sessionId, QueuedEvent end, CancellationToken ct)
    {
        var payload = AgentPayload.WithEventId(_redactor.Redact(end.PayloadJson), end.Id);
        return SendAloneAsync(sessionId, end, () => _api.SendSessionEndAsync(sessionId, payload, ct));
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

    private async Task<EventOutcome> ShipItemAsync(OpenSession session, QueuedEvent evt, CancellationToken ct) =>
        evt.EventType == EventType.ConversationTurn
            ? await _conversations.ShipAsync(session, evt, ct)
            : await _tools.ShipAsync(session, evt, ct);

    /// <summary>Signals gather only while the session is open, so with no session there are none.</summary>
    private async Task<bool> FlushSignalsAsync(SessionRun run, CancellationToken ct)
    {
        if (run.Session is not { } session)
            return true;

        var contributors = run.Signals.ToList();
        run.Signals.Clear();
        return await ApplyAsync(session.SessionId, run, await _signals.FlushAsync(session, contributors, ct), ct);
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
        run.Dead = true;
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
}
