using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Captures a ConversationTurn marker's new turns, records their usage on this machine, and ships
/// them in chunks. After each chunk, every transcript's cursor moves past the turns the server has answered
/// for, rejected ones included, so a failed chunk resumes where it stopped.</summary>
internal sealed class ConversationShipper
{
    /// <summary>Keeps a long session's POST well under the server's request size limit.</summary>
    public const int MaxTurnsPerPost = 200;

    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IConversationCapture _capture;
    private readonly IConversationCursorStore _cursorStore;
    private readonly IUsageStore _usageStore;
    private readonly ILogger _logger;
    private readonly DateTime? _pilotSince;

    /// <param name="pilotSince">Turns from before Pilot enrolment are recorded here but never sent, even
    /// when a marker queued after enrolment captures them (a session resumed across enrolment).</param>
    public ConversationShipper(
        IApiClient api, IPrivacyRedactor redactor, IConversationCapture capture,
        IConversationCursorStore cursorStore, IUsageStore usageStore, ILogger logger, DateTime? pilotSince = null)
    {
        _pilotSince = pilotSince;
        _api = api;
        _redactor = redactor;
        _capture = capture;
        _cursorStore = cursorStore;
        _usageStore = usageStore;
        _logger = logger;
    }

    /// <summary>Records the usage and sends nothing. Each live cursor moves up to its first turn dated at or
    /// after the bound, so a later marker still sends that turn and the ones after it; with no bound (Local
    /// mode) it moves to the end.</summary>
    /// <param name="keepFrom">The bound, when it is not enrolment itself: the session began before
    /// enrolment, and only its turns from this instant on may ship after it is resumed.</param>
    public async Task RecordLocallyAsync(
        string sessionId, QueuedEvent marker, CancellationToken ct, DateTime? keepFrom = null)
    {
        if (await CaptureAsync(sessionId, marker, _pilotSince, ct) is not { } captured)
            return;

        var bound = _pilotSince is null ? null : keepFrom ?? _pilotSince;
        foreach (var transcript in captured.Transcripts)
        {
            var stop = bound is { } from ? FirstLineFrom(transcript, from) : null;
            await AdvanceAsync(transcript, stop ?? transcript.Result.TotalLines, ct);
        }
    }

    /// <summary>An undated turn is not one: with a cutoff it is never sent, so the cursor may pass it.</summary>
    private static int? FirstLineFrom(TranscriptCapture transcript, DateTime from) =>
        transcript.Result.Turns
            .Where(t => t.Timestamp is { } ts && ToUtc(ts) >= from)
            .Select(t => (int?)t.StartLine)
            .Min();

    /// <summary>Done once every captured turn has a definitive outcome. A transient chunk stops there:
    /// the cursors keep what earlier chunks earned, and the next poll resumes from them.</summary>
    /// <param name="reopenedAt">When the session left Local: its turns from before then are never sent.</param>
    public async Task<EventOutcome> ShipAsync(
        string sessionId, QueuedEvent marker, CancellationToken ct, DateTime? reopenedAt = null)
    {
        if (await CaptureAsync(sessionId, marker, Later(_pilotSince, reopenedAt), ct) is not { } captured)
            return EventOutcome.Done;

        var answered = new HashSet<string>(StringComparer.Ordinal);
        var turns = captured.Turns;
        if (turns.Count == 0)
        {
            await AdvanceCursorsAsync(captured, answered, ct);
            return EventOutcome.Done;
        }

        var title = captured.Marker.Backfill
            ? null
            : await ConversationPayload.ResolveSessionTitleAsync(captured.Marker.TranscriptPath, _pilotSince, ct);
        var callbacks = Callbacks(sessionId, title, captured, answered, ct);
        foreach (var chunk in turns.Chunk(MaxTurnsPerPost))
        {
            var stop = await HalvingPoster.PostAsync(chunk, callbacks);
            if (stop != HalvingStop.Completed)
                return stop.ToOutcome(EventOutcome.Transient);
        }

        return turns.All(t => answered.Contains(t.Id)) ? EventOutcome.Done : EventOutcome.Transient;
    }

