using System.Globalization;
using Microsoft.Data.Sqlite;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Whether a session may ship. <see cref="New"/> is the default, so an unset value fails closed.</summary>
public enum AccessState
{
    /// <summary>No start of the session has been decided on this machine.</summary>
    New = 0,

    /// <summary>A start was decided and the session must not ship.</summary>
    Closed,

    /// <summary>The session ships its content from <see cref="SessionAccess.OpenFrom"/> on.</summary>
    Open
}

/// <param name="OpenFrom">Set only when <paramref name="State"/> is <see cref="AccessState.Open"/>; always UTC.</param>
public readonly record struct SessionAccess(AccessState State, DateTime? OpenFrom);

public sealed partial class EventQueue
{
    public Task<SessionAccess> GetAccessAsync(string sessionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT open_from FROM session_access WHERE session_id = $sessionId";
            cmd.Parameters.AddWithValue("$sessionId", sessionId);

            using var reader = cmd.ExecuteReader();
            var access = !reader.Read() ? new SessionAccess(AccessState.New, null)
                : reader.IsDBNull(0) ? new SessionAccess(AccessState.Closed, null)
                : new SessionAccess(AccessState.Open, ParseInstant(reader.GetString(0)));
            return Task.FromResult(access);
        }
    }

    public Task<DateTime> MarkOpenedAsync(QueuedEvent start, DateTime openFrom, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            MarkStartShipped(transaction, start);

            using var upsert = _connection.CreateCommand();
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO session_access (session_id, open_from) VALUES ($sessionId, $openFrom)
                ON CONFLICT(session_id) DO UPDATE SET open_from = excluded.open_from
                WHERE session_access.open_from IS NULL
                """;
            upsert.Parameters.AddWithValue("$sessionId", start.SessionId);
            upsert.Parameters.AddWithValue("$openFrom", UtcInstant.Utc(openFrom).ToString("o"));
            upsert.ExecuteNonQuery();

            using var select = _connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = "SELECT open_from FROM session_access WHERE session_id = $sessionId";
            select.Parameters.AddWithValue("$sessionId", start.SessionId);
            var effective = ParseInstant((string)select.ExecuteScalar()!);

            transaction.Commit();
            return Task.FromResult(effective);
        }
    }

    public Task MarkClosedAsync(QueuedEvent start, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            MarkStartShipped(transaction, start);
            Close(transaction, start);
            transaction.Commit();
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync(QueuedEvent start, CancellationToken ct = default)
    {
        lock (_lock)
            Close(null, start);

        return Task.CompletedTask;
    }

    /// <summary>open_from and created_at are both UTC "o" text, so the text comparison orders by instant. A
    /// session already closed stays closed: NULL &lt; x is not true.</summary>
    private void Close(SqliteTransaction? transaction, QueuedEvent start)
    {
        using var upsert = _connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO session_access (session_id, open_from) VALUES ($sessionId, NULL)
            ON CONFLICT(session_id) DO UPDATE SET open_from = NULL
            WHERE session_access.open_from < $createdAt
            """;
        upsert.Parameters.AddWithValue("$sessionId", start.SessionId);
        upsert.Parameters.AddWithValue("$createdAt", UtcInstant.Utc(start.CreatedAt).ToString("o"));
        upsert.ExecuteNonQuery();
    }

    private void MarkStartShipped(SqliteTransaction transaction, QueuedEvent start)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = $"UPDATE events SET shipped = {Shipped} WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", start.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Runs once per database, guarded by user_version. A queue from before session_access marks a row
    /// shipped only once it was sent, so a session with a shipped row was on the server from its earliest one.</summary>
    private void SeedAccess()
    {
        using var transaction = _connection.BeginTransaction();

        using var version = _connection.CreateCommand();
        version.Transaction = transaction;
        version.CommandText = "PRAGMA user_version";
        if ((long)version.ExecuteScalar()! != 0)
            return;

        using var seed = _connection.CreateCommand();
        seed.Transaction = transaction;
        seed.CommandText = $"""
            INSERT OR IGNORE INTO session_access (session_id, open_from)
            SELECT session_id, MIN(created_at) FROM events WHERE shipped = {Shipped} GROUP BY session_id;
            PRAGMA user_version = 1;
            """;
        seed.ExecuteNonQuery();

        transaction.Commit();
    }

    private static DateTime ParseInstant(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
