using DeveloperOverwatch.Agent.Models;
using Microsoft.Data.Sqlite;

namespace DeveloperOverwatch.Agent.Services;

public interface IUsageStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task SetSessionProjectAsync(string sessionId, string? projectKey, CancellationToken ct = default);
    /// <summary>Writes one row per model response that carries usage; a response already stored is left as is.</summary>
    Task<RecordedUsage> RecordTurnsAsync(string sessionId, IReadOnlyList<CapturedTurn> turns, CancellationToken ct = default);

    /// <summary>Rows with <paramref name="fromUtc"/> &lt;= timestamp &lt; <paramref name="toUtc"/>.</summary>
    Task<IReadOnlyList<UsageRecord>> GetUsageAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Deletes usage older than <paramref name="minimumRetention"/>, but never from the current local
    /// month, so the usage report's "This month" stays whole even on the 31st.</summary>
    Task PurgeAsync(TimeSpan minimumRetention, CancellationToken ct = default);
}

/// <summary>
/// Token usage kept on this machine in queue.db, one row per API response, written in both modes.
/// It holds counts and labels only — never prompt or response text. A row is keyed by the turn's
/// stable source id, so re-capturing a transcript after a failed send cannot count it twice.
/// </summary>
public sealed class UsageStore : IUsageStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();

    public UsageStore(string dbPath, TimeProvider? timeProvider = null)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _time = timeProvider ?? TimeProvider.System;
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;");
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS usage (
                source_id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                model TEXT,
                input_tokens INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                cache_read_tokens INTEGER NOT NULL,
                cache_creation_tokens INTEGER NOT NULL,
                project_key TEXT,
                git_branch TEXT,
                subagent_name TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_usage_timestamp ON usage (timestamp);
            CREATE TABLE IF NOT EXISTS session_project (
                session_id TEXT PRIMARY KEY,
                project_key TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
        return Task.CompletedTask;
    }

    public Task SetSessionProjectAsync(string sessionId, string? projectKey, CancellationToken ct = default)
    {
        if (projectKey is null)
            return Task.CompletedTask;

        Execute("""
            INSERT INTO session_project (session_id, project_key, created_at) VALUES ($sessionId, $projectKey, $now)
            ON CONFLICT(session_id) DO UPDATE SET project_key = excluded.project_key, created_at = excluded.created_at
            """,
            ("$sessionId", sessionId), ("$projectKey", projectKey), ("$now", ToUtcIso(Now)));
        return Task.CompletedTask;
    }

    public Task<RecordedUsage> RecordTurnsAsync(string sessionId, IReadOnlyList<CapturedTurn> turns, CancellationToken ct = default)
    {
        var withUsage = turns.Where(HasUsage).ToList();
        if (withUsage.Count == 0)
            return Task.FromResult(new RecordedUsage(0, 0));

        var added = 0;
        lock (_lock)
        {
            using var transaction = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR IGNORE INTO usage (source_id, session_id, timestamp, model, input_tokens, output_tokens,
                    cache_read_tokens, cache_creation_tokens, project_key, git_branch, subagent_name)
                VALUES ($sourceId, $sessionId, $timestamp, $model, $input, $output, $cacheRead, $cacheCreate,
                    (SELECT project_key FROM session_project WHERE session_id = $sessionId), $gitBranch, $subagentName)
                """;

            foreach (var turn in withUsage)
            {
                cmd.Parameters.Clear();
                AddParameters(cmd,
                    ("$sourceId", turn.SourceId), ("$sessionId", sessionId),
                    ("$timestamp", ToUtcIso(turn.Timestamp ?? Now)), ("$model", turn.Model),
                    ("$input", turn.InputTokens ?? 0), ("$output", turn.OutputTokens ?? 0),
                    ("$cacheRead", turn.CacheReadInputTokens ?? 0), ("$cacheCreate", turn.CacheCreationInputTokens ?? 0),
                    ("$gitBranch", turn.GitBranch), ("$subagentName", turn.SubagentName));
                added += cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return Task.FromResult(new RecordedUsage(added, withUsage.Count - added));
    }

    public Task<IReadOnlyList<UsageRecord>> GetUsageAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var rows = new List<UsageRecord>();
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT source_id, session_id, timestamp, model, input_tokens, output_tokens,
                    cache_read_tokens, cache_creation_tokens, project_key, git_branch, subagent_name
                FROM usage
                WHERE timestamp >= $from AND timestamp < $to
                ORDER BY timestamp, source_id
                """;
            AddParameters(cmd, ("$from", ToUtcIso(fromUtc)), ("$to", ToUtcIso(toUtc)));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(new UsageRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    DateTime.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return Task.FromResult<IReadOnlyList<UsageRecord>>(rows);
    }

    public Task PurgeAsync(TimeSpan minimumRetention, CancellationToken ct = default)
    {
        var retentionStart = Now - minimumRetention;
        var monthStart = LocalCalendar.MonthStartUtc(Now, _time.LocalTimeZone);
        var cutoff = retentionStart < monthStart ? retentionStart : monthStart;

        Execute("""
            DELETE FROM usage WHERE timestamp < $cutoff;
            DELETE FROM session_project WHERE created_at < $cutoff;
            """,
            ("$cutoff", ToUtcIso(cutoff)));
        return Task.CompletedTask;
    }

    public void Dispose() => _connection.Dispose();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static bool HasUsage(CapturedTurn t) =>
        t.Role is "assistant" or "subagent"
        && (t.InputTokens ?? t.OutputTokens ?? t.CacheReadInputTokens ?? t.CacheCreationInputTokens) is not null;

    // Range queries and purges compare these strings, so every value must be UTC in one format.
    private static string ToUtcIso(DateTime value) => UtcInstant.Utc(value).ToString("o");

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            AddParameters(cmd, parameters);
            cmd.ExecuteNonQuery();
        }
    }

    private static void AddParameters(SqliteCommand cmd, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
