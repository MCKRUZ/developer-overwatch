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

    public ConversationShipper(
        IApiClient api, IPrivacyRedactor redactor, IConversationCapture capture,
        IConversationCursorStore cursorStore, IUsageStore usageStore, ILogger logger)
    {
        _api = api;
        _redactor = redactor;
        _capture = capture;
        _cursorStore = cursorStore;
        _usageStore = usageStore;
        _logger = logger;
    }

    /// <summary>Local mode: records the usage and moves the live cursors to the end. Sends nothing.</summary>
    public async Task RecordLocallyAsync(string sessionId, QueuedEvent marker, CancellationToken ct)
    {
        if (await CaptureAsync(sessionId, marker, ct) is not { } captured)
            return;

        foreach (var transcript in captured.Transcripts)
            await AdvanceAsync(transcript, transcript.Result.TotalLines, ct);
    }

    /// <summary>Done once every captured turn has a definitive outcome. A transient chunk stops there:
    /// the cursors keep what earlier chunks earned, and the next poll resumes from them.</summary>
    public async Task<EventOutcome> ShipAsync(string sessionId, QueuedEvent marker, CancellationToken ct)
    {
        if (await CaptureAsync(sessionId, marker, ct) is not { } captured)
            return EventOutcome.Done;

        var answered = new HashSet<string>(StringComparer.Ordinal);
        if (captured.Turns.Count > 0)
        {
            var title = await ConversationPayload.ResolveSessionTitleAsync(captured.Marker.TranscriptPath, ct);
            foreach (var chunk in captured.Turns.Chunk(MaxTurnsPerPost))
            {
                var payload = ConversationPayload.Build(
                    sessionId, title, chunk.Select(t => t.Turn).ToList(), _redactor, captured.Marker.UsageOnly);
                var result = await _api.SendSessionEventsAsync(sessionId, _redactor.Redact(payload), ct);
                if (result.Status == PostStatus.Transient)
                    return EventOutcome.Transient;
                if (result.Status == PostStatus.SessionNotFound)
                    return EventOutcome.SessionGone;

                RecordAnswers(sessionId, chunk, result, answered);
                await AdvanceCursorsAsync(captured, answered, ct);
            }
        }
        else
        {
            await AdvanceCursorsAsync(captured, answered, ct);
        }

        return captured.Turns.All(t => answered.Contains(t.Id)) ? EventOutcome.Done : EventOutcome.Pending;
    }

    /// <summary>Adds each turn the server answered for. Rejected turns count too: they would never
    /// succeed, so they are logged here, once, and the cursor moves past them.</summary>
    private void RecordAnswers(string sessionId, TurnRef[] chunk, ApiResult result, HashSet<string> answered)
    {
        var rejected = new List<string>();
        foreach (var turn in chunk)
        {
            var outcome = result.Status == PostStatus.Rejected ? ItemOutcome.Rejected : result.OutcomeOf(turn.Id);
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
    private async Task<CapturedConversation?> CaptureAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
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
        return new CapturedConversation(marker, marker.UsageOnly ? [] : transcripts, turns);
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
}
