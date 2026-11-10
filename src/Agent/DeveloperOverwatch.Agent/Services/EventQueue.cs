using System.Text.Json;
using Microsoft.Data.Sqlite;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

public interface IEventQueue
{
    Task InitializeAsync(CancellationToken ct = default);
    Task EnqueueAsync(QueuedEvent @event, CancellationToken ct = default);
    /// <summary>Up to <paramref name="batchSize"/> pending events: each session's oldest, taken in turn, so one
    /// session's backlog cannot crowd out the others. Each session's share is a contiguous oldest prefix of its
    /// pending events. Sessions in <paramref name="skipSessions"/> are left out.</summary>
    Task<IReadOnlyList<QueuedEvent>> DequeueBatchAsync(
        int batchSize, IReadOnlyCollection<string>? skipSessions = null, CancellationToken ct = default);

    /// <summary>Whether a start of the session already reached the server, as far as this queue knows.
    /// Pending starts do not count.</summary>
    Task<StartState> GetStartStateAsync(string sessionId, CancellationToken ct = default);

    /// <summary>When the session's first start queued after <paramref name="after"/> that may open a held
    /// session (see <see cref="SessionStartPayload.MayOpen"/>) was queued, whatever its state; null when
    /// the queue holds none.</summary>
    Task<DateTime?> GetNextStartAtAsync(string sessionId, DateTime after, CancellationToken ct = default);

    /// <summary>When the start that took the session out of Local was queued; null when it never was Local
    /// or no start has shipped since. Only its turns from this instant on may ship.</summary>
    Task<DateTime?> GetReopenedAtAsync(string sessionId, CancellationToken ct = default);

    Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    /// <summary>Never dequeued again: the server does not know the session. Purged like shipped events.</summary>
    Task MarkDeadAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    /// <summary>Starts handled on this machine (before enrolment, or in Local mode). Kept as long as
    /// unshipped events, so the session's later items stay here after shipped rows are purged.</summary>
    Task MarkLocalAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    Task IncrementRetryAsync(string id, CancellationToken ct = default);
    Task<int> GetUnshippedCountAsync(CancellationToken ct = default);
    Task PurgeShippedAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Events this machine never sent: pending ones it could not, and Local starts it must not.</summary>
    Task PurgeUnshippedAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Whether the session may ship, and from which instant; New when no start has decided it yet.</summary>
    Task<SessionAccess> GetAccessAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Settles <paramref name="start"/> and opens its session from <paramref name="openFrom"/> unless it
    /// is open already; returns the instant the session is open from, since the first opening wins.</summary>
    Task<DateTime> MarkOpenedAsync(QueuedEvent start, DateTime openFrom, CancellationToken ct = default);

    /// <summary>Settles <paramref name="start"/> and closes its session, unless the session opened after the
    /// start was queued.</summary>
    Task MarkClosedAsync(QueuedEvent start, CancellationToken ct = default);
}

/// <summary>What the queue says about a session's settled starts. <see cref="Unknown"/> is the default.</summary>
public enum StartState
{
    /// <summary>No shipped or Local start for the session is queued (purged after shipping, never queued, or
    /// only refused). Its items ship, and the server answers session_not_found if it lacks the session.</summary>
    Unknown,

    /// <summary>A start was shipped: the session is on the server.</summary>
    Shipped,

    /// <summary>The server answered session_not_found earlier in the same walk. The queue never reports it:
    /// a dead-marked start was refused, which does not show the session is missing, since an earlier start
    /// may have reached the server and been purged here.</summary>
    Dead,

    /// <summary>The session began before enrolment, or in Local mode, and no start since has shipped. Its
    /// items are handled on this machine and never sent.</summary>
    Local
}

