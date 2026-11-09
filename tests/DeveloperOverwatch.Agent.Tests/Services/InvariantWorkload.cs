using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The sessions the delivery invariant test queues, and what the server must end up holding for
/// them. Each enqueue records whether its item must be stored, so the expectations cannot drift from the
/// workload. sess-a is plain; sess-big carries the poison turn and outgrows the server's item limit;
/// sess-r is resumed and stays active; sess-held carries a turn too large even alone, which holds it and
/// everything after it; sess-d began before enrolment, and nothing of it may ever be sent, including the
/// items queued after the clock jump purged every shipped row; sess-q is backfilled, usage only.</summary>
internal sealed class InvariantWorkload(Func<IEventQueue> queue, Dictionary<string, List<CapturedTurn>> transcripts, DateTime pilotSince)
{
    private const string A = "sess-a";
    private const string Big = "sess-big";
    private const string R = "sess-r";
    private const string Held = "sess-held";
    private const string D = "sess-d";
    private const string Q = "sess-q";

    public static readonly string PoisonId = TurnId(Big, 117);
    public static readonly string HeldId = TurnId(Held, 2);

    private readonly List<(string Session, bool IsStart, bool IsBackfill)> _edges = [];
    private DateTime _post = pilotSince.AddMinutes(1);
    private DateTime _pre = pilotSince.AddMinutes(-20);

    /// <summary>Every row the server must hold at the end, as <see cref="StoredRow.Key"/>.</summary>
    public HashSet<string> ExpectedRows { get; } = new(StringComparer.Ordinal);

    /// <summary>The queue ids that must stay pending forever: the held marker and everything after it.</summary>
    public HashSet<string> HeldPending { get; } = new(StringComparer.Ordinal);

    /// <summary>Ids of turns dated before enrolment.</summary>
    public HashSet<string> PreTurnIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Ids of the turns only a usage-only backfill may send.</summary>
    public HashSet<string> BackfillTurnIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Strings no request may contain: sess-d's name (in its ids, texts and paths), its turn ids,
    /// sess-q's pre-enrolment live start and its conversation text.</summary>
    public List<string> Forbidden { get; } = [D, "secret sess-q", $"{Q}:live-start"];

    public static string TurnId(string sessionId, int index) => StableTurnId.From($"uuid:{sessionId}-{index}");

    /// <summary>The server's status rules applied to every start and end that must reach it, in queue order.</summary>
    public Dictionary<string, FakeSessionStatus> ExpectedSessions()
    {
        var statuses = new Dictionary<string, FakeSessionStatus>(StringComparer.Ordinal);
        foreach (var (session, isStart, isBackfill) in _edges)
        {
            var current = statuses.TryGetValue(session, out var s) ? s : (FakeSessionStatus?)null;
            if (SessionStatusRules.Next(current, isStart, isBackfill) is { } next)
                statuses[session] = next;
        }

        return statuses;
    }

    public async Task SeedAsync()
    {
        await SessionAAsync();
        await SessionBigAsync();
        await SessionResumedAsync();
        await SessionHeldAsync();
        await SessionPreEnrolmentAsync();
        await SessionBackfillAsync();
    }

    /// <summary>sess-d's items queued after the clock jump: its Local start must still keep them here.</summary>
    public async Task SeedLateAsync()
    {
        await ToolAsync(D, "late-tool", items: 1);
        AddTurns(D, 2, pre: false, stored: _ => false);
        await MarkerAsync(D, "late-marker");
        await FailureAsync(D, "late-fail");
        await EndAsync(D, "late-end");
    }

    private async Task SessionAAsync()
    {
        await StartAsync(A, "start");
        await ToolAsync(A, "tool-1", items: 1);
        await FailureAsync(A, "fail-1");
        await ToolAsync(A, "tool-2", items: 3);
        AddTurns(A, 8, pre: false, stored: _ => true);
        await MarkerAsync(A, "marker");
        await EnqueueAsync(Event($"{A}:activity", A, EventType.ActivityChanged, $$"""{"state":"waiting","at":"{{_post:o}}"}"""), pre: false);
        await FailureAsync(A, "fail-2");
        await EndAsync(A, "end");
    }

    private async Task SessionBigAsync()
    {
        await StartAsync(Big, "start");
        AddTurns(Big, 230, pre: false, stored: i => TurnId(Big, i) != PoisonId);
        await MarkerAsync(Big, "marker");
        await ToolAsync(Big, "tool", items: 1);
        await EndAsync(Big, "end");
    }

    private async Task SessionResumedAsync()
    {
        await StartAsync(R, "start-1");
        await ToolAsync(R, "tool-1", items: 1);
        AddTurns(R, 6, pre: false, stored: _ => true);
        await MarkerAsync(R, "marker-1");
        await EndAsync(R, "end-1");
        await StartAsync(R, "start-2");
        await ToolAsync(R, "tool-2", items: 1);
        AddTurns(R, 3, pre: false, stored: _ => true);
        await MarkerAsync(R, "marker-2");
        await FailureAsync(R, "fail");
    }

    private async Task SessionHeldAsync()
    {
        await StartAsync(Held, "start");
        await ToolAsync(Held, "h1", items: 1);
        AddTurns(Held, 6, pre: false, stored: i => i < 2);
        await MarkerAsync(Held, "marker", held: true);
        await ToolAsync(Held, "h2", items: 1, held: true);
        await EndAsync(Held, "end", held: true);
    }