    /// <summary>A refused chunk is split until each turn has its own answer. The cursors move after every
    /// answer, so a turn held as too large keeps them, and the turns after it, where they are.</summary>
    private HalvingPoster.Callbacks<TurnRef> Callbacks(
        string sessionId, string? title, CapturedConversation captured, HashSet<string> answered, CancellationToken ct) =>
        new(
            subset => _api.SendSessionEventsAsync(sessionId, _redactor.Redact(ConversationPayload.Build(
                sessionId, title, subset.Select(t => t.Turn).ToList(), _redactor, captured.Marker.UsageOnly)), ct),
            async (subset, result) =>
            {
                RecordAnswers(sessionId, subset, result, answered);
                await AdvanceCursorsAsync(captured, answered, ct);
            },
            async (turn, result) =>
            {
                _logger.LogWarning("Server rejected turn {TurnId} of session {SessionId}; dropping it: {Reason}",
                    turn.Id, sessionId, result.Reason);
                answered.Add(turn.Id);
                await AdvanceCursorsAsync(captured, answered, ct);
            },
            turn => _logger.LogWarning(
                "Server refused turn {TurnId} of session {SessionId} as too large even alone; holding it and the turns after it for the next poll",
                turn.Id, sessionId));

    /// <summary>Adds each turn a 200 answered for. Rejected turns count too: they would never succeed, so
    /// they are logged here, once, and the cursor moves past them.</summary>
    private void RecordAnswers(string sessionId, IReadOnlyList<TurnRef> chunk, ApiResult result, HashSet<string> answered)
    {
        var rejected = new List<string>();
        foreach (var turn in chunk)
        {
            var outcome = result.OutcomeOf(turn.Id);
            if (outcome is null)
                continue;
            answered.Add(turn.Id);
            if (outcome == ItemOutcome.Rejected)
                rejected.Add(turn.Id);
        }

        if (rejected.Count > 0)
            _logger.LogWarning("Server rejected {Count} turns of session {SessionId}; dropping them. First {TurnId}: {Reason}",
                rejected.Count, sessionId, rejected[0], result.RejectionReason(rejected[0]));
    }

    /// <summary>Each transcript's cursor goes to the start of its first unanswered turn, or to its end
    /// when every turn from it is answered. A usage-only marker has no live cursors to move.</summary>
    private async Task AdvanceCursorsAsync(CapturedConversation captured, HashSet<string> answered, CancellationToken ct)
    {
        foreach (var transcript in captured.Transcripts)
        {
            var firstOpen = captured.Turns
                .Where(t => t.CursorKey == transcript.CursorKey && !answered.Contains(t.Id))
                .Select(t => (int?)t.Turn.StartLine)
                .Min();
            await AdvanceAsync(transcript, firstOpen ?? transcript.Result.TotalLines, ct);
        }
    }

    // A cursor never moves backward, and is written only when it moves.
    private async Task AdvanceAsync(TranscriptCapture transcript, int line, CancellationToken ct)
    {
        if (line > transcript.From)
            await _cursorStore.SetAsync(transcript.CursorKey, line, ct);
    }

    private sealed record TranscriptCapture(string CursorKey, int From, ConversationCaptureResult Result);

    private sealed record TurnRef(string CursorKey, CapturedTurn Turn, string Id);

    /// <param name="Transcripts">The live cursors to move as turns are answered; none for a usage-only marker.</param>
    private sealed record CapturedConversation(
        ConversationMarker Marker, IReadOnlyList<TranscriptCapture> Transcripts, IReadOnlyList<TurnRef> Turns);

