using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>One session's events from this poll in the order they happened, the walk's position in them,
/// which of them are already marked, and what the session's access is as the walk goes.</summary>
/// <param name="state">The session's access as the queue holds it when the poll begins.</param>
/// <param name="session">Set when <paramref name="state"/> is <see cref="AccessState.Open"/>.</param>
internal sealed class SessionRun(
    IEventQueue queue, IReadOnlyList<QueuedEvent> events, AccessState state, OpenSession? session)
{
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

    public IReadOnlyList<QueuedEvent> Events => events;

    public int At { get; set; }

    public AccessState State { get; set; } = state;

    /// <summary>Null until a start opens the session; its items are kept here until then.</summary>
    public OpenSession? Session { get; set; } = session;

    /// <summary>Set once the server answers session_not_found: the session's items are dropped up to its next
    /// start. Cleared when a start ships.</summary>
    public bool Dead { get; set; }

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

    /// <returns>The instant the session is open from, which an earlier open keeps.</returns>
    public async Task<DateTime> MarkOpenedAsync(QueuedEvent start, DateTime openFrom, CancellationToken ct)
    {
        _marked.Add(start.Id);
        return await queue.MarkOpenedAsync(start, openFrom, ct);
    }

    public async Task MarkClosedAsync(QueuedEvent start, CancellationToken ct)
    {
        _marked.Add(start.Id);
        await queue.MarkClosedAsync(start, ct);
    }

    public async Task MarkDeadAndClosedAsync(QueuedEvent start, CancellationToken ct)
    {
        _marked.Add(start.Id);
        await queue.MarkDeadAndClosedAsync(start, ct);
    }

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