    private async Task SessionPreEnrolmentAsync()
    {
        await StartAsync(D, "start", pre: true);
        await ToolAsync(D, "tool", items: 1, pre: true);
        await FailureAsync(D, "fail", pre: true);
        AddTurns(D, 2, pre: true, stored: _ => false);
        await MarkerAsync(D, "marker", pre: true);
    }

    private async Task SessionBackfillAsync()
    {
        await StartAsync(Q, "live-start", pre: true);
        AddTurns(Q, 3, pre: true, stored: _ => true);
        BackfillTurnIds.UnionWith(Enumerable.Range(0, 3).Select(i => TurnId(Q, i)));
        await StartAsync(Q, "bf-start", backfill: true);

        var from = transcripts[TranscriptOf(Q)][0].Timestamp!.Value.AddMinutes(-1);
        await EnqueueAsync(Event($"{Q}:bf-marker", Q, EventType.ConversationTurn, JsonSerializer.Serialize(new
        {
            sessionId = Q, transcriptPath = TranscriptOf(Q), usageOnly = true, isBackfill = true, from
        })), pre: false);
        await EndAsync(Q, "bf-end");
    }

    private void AddTurns(string sessionId, int count, bool pre, Func<int, bool> stored)
    {
        var path = TranscriptOf(sessionId);
        var transcript = transcripts.TryGetValue(path, out var existing) ? existing : transcripts[path] = [];
        for (var n = 0; n < count; n++)
        {
            var i = transcript.Count;
            var id = TurnId(sessionId, i);
            transcript.Add(new CapturedTurn(
                $"uuid:{sessionId}-{i}", "user", $"secret {sessionId} turn {i}", Next(pre), InputTokens: 10, OutputTokens: 5, StartLine: i));
            if (pre)
                PreTurnIds.Add(id);
            if (sessionId == D)
                Forbidden.Add(id);
            if (stored(i))
                Expect("turn", sessionId, id);
        }
    }

    private async Task StartAsync(string sessionId, string name, bool pre = false, bool backfill = false)
    {
        var id = $"{sessionId}:{name}";
        object payload = backfill
            ? new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = _post, isBackfill = true }
            : new { sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = _post };
        if (!await EnqueueAsync(Event(id, sessionId, EventType.SessionStart, JsonSerializer.Serialize(payload)), pre))
            return;
        Expect("start", sessionId, id);
        _edges.Add((sessionId, true, backfill));
    }

    private async Task EndAsync(string sessionId, string name, bool held = false)
    {
        var id = $"{sessionId}:{name}";
        if (!await EnqueueAsync(Event(id, sessionId, EventType.SessionEnd, JsonSerializer.Serialize(new { sessionId, endedAt = _post })), false, held))
            return;
        Expect("end", sessionId, id);
        _edges.Add((sessionId, false, false));
    }

    private async Task ToolAsync(string sessionId, string name, int items, bool pre = false, bool held = false)
    {
        var id = $"{sessionId}:{name}";
        var itemIds = Enumerable.Range(0, items).Select(k => $"{id}-{k}").ToList();
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            toolExecutions = itemIds.Select(itemId => new { id = itemId, toolName = "Read", timestamp = _post }),
            fileChanges = Array.Empty<object>(),
            conversationTurns = Array.Empty<object>()
        });
        if (await EnqueueAsync(Event(id, sessionId, EventType.ToolExecution, payload), pre, held))
            itemIds.ForEach(itemId => Expect("tool", sessionId, itemId));
    }

    private async Task FailureAsync(string sessionId, string name, bool pre = false)
    {
        var id = $"{sessionId}:{name}";
        if (await EnqueueAsync(Event(id, sessionId, EventType.ToolFailed, $$"""{"toolName":"Bash","at":"{{_post:o}}"}"""), pre))
            Expect("failure", sessionId, id);
    }

    private Task MarkerAsync(string sessionId, string name, bool pre = false, bool held = false) =>
        EnqueueAsync(Event($"{sessionId}:{name}", sessionId, EventType.ConversationTurn,
            JsonSerializer.Serialize(new { transcriptPath = TranscriptOf(sessionId) })), pre, held);

    /// <returns>True when the event's item must reach the server: it is post-enrolment, not sess-d's, and
    /// not held.</returns>
    private async Task<bool> EnqueueAsync(QueuedEvent evt, bool pre, bool held = false)
    {
        await queue().EnqueueAsync(new QueuedEvent
        {
            Id = evt.Id, SessionId = evt.SessionId, EventType = evt.EventType, PayloadJson = evt.PayloadJson, CreatedAt = Next(pre)
        });
        if (held)
            HeldPending.Add(evt.Id);
        return !pre && !held && evt.SessionId != D;
    }

    private void Expect(string kind, string sessionId, string id) => ExpectedRows.Add(new StoredRow(kind, sessionId, id).Key);

    private DateTime Next(bool pre) => pre ? (_pre = _pre.AddSeconds(1)) : (_post = _post.AddSeconds(1));

    public static string TranscriptOf(string sessionId) => $"C:/transcripts/{sessionId}.jsonl";

    private static QueuedEvent Event(string id, string sessionId, EventType type, string payload) =>
        new() { Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload };
}
