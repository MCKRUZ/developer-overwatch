using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>An upgraded install has no session_access rows. Its sessions that already shipped must keep shipping
/// from their first shipped row, and the seed must run once only: a later shipped row is decided by its start,
/// not re-seeded.</summary>
public sealed class EventQueueAccessSeedTests : IDisposable
{
    private static readonly DateTime X = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.GetTempFileName() + ".db";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task Initialize_QueueFromBeforeSessionAccess_SeedsShippedSessionsOnce()
    {
        CreateOldQueue(
            ("a-pending", "shipped-session", X.AddMinutes(-10), 0),
            ("a-1", "shipped-session", X, 1),
            ("a-2", "shipped-session", X.AddMinutes(10), 1),
            ("p-1", "pending-session", X, 0));

        using (var queue = new EventQueue(_dbPath))
        {
            await queue.InitializeAsync();

            (await queue.GetAccessAsync("shipped-session")).Should().Be(new SessionAccess(AccessState.Open, X));
            (await queue.GetAccessAsync("pending-session")).Should().Be(new SessionAccess(AccessState.New, null));

            await queue.EnqueueAsync(new QueuedEvent
            {
                Id = "c-1",
                SessionId = "later-session",
                EventType = EventType.ToolExecution,
                PayloadJson = "{}",
                CreatedAt = X,
                Shipped = true
            });
            await queue.InitializeAsync();
            (await queue.GetAccessAsync("later-session")).State.Should().Be(AccessState.New);
        }

        using var restarted = new EventQueue(_dbPath);
        await restarted.InitializeAsync();

        (await restarted.GetAccessAsync("later-session")).State.Should().Be(AccessState.New);
        (await restarted.GetAccessAsync("shipped-session")).Should().Be(new SessionAccess(AccessState.Open, X));
    }

    [Fact]
    public async Task Initialize_FreshDatabase_LaterShippedRowIsNotSeeded()
    {
        using (var queue = new EventQueue(_dbPath))
            await queue.InitializeAsync();

        CreateOldQueue(("a-1", "s1", X, 1));

        using var restarted = new EventQueue(_dbPath);
        await restarted.InitializeAsync();

        (await restarted.GetAccessAsync("s1")).State.Should().Be(AccessState.New);
    }

    // Earlier builds of this branch marked a start held in Local mode with shipped = 3. A session holding one
    // was used in Local mode, so its shipped rows do not show it was on the server from the earliest of them. Left
    // New, a later clear or no-source start could open it at pilotSince, so it is seeded Closed.
    [Fact]
    public async Task Initialize_SessionHoldingALegacyLocalRow_IsSeededClosed()
    {
        CreateOldQueue(
            ("a-1", "shipped-session", X, 1),
            ("l-local", "local-session", X.AddMinutes(-5), LegacyLocal),
            ("l-1", "local-session", X, 1));

        using var queue = new EventQueue(_dbPath);
        await queue.InitializeAsync();

        (await queue.GetAccessAsync("local-session")).Should().Be(new SessionAccess(AccessState.Closed, null));
        (await queue.GetAccessAsync("shipped-session")).Should().Be(new SessionAccess(AccessState.Open, X));
    }

    // Nothing writes shipped = 3 any more and nothing dequeues it, so only the shipped purge can clear it.
    [Fact]
    public async Task PurgeShipped_LeftoverLegacyLocalRow_IsDeleted()
    {
        CreateOldQueue(("l-local", "local-session", X, LegacyLocal), ("p-1", "pending-session", X, 0));

        using var queue = new EventQueue(_dbPath);
        await queue.InitializeAsync();
        await queue.PurgeShippedAsync(TimeSpan.Zero);

        RowIds().Should().Equal("p-1");
    }

    private const int LegacyLocal = 3;

    private IReadOnlyList<string> RowIds()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id FROM events ORDER BY id";
        using var reader = select.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>The events table exactly as EventQueue creates it, written without EventQueue.</summary>
    private void CreateOldQueue(params (string Id, string SessionId, DateTime CreatedAt, int Shipped)[] rows)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = """
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
        create.ExecuteNonQuery();

        foreach (var row in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO events (id, session_id, event_type, payload_json, created_at, shipped)
                VALUES ($id, $sessionId, 'ToolExecution', '{}', $createdAt, $shipped)
                """;
            insert.Parameters.AddWithValue("$id", row.Id);
            insert.Parameters.AddWithValue("$sessionId", row.SessionId);
            insert.Parameters.AddWithValue("$createdAt", row.CreatedAt.ToString("o"));
            insert.Parameters.AddWithValue("$shipped", row.Shipped);
            insert.ExecuteNonQuery();
        }
    }
}
