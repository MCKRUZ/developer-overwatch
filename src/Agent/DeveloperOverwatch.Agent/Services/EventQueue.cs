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

    /// <summary>When the session's first start queued after <paramref name="after"/> that may open a held
    /// session (see <see cref="SessionStartPayload.MayOpen"/>) was queued, whatever its state; null when
    /// the queue holds none.</summary>
    Task<DateTime?> GetNextStartAtAsync(string sessionId, DateTime after, CancellationToken ct = default);

    Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    /// <summary>Never dequeued again: the server refused it or does not know the session. Purged like shipped
    /// events.</summary>
    Task MarkDeadAsync(IReadOnlyList<string> ids, CancellationToken ct = default);

    Task IncrementRetryAsync(string id, CancellationToken ct = default);
    Task<int> GetUnshippedCountAsync(CancellationToken ct = default);
    Task PurgeShippedAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Pending events this machine could not send. A session's access outlives every purge.</summary>
    Task PurgeUnshippedAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Whether the session may ship, and from which instant; New when no start has decided it yet.</summary>
    Task<SessionAccess> GetAccessAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Settles <paramref name="start"/> and opens its session from <paramref name="openFrom"/> unless it
    /// is open already; returns the instant the session is open from, since the first opening wins.</summary>
    Task<DateTime> MarkOpenedAsync(QueuedEvent start, DateTime openFrom, CancellationToken ct = default);

    /// <summary>Settles <paramref name="start"/> and closes its session, unless the session opened after the
    /// start was queued.</summary>
    Task MarkClosedAsync(QueuedEvent start, CancellationToken ct = default);

    /// <summary>Closes the session of <paramref name="start"/> like <see cref="MarkClosedAsync"/>, but leaves
    /// the start's own state alone.</summary>
    Task CloseAsync(QueuedEvent start, CancellationToken ct = default);

    /// <summary>Closes every session opened before <paramref name="pilotSince"/>: it was opened in an earlier
    /// enrolment, or with no cutoff, and may have been used in Local mode since.</summary>
    Task CloseOpenedBeforeAsync(DateTime pilotSince, CancellationToken ct = default);
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
            cmd.Parameters.AddWithValue("$createdAt", UtcInstant.Utc(@event.CreatedAt).ToString("o"));
            cmd.Parameters.AddWithValue("$retryCount", @event.RetryCount);
            cmd.Parameters.AddWithValue("$shipped", @event.Shipped ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

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
            cmd.Parameters.AddWithValue("$after", UtcInstant.Utc(after).ToString("o"));

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

    // Values of the `shipped` column. Only Pending is ever dequeued.
    private const int Pending = 0;
    private const int Shipped = 1;
    private const int Dead = 2;

    // Written only by earlier builds of this branch, for a start held in Local mode; never written now.
    private const int LegacyLocal = 3;

    public Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => SetStateAsync(ids, Shipped);

    public Task MarkDeadAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => SetStateAsync(ids, Dead);

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

            transaction.Commit();
        }

        return Task.CompletedTask;
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
        PurgeAsync($"shipped IN ({Shipped}, {Dead}, {LegacyLocal})", olderThan);

    public Task PurgeUnshippedAsync(TimeSpan olderThan, CancellationToken ct = default) =>
        PurgeAsync($"shipped = {Pending}", olderThan);

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
