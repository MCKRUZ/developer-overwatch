using System.Net;
using System.Text;
using System.Text.Json;
using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Tests.Services;

internal enum InvariantFault
{
    // Drawn at random, only while the server is faulting.
    CommitThenTimeout,
    ServerError,
    NotFoundWithoutCode,
    CommitThenDie,
    WholeRequestRejected,
    CommitThenResultsMissingIds,
    CommitThenHtml,
    CursorWriteThenDie,

    // Properties of the server, on every attempt.
    PoisonRejectedAlone,
    OverItemLimit,
    HeldTooLargeAlone
}

internal sealed class AgentDiedException(string where) : Exception($"injected: the agent died {where}");

/// <summary>The seeded fault schedule, shared by the fake server and the dying cursor store, and a tally of
/// every fault that actually fired.</summary>
internal sealed class FaultPlan(int seed, int faultPolls, double faultRate, double cursorDeathRate)
{
    private static readonly InvariantFault[] RequestFaults =
    [
        InvariantFault.CommitThenTimeout, InvariantFault.ServerError, InvariantFault.NotFoundWithoutCode,
        InvariantFault.CommitThenDie, InvariantFault.WholeRequestRejected, InvariantFault.CommitThenResultsMissingIds,
        InvariantFault.CommitThenHtml
    ];

    private readonly Random _random = new(seed);

    public int Poll { get; set; }

    public bool Faulting => Poll <= faultPolls;

    public Dictionary<InvariantFault, int> Fired { get; } = [];

    public void Record(InvariantFault fault) => Fired[fault] = Fired.GetValueOrDefault(fault) + 1;

    /// <returns>A candidate fault for one request; the server applies it only where it fits.</returns>
    public InvariantFault? NextRequestFault() =>
        Faulting && _random.NextDouble() < faultRate ? RequestFaults[_random.Next(RequestFaults.Length)] : null;

    public bool DiesAfterCursorWrite() => Faulting && _random.NextDouble() < cursorDeathRate;
}

internal enum FakeSessionStatus
{
    Active,
    Completed
}

/// <summary>The real server's status rules: a start activates a new session and re-activates an existing one
/// unless it is a backfill start; an end completes only an active session.</summary>
internal static class SessionStatusRules
{
    public static FakeSessionStatus? Next(FakeSessionStatus? current, bool isStart, bool isBackfill) =>
        isStart
            ? (current is null || !isBackfill ? FakeSessionStatus.Active : current)
            : (current == FakeSessionStatus.Active ? FakeSessionStatus.Completed : current);
}

internal sealed record PostedItem(string Kind, string Id, bool HasText);

internal sealed record Attempt(int Poll, string Path, string Body, IReadOnlyList<PostedItem> Items);

internal sealed record StoredRow(string Kind, string SessionId, string Id)
{
    public string Key => $"{Kind}:{SessionId}:{Id}";
}

/// <summary>Holds what the real server would store, with its dedupe keys: turns by id across sessions; tool
/// executions, file changes and tool failures by id within the session; starts and ends by eventId within
/// the session. It tracks session status, logs every attempt, refuses the poison item with a 400 and the held
/// turn with a 413 on every attempt, refuses a payload over the item limit with a 413, and injects the plan's
/// faults. A 400 or 413 commits nothing.</summary>
internal sealed class InvariantFakeServer(FaultPlan plan, string poisonId, string heldId, int maxItems) : HttpMessageHandler
{
    private static readonly (string Kind, string List)[] ItemLists =
        [("turn", "conversationTurns"), ("tool", "toolExecutions"), ("file", "fileChanges"), ("failure", "toolFailures")];

    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    public FaultPlan Plan => plan;

    public Dictionary<string, FakeSessionStatus> Sessions { get; } = new(StringComparer.Ordinal);

    public List<Attempt> Attempts { get; } = [];

    public List<StoredRow> Rows { get; } = [];

    public HashSet<string> TurnsStoredWithText { get; } = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = await request.Content!.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var items = ItemsOf(doc.RootElement);
        Attempts.Add(new Attempt(plan.Poll, path, body, items));

        var fault = Applicable(plan.NextRequestFault(), path, items);
        if (fault is { } fired)
            plan.Record(fired);
        switch (fault)
        {
            case InvariantFault.ServerError:
                return Json(HttpStatusCode.InternalServerError, "{}");
            case InvariantFault.NotFoundWithoutCode:
                return Json(HttpStatusCode.NotFound, "{}");
            case InvariantFault.WholeRequestRejected:
                return Json(HttpStatusCode.BadRequest, """{"title":"injected: invalid request"}""");
        }

        if (Refusal(items) is { } refusal)
            return refusal;