    /// <summary>Captures the new turns and records their usage on this machine before anything is sent,
    /// so a failed send never loses it; re-capture after a failure is deduplicated by the turns' stable
    /// ids. Null when the marker names no transcript.</summary>
    /// <param name="shipFrom">Turns dated before it are recorded but never sent: enrolment, or the later
    /// instant the session reopened.</param>
    private async Task<CapturedConversation?> CaptureAsync(
        string sessionId, QueuedEvent evt, DateTime? shipFrom, CancellationToken ct)
    {
        if (ConversationMarker.Parse(evt.PayloadJson) is not { } marker)
        {
            _logger.LogDebug("ConversationTurn marker names no transcript, skipping");
            return null;
        }

        var transcripts = await CaptureTranscriptsAsync(sessionId, marker, ct);
        // The server numbers turns in arrival order, so interleave subagent work with the parent
        // conversation by time. OrderBy is stable: same-timestamp turns keep transcript order.
        var turns = transcripts
            .SelectMany(c => c.Result.Turns.Select(t => new TurnRef(c.CursorKey, t, StableTurnId.From(t.SourceId))))
            .Where(t => marker.From is not { } from || (t.Turn.Timestamp is { } ts && ToUtc(ts) >= from))
            .OrderBy(t => t.Turn.Timestamp ?? DateTime.MaxValue)
            .ToList();

        await _usageStore.RecordTurnsAsync(sessionId, turns.Select(t => t.Turn).ToList(), ct);
        // Left out of Turns, a pre-enrolment turn is never sent, and the cursor moves past it like an answered one.
        // A turn with no timestamp cannot be shown to postdate enrolment, so with a cutoff it is never sent either.
        // Backfill is the explicit opt-in for that usage, and never carries text.
        var shippable = turns
            .Where(t => marker.Backfill || shipFrom is not { } since || (t.Turn.Timestamp is { } ts && ToUtc(ts) >= since))
            .ToList();
        if (shipFrom is { } cutoff && !marker.UsageOnly)
            shippable = await HoldPreEnrolmentSummariesAsync(sessionId, marker, shippable, cutoff, ct);
        return new CapturedConversation(marker, marker.UsageOnly ? [] : transcripts, shippable);
    }

    /// <summary>A compaction summary is dated at compaction but restates the prompts it replaces, so its
    /// text ships only when the whole transcript, read from its first line, is from after enrolment and
    /// holds the summary. Otherwise it ships as usage alone. Capture reads only past the cursor, hence the
    /// second, full read; it happens only when a summary is present, and a transcript that cannot be read
    /// comes back empty, which holds the text.</summary>
    private async Task<List<TurnRef>> HoldPreEnrolmentSummariesAsync(
        string sessionId, ConversationMarker marker, List<TurnRef> shippable, DateTime cutoff, CancellationToken ct)
    {
        if (!shippable.Any(t => t.CursorKey == sessionId && t.Turn.IsCompactSummary))
            return shippable;

        var whole = (await _capture.CaptureFromFileAsync(marker.TranscriptPath, 0, false, ct)).Turns;
        var allAfter = whole.Count > 0 && whole.All(t => t.Timestamp is { } ts && ToUtc(ts) >= cutoff);
        var read = whole.Select(t => t.SourceId).ToHashSet(StringComparer.Ordinal);
        return shippable
            .Select(t => t.CursorKey != sessionId || !t.Turn.IsCompactSummary || (allAfter && read.Contains(t.Turn.SourceId))
                ? t
                : t with { Turn = t.Turn with { Content = null } })
            .ToList();
    }

    /// <summary>Reads the session transcript plus every subagent transcript beside it. A subagent's
    /// real usage — with its input/output/cache split and its own model — is only in its own file;
    /// the parent's completion notice reports just the subagent's final context size. A usage-only
    /// marker reads every file from line 0 with a throwaway cursor: the live cursors belong to live
    /// capture, which it must neither skip nor move.</summary>
    private async Task<List<TranscriptCapture>> CaptureTranscriptsAsync(
        string sessionId, ConversationMarker marker, CancellationToken ct)
    {
        async Task<TranscriptCapture> CaptureOneAsync(string cursorKey, string path, bool asSubagent)
        {
            var cursor = marker.UsageOnly ? 0 : await _cursorStore.GetAsync(cursorKey, ct);
            return new(cursorKey, cursor, await _capture.CaptureFromFileAsync(path, cursor, asSubagent, ct));
        }

        var captures = new List<TranscriptCapture> { await CaptureOneAsync(sessionId, marker.TranscriptPath, false) };
        foreach (var subagentPath in _capture.FindSubagentTranscripts(marker.TranscriptPath))
        {
            var cursorKey = $"{sessionId}/{Path.GetFileNameWithoutExtension(subagentPath)}";
            captures.Add(await CaptureOneAsync(cursorKey, subagentPath, true));
        }

        return captures;
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

    private static DateTime? Later(DateTime? a, DateTime? b) =>
        a is null ? b : b is null ? a : (ToUtc(a.Value) >= ToUtc(b.Value) ? a : b);
}