public sealed partial class EventQueue : IEventQueue, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    public EventQueue(string dbPath, TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        // WAL mode gives better concurrent read performance while hooks write
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS events (
                    rowid INTEGER PRIMARY KEY,
                    id TEXT UNIQUE NOT NULL,
                    session_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    payload_json TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    retry_count INTEGER NOT NULL DEFAULT 0,
                    shipped INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_events_unshipped
                    ON events (shipped, created_at) WHERE shipped = 0;
                CREATE TABLE IF NOT EXISTS session_reopens (
                    session_id TEXT PRIMARY KEY,
                    reopened_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS session_access (
                    session_id TEXT PRIMARY KEY,
                    open_from TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            SeedAccess();
        }

        return Task.CompletedTask;
    }

    public Task EnqueueAsync(QueuedEvent @event, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO events (id, session_id, event_type, payload_json, created_at, retry_count, shipped)
                VALUES ($id, $sessionId, $eventType, $payloadJson, $createdAt, $retryCount, $shipped)
                """;
            cmd.Parameters.AddWithValue("$id", @event.Id);
            cmd.Parameters.AddWithValue("$sessionId", @event.SessionId);
            cmd.Parameters.AddWithValue("$eventType", @event.EventType.ToString());
            cmd.Parameters.AddWithValue("$payloadJson", @event.PayloadJson);
            cmd.Parameters.AddWithValue("$createdAt", ToUtc(@event.CreatedAt).ToString("o"));
            cmd.Parameters.AddWithValue("$retryCount", @event.RetryCount);
            cmd.Parameters.AddWithValue("$shipped", @event.Shipped ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    /// <summary>created_at is compared as text, which orders by instant only when every row is UTC. An
    /// unspecified kind is taken as UTC already, as the hooks write it.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    public Task<IReadOnlyList<QueuedEvent>> DequeueBatchAsync(
        int batchSize, IReadOnlyCollection<string>? skipSessions = null, CancellationToken ct = default)
    {
        var results = new List<QueuedEvent>();

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT rowid, id, session_id, event_type, payload_json, created_at, retry_count, shipped
                FROM (
                    SELECT rowid, id, session_id, event_type, payload_json, created_at, retry_count, shipped,
                        ROW_NUMBER() OVER (PARTITION BY session_id ORDER BY created_at, rowid) AS turn
                    FROM events
                    WHERE shipped = 0 AND session_id NOT IN (SELECT value FROM json_each($skip))
                )
                ORDER BY turn, created_at, rowid
                LIMIT $batchSize
                """;
            cmd.Parameters.AddWithValue("$batchSize", batchSize);
            cmd.Parameters.AddWithValue("$skip", JsonSerializer.Serialize(skipSessions ?? []));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(ReadEvent(reader));
        }

        return Task.FromResult<IReadOnlyList<QueuedEvent>>(results);
    }

    /// <summary>A resumed session queues another start, so a shipped start anywhere means the session
    /// is on the server and wins over a Local one, and nothing of a session that began before enrolment is
    /// sent until a later start ships. Dead starts count as Unknown. Pending starts are left out: a batch holds the oldest
    /// prefix of each session's pending events, so every pending start of a session older than its events in
    /// a batch is in that batch.</summary>
    public Task<StartState> GetStartStateAsync(string sessionId, CancellationToken ct = default)
    {
        var states = new HashSet<int>();
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT DISTINCT shipped FROM events WHERE session_id = $sessionId AND event_type = $eventType AND shipped <> {Pending}";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$eventType", nameof(EventType.SessionStart));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                states.Add(reader.GetInt32(0));
        }

        var state = states.Contains(Shipped) ? StartState.Shipped
            : states.Contains(Local) ? StartState.Local
            : StartState.Unknown;
        return Task.FromResult(state);
    }

    public Task<DateTime?> GetNextStartAtAsync(string sessionId, DateTime after, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT created_at, payload_json FROM events
                WHERE session_id = $sessionId AND event_type = $eventType AND created_at > $after
                ORDER BY created_at
                """;
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            cmd.Parameters.AddWithValue("$eventType", nameof(EventType.SessionStart));
            cmd.Parameters.AddWithValue("$after", ToUtc(after).ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (SessionStartPayload.MayOpen(reader.GetString(1), AccessState.Closed))
                    return Task.FromResult<DateTime?>(
                        DateTime.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind));
            }

            return Task.FromResult<DateTime?>(null);
        }
    }

    public Task<DateTime?> GetReopenedAtAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT reopened_at FROM session_reopens WHERE session_id = $sessionId";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);
            return Task.FromResult(cmd.ExecuteScalar() is string at
                ? DateTime.Parse(at, null, System.Globalization.DateTimeStyles.RoundtripKind)
                : (DateTime?)null);
        }
    }

    // Values of the `shipped` column. Only Pending is ever dequeued.
    private const int Pending = 0;
    private const int Shipped = 1;
    private const int Dead = 2;
    private const int Local = 3;

    public Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => SetStateAsync(ids, Shipped);

    public Task MarkDeadAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => SetStateAsync(ids, Dead);

    public Task MarkLocalAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => SetStateAsync(ids, Local);

    /// <summary>A shipped start also releases its session's Local starts to the shipped purge, in the same
    /// transaction: the session is opted in, and must not close again once the shipped start is purged.</summary>
    private Task SetStateAsync(IReadOnlyList<string> ids, int state)
    {
        if (ids.Count == 0)
            return Task.CompletedTask;

        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE events SET shipped = $state WHERE id = $id";
            cmd.Parameters.AddWithValue("$state", state);
            var param = cmd.Parameters.Add("$id", SqliteType.Text);
            cmd.Prepare();

            foreach (var id in ids)
            {
                param.Value = id;
                cmd.ExecuteNonQuery();
            }

            if (state == Shipped)
                ReleaseLocalStarts(transaction, ids);

            transaction.Commit();
        }

        return Task.CompletedTask;
    }

    /// <summary>Also records when each released session reopened, the first time it does: its turns from
    /// before then stay here even though its Local starts no longer say so.</summary>
    private void ReleaseLocalStarts(SqliteTransaction transaction, IReadOnlyList<string> shippedIds)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"""
            INSERT OR IGNORE INTO session_reopens (session_id, reopened_at)
            SELECT s.session_id, MIN(s.created_at) FROM events s
            WHERE s.event_type = $eventType AND s.id IN (SELECT value FROM json_each($ids))
                AND EXISTS (SELECT 1 FROM events l WHERE l.session_id = s.session_id AND l.shipped = {Local})
            GROUP BY s.session_id;
            UPDATE events SET shipped = {Shipped}
            WHERE shipped = {Local} AND session_id IN (
                SELECT session_id FROM events
                WHERE event_type = $eventType AND id IN (SELECT value FROM json_each($ids)))
            """;
        cmd.Parameters.AddWithValue("$eventType", nameof(EventType.SessionStart));
        cmd.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(shippedIds));
        cmd.ExecuteNonQuery();
    }

    public Task IncrementRetryAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE events SET retry_count = retry_count + 1 WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task<int> GetUnshippedCountAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM events WHERE shipped = 0";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return Task.FromResult(count);
        }
    }

    public Task PurgeShippedAsync(TimeSpan olderThan, CancellationToken ct = default) =>
        PurgeAsync($"shipped IN ({Shipped}, {Dead})", olderThan);

    public Task PurgeUnshippedAsync(TimeSpan olderThan, CancellationToken ct = default) =>
        PurgeAsync($"shipped IN ({Pending}, {Local})", olderThan);

    /// <param name="stateFilter">Built only from this class's state constants, never from input.</param>
    private Task PurgeAsync(string stateFilter, TimeSpan olderThan)
    {
        var threshold = _time.GetUtcNow().UtcDateTime.Subtract(olderThan).ToString("o");

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"DELETE FROM events WHERE {stateFilter} AND created_at < $threshold";
            cmd.Parameters.AddWithValue("$threshold", threshold);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    private static QueuedEvent ReadEvent(SqliteDataReader reader) => new()
    {
        RowId = reader.GetInt64(0),
        Id = reader.GetString(1),
        SessionId = reader.GetString(2),
        EventType = Enum.Parse<EventType>(reader.GetString(3)),
        PayloadJson = reader.GetString(4),
        CreatedAt = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        RetryCount = reader.GetInt32(6),
        Shipped = reader.GetInt32(7) == 1
    };

    public void Dispose() => _connection.Dispose();
}
