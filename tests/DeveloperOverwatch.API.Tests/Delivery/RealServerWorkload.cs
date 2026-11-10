extern alias agent;

namespace DeveloperOverwatch.API.Tests.Delivery;

using System.Text.Json;
using agent::DeveloperOverwatch.Agent.Models;
using agent::DeveloperOverwatch.Agent.Services;
using DeveloperOverwatch.Domain.Enums;

/// <summary>The sessions queued for the real-server run, and what the database must hold for them. Each
/// enqueue records what must be stored, so the expectations cannot drift from the workload. sess-a carries
/// tools, file changes, failures, turns and activity; sess-big has more turns than one request carries and
/// one turn the server rejects; sess-r is resumed and stays active; sess-d began before enrolment and must
/// never reach the server.</summary>
internal sealed class RealServerWorkload(Func<IEventQueue> queue, Dictionary<string, List<CapturedTurn>> transcripts, DateTime pilotSince)
{
    public const string A = "sess-a";
    public const string Big = "sess-big";
    public const string R = "sess-r";
    public const string D = "sess-d";

    public static readonly string PoisonId = TurnId(Big, 117);

    private DateTime _post = pilotSince.AddMinutes(1);
    private DateTime _pre = pilotSince.AddMinutes(-20);

    public HashSet<string> Turns { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Tools { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Failures { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> TurnsPerSession { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> FailuresPerSession { get; } = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, SessionStatus> ExpectedStatuses { get; } = new Dictionary<string, SessionStatus>
    {
        [A] = SessionStatus.Completed,
        [Big] = SessionStatus.Completed,
        [R] = SessionStatus.Active
    };

    public static string TurnId(string sessionId, int index) => StableTurnId.From($"uuid:{sessionId}-{index}");

    public static string TranscriptOf(string sessionId) => $"C:/transcripts/{sessionId}.jsonl";

    public async Task SeedAsync()
    {
        await SessionAAsync();
        await SessionBigAsync();
        await SessionResumedAsync();
        await SessionPreEnrolmentAsync();
    }

    private async Task SessionAAsync()
    {
        await StartAsync(A, "start");
        await ToolAsync(A, "tool", items: 2);
        await FileChangeAsync(A, "file-1", $"{A}:tool-0");
        await FileChangeAsync(A, "file-2", $"{A}:tool-1");
        await FailureAsync(A, "fail-1");
        AddTurns(A, 8, pre: false);
        await MarkerAsync(A, "marker", pre: false);
        await EnqueueAsync($"{A}:activity", A, EventType.ActivityChanged, $$"""{"state":"waiting","at":"{{_post:o}}"}""", pre: false);
        await FailureAsync(A, "fail-2");
        await EndAsync(A, "end");
    }

    private async Task SessionBigAsync()
    {
        await StartAsync(Big, "start");
        AddTurns(Big, 230, pre: false);
        await MarkerAsync(Big, "marker", pre: false);
        await ToolAsync(Big, "tool", items: 1);
        await EndAsync(Big, "end");
    }

    private async Task SessionResumedAsync()
    {
        await StartAsync(R, "start-1");
        await ToolAsync(R, "tool-a", items: 1);
        AddTurns(R, 6, pre: false);
        await MarkerAsync(R, "marker-1", pre: false);
        await EndAsync(R, "end-1");
        await StartAsync(R, "start-2");
        await ToolAsync(R, "tool-b", items: 1);
        AddTurns(R, 3, pre: false);
        await MarkerAsync(R, "marker-2", pre: false);
        await FailureAsync(R, "fail");
    }

    private async Task SessionPreEnrolmentAsync()
    {
        await StartAsync(D, "start", pre: true);
        await ToolAsync(D, "tool", items: 1, pre: true);
        await FailureAsync(D, "fail", pre: true);
        AddTurns(D, 2, pre: true);
        await MarkerAsync(D, "marker", pre: true);
    }

    private void AddTurns(string sessionId, int count, bool pre)
    {
        var path = TranscriptOf(sessionId);
        var transcript = transcripts.TryGetValue(path, out var existing) ? existing : transcripts[path] = [];
        for (var n = 0; n < count; n++)
        {
            var i = transcript.Count;
            var id = TurnId(sessionId, i);
            var role = id == PoisonId ? "" : "user";
            transcript.Add(new CapturedTurn(
                $"uuid:{sessionId}-{i}", role, $"secret {sessionId} turn {i}", Next(pre), InputTokens: 10, OutputTokens: 5, StartLine: i));
            if (!pre && id != PoisonId && Turns.Add(id))
                TurnsPerSession[sessionId] = TurnsPerSession.GetValueOrDefault(sessionId) + 1;
        }
    }

    private Task StartAsync(string sessionId, string name, bool pre = false) =>
        EnqueueAsync($"{sessionId}:{name}", sessionId, EventType.SessionStart, JsonSerializer.Serialize(new
        {
            sessionId, developerEmail = "dev@example.com", repoPath = "C:/repo", startedAt = pre ? _pre : _post
        }), pre);

    private Task EndAsync(string sessionId, string name) =>
        EnqueueAsync($"{sessionId}:{name}", sessionId, EventType.SessionEnd,
            JsonSerializer.Serialize(new { sessionId, endedAt = _post }), pre: false);

    private async Task ToolAsync(string sessionId, string name, int items, bool pre = false)
    {
        var itemIds = Enumerable.Range(0, items).Select(k => $"{sessionId}:{name}-{k}").ToList();
        await EnqueueAsync($"{sessionId}:{name}", sessionId, EventType.ToolExecution, JsonSerializer.Serialize(new
        {
            sessionId,
            toolExecutions = itemIds.Select(id => new { id, toolName = "Read", timestamp = _post }),
            fileChanges = Array.Empty<object>(),
            conversationTurns = Array.Empty<object>()
        }), pre);
        if (!pre)
            Tools.UnionWith(itemIds);
    }

    private async Task FileChangeAsync(string sessionId, string name, string toolExecutionId)
    {
        var id = $"{sessionId}:{name}";
        await EnqueueAsync(id, sessionId, EventType.FileChange, JsonSerializer.Serialize(new
        {
            sessionId,
            toolExecutions = Array.Empty<object>(),
            fileChanges = new[] { new { id, toolExecutionId, filePath = $"src/{name}.cs", changeType = "Modified" } },
            conversationTurns = Array.Empty<object>()
        }), pre: false);
        Files.Add(id);
    }

    private async Task FailureAsync(string sessionId, string name, bool pre = false)
    {
        var id = $"{sessionId}:{name}";
        await EnqueueAsync(id, sessionId, EventType.ToolFailed, $$"""{"toolName":"Bash","at":"{{(pre ? _pre : _post):o}}"}""", pre);
        if (pre)
            return;
        Failures.Add(id);
        FailuresPerSession[sessionId] = FailuresPerSession.GetValueOrDefault(sessionId) + 1;
    }

    private Task MarkerAsync(string sessionId, string name, bool pre) =>
        EnqueueAsync($"{sessionId}:{name}", sessionId, EventType.ConversationTurn,
            JsonSerializer.Serialize(new { transcriptPath = TranscriptOf(sessionId) }), pre);

    private Task EnqueueAsync(string id, string sessionId, EventType type, string payload, bool pre) =>
        queue().EnqueueAsync(new QueuedEvent
        {
            Id = id, SessionId = sessionId, EventType = type, PayloadJson = payload, CreatedAt = Next(pre)
        });

    private DateTime Next(bool pre) => pre ? (_pre = _pre.AddSeconds(1)) : (_post = _post.AddSeconds(1));
}