        var response = Commit(path, doc.RootElement, items, omitIds: fault == InvariantFault.CommitThenResultsMissingIds);
        return fault switch
        {
            InvariantFault.CommitThenTimeout => throw new TaskCanceledException("injected: committed, then timed out"),
            InvariantFault.CommitThenDie => throw new AgentDiedException("between a send and its mark"),
            InvariantFault.CommitThenHtml => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body>Signed out</body></html>", Encoding.UTF8, "text/html")
            },
            _ => response
        };
    }

    /// <summary>A random whole-request 400 goes only to a request the agent can split, so no legitimate item is
    /// ever refused alone; missing ids need an <c>/events</c> reply with results.</summary>
    private static InvariantFault? Applicable(InvariantFault? fault, string path, IReadOnlyList<PostedItem> items) => fault switch
    {
        InvariantFault.WholeRequestRejected when items.Count < 2 => null,
        InvariantFault.CommitThenResultsMissingIds when !path.EndsWith("/events", StringComparison.Ordinal) || items.Count == 0 => null,
        _ => fault
    };

    private HttpResponseMessage? Refusal(IReadOnlyList<PostedItem> items)
    {
        if (items.Any(i => i.Id == poisonId))
        {
            if (items.Count == 1)
                plan.Record(InvariantFault.PoisonRejectedAlone);
            return Json(HttpStatusCode.BadRequest, """{"title":"invalid item"}""");
        }

        if (items.Any(i => i.Id == heldId))
        {
            if (items.Count == 1)
                plan.Record(InvariantFault.HeldTooLargeAlone);
            return Json(HttpStatusCode.RequestEntityTooLarge, "{}");
        }

        if (items.Count <= maxItems)
            return null;
        plan.Record(InvariantFault.OverItemLimit);
        return Json(HttpStatusCode.RequestEntityTooLarge, "{}");
    }

    private HttpResponseMessage Commit(string path, JsonElement root, IReadOnlyList<PostedItem> items, bool omitIds)
    {
        if (path == "/api/sessions/start")
            return Start(root);

        var sessionId = path.Split('/')[3];
        if (!Sessions.ContainsKey(sessionId))
            return Json(HttpStatusCode.NotFound, """{"code":"session_not_found"}""");
        if (path.EndsWith("/end", StringComparison.Ordinal))
            return End(sessionId, root);

        var accepted = new List<string>();
        var duplicate = new List<string>();
        foreach (var item in items)
            (Store(sessionId, item) ? accepted : duplicate).Add(item.Id);

        // Every other id, starting with the first, is left out of the reply.
        var omitted = omitIds ? items.Where((_, i) => i % 2 == 0).Select(i => i.Id).ToHashSet() : [];
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            eventsIngested = accepted.Count,
            results = new
            {
                accepted = accepted.Where(id => !omitted.Contains(id)),
                duplicate = duplicate.Where(id => !omitted.Contains(id)),
                rejected = Array.Empty<object>()
            }
        }));
    }

    private HttpResponseMessage Start(JsonElement root)
    {
        var sessionId = root.GetProperty("sessionId").GetString()!;
        var eventId = root.GetProperty("eventId").GetString()!;
        if (!_keys.Add($"start|{sessionId}|{eventId}"))
            return SessionOutcome(sessionId, "duplicate");

        Rows.Add(new StoredRow("start", sessionId, eventId));
        var isBackfill = root.TryGetProperty("isBackfill", out var flag) && flag.ValueKind == JsonValueKind.True;
        Sessions[sessionId] = SessionStatusRules.Next(Sessions.TryGetValue(sessionId, out var s) ? s : null, true, isBackfill)!.Value;
        return SessionOutcome(sessionId, "accepted");
    }

    private HttpResponseMessage End(string sessionId, JsonElement root)
    {
        var eventId = root.GetProperty("eventId").GetString()!;
        if (!_keys.Add($"end|{sessionId}|{eventId}"))
            return SessionOutcome(sessionId, "duplicate");

        Rows.Add(new StoredRow("end", sessionId, eventId));
        Sessions[sessionId] = SessionStatusRules.Next(Sessions[sessionId], false, false)!.Value;
        return SessionOutcome(sessionId, "accepted");
    }

    /// <returns>True when the item is new to the store.</returns>
    private bool Store(string sessionId, PostedItem item)
    {
        var key = item.Kind == "turn" ? $"turn|{item.Id}" : $"{item.Kind}|{sessionId}|{item.Id}";
        if (!_keys.Add(key))
            return false;
        Rows.Add(new StoredRow(item.Kind, sessionId, item.Id));
        if (item.HasText)
            TurnsStoredWithText.Add(item.Id);
        return true;
    }

    private static List<PostedItem> ItemsOf(JsonElement root) =>
        ItemLists
            .Where(l => root.TryGetProperty(l.List, out _))
            .SelectMany(l => root.GetProperty(l.List).EnumerateArray().Select(i => new PostedItem(
                l.Kind,
                i.GetProperty("id").GetString()!,
                i.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)))
            .ToList();

    private static HttpResponseMessage SessionOutcome(string sessionId, string outcome) =>
        Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { sessionId, outcome }));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>The real cursor store, except that the agent may die right after a cursor write, before the
/// marker it belongs to is marked.</summary>
internal sealed class DyingCursorStore(IConversationCursorStore inner, FaultPlan plan) : IConversationCursorStore
{
    public Task<int> GetAsync(string sessionId, CancellationToken ct) => inner.GetAsync(sessionId, ct);

    public async Task SetAsync(string sessionId, int lineCount, CancellationToken ct)
    {
        await inner.SetAsync(sessionId, lineCount, ct);
        if (!plan.DiesAfterCursorWrite())
            return;
        plan.Record(InvariantFault.CursorWriteThenDie);
        throw new AgentDiedException("between a cursor write and its marker's mark");
    }
}
