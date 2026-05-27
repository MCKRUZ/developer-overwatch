using Microsoft.Data.Sqlite;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

public interface IEventQueue
{
    Task InitializeAsync(CancellationToken ct = default);
    Task EnqueueAsync(QueuedEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<QueuedEvent>> DequeueBatchAsync(int batchSize, CancellationToken ct = default);
    Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default);
    Task IncrementRetryAsync(string id, CancellationToken ct = default);
    Task<int> GetUnshippedCountAsync(CancellationToken ct = default);
    Task PurgeShippedAsync(TimeSpan olderThan, CancellationToken ct = default);
}

public sealed class EventQueue : IEventQueue, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();

    public EventQueue(string dbPath)
    {
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
                """;
            cmd.ExecuteNonQuery();
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
            cmd.Parameters.AddWithValue("$createdAt", @event.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$retryCount", @event.RetryCount);
            cmd.Parameters.AddWithValue("$shipped", @event.Shipped ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QueuedEvent>> DequeueBatchAsync(int batchSize, CancellationToken ct = default)
    {
        var results = new List<QueuedEvent>();

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT rowid, id, session_id, event_type, payload_json, created_at, retry_count, shipped
                FROM events
                WHERE shipped = 0
                ORDER BY created_at
                LIMIT $batchSize
                """;
            cmd.Parameters.AddWithValue("$batchSize", batchSize);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                results.Add(ReadEvent(reader));
        }

        return Task.FromResult<IReadOnlyList<QueuedEvent>>(results);
    }

    public Task MarkShippedAsync(IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return Task.CompletedTask;

        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();

            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE events SET shipped = 1 WHERE id = $id";
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

    public Task PurgeShippedAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.Subtract(olderThan).ToString("o");

        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM events WHERE shipped = 1 AND created_at < $threshold";
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
