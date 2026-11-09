namespace DeveloperOverwatch.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Brings the database schema up to the current model at startup.</summary>
public static class DatabaseInitializer
{
    public const string SqliteBaselineMigrationId = "20261109143852_SqliteBaseline";
    public const string SqliteUpgradeMigrationId = "20261109143930_SqliteTokenUsageAndActivity";

    // Columns the upgrade migration adds. A history-less database has either all of them (created by
    // code that already had them) or none (created by master).
    private static readonly IReadOnlyDictionary<string, string[]> UpgradeColumns = new Dictionary<string, string[]>
    {
        ["Sessions"] =
        [
            "EstimatedCostUsd", "LastActivityAt", "Title", "TotalCacheCreationInputTokens",
            "TotalCacheReadInputTokens", "TotalInputTokens", "TotalOutputTokens",
        ],
        ["ConversationTurns"] =
        [
            "CacheCreationInputTokens", "CacheReadInputTokens", "ContainsRedactedSecret",
            "EstimatedCostUsd", "InputTokens", "Model", "OutputTokens",
        ],
        ["GovernanceViolations"] = ["ConversationTurnId", "ToolExecutionId"],
    };

    public static async Task InitializeAsync(OverwatchDbContext db, CancellationToken ct = default)
    {
        if (db is SqliteOverwatchDbContext)
            await AdoptEnsureCreatedSqliteDatabaseAsync(db, ct);

        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// SQLite databases used to be created with EnsureCreated, so they have tables but no migration
    /// history, and MigrateAsync would try to create the tables again. Record the migrations their
    /// schema already contains, so only the missing ones run.
    /// </summary>
    private static async Task AdoptEnsureCreatedSqliteDatabaseAsync(DbContext db, CancellationToken ct)
    {
        var tables = await ReadNamesAsync(db, "SELECT name FROM sqlite_master WHERE type = 'table'", ct);
        if (!tables.Contains("Sessions") || tables.Contains("__EFMigrationsHistory"))
            return;

        var alreadyApplied = new List<string> { SqliteBaselineMigrationId };
        if (await HasUpgradeColumnsAsync(db, ct))
            alreadyApplied.Add(SqliteUpgradeMigrationId);

        var history = db.GetService<IHistoryRepository>();
        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "unknown";
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
        foreach (var migrationId in alreadyApplied)
            await db.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(migrationId, productVersion)), ct);
    }

    private static async Task<bool> HasUpgradeColumnsAsync(DbContext db, CancellationToken ct)
    {
        var presentCount = 0;
        var expectedCount = 0;
        var partialTables = new List<string>();
        foreach (var (table, upgradeColumns) in UpgradeColumns)
        {
            var columns = await ReadNamesAsync(db, $"SELECT name FROM pragma_table_info('{table}')", ct);
            var present = upgradeColumns.Count(columns.Contains);
            if (present != 0 && present != upgradeColumns.Length)
                partialTables.Add(table);
            presentCount += present;
            expectedCount += upgradeColumns.Length;
        }

        if (partialTables.Count > 0 || (presentCount != 0 && presentCount != expectedCount))
            throw new InvalidOperationException(
                "The SQLite database has no migration history and only part of the current schema " +
                $"(tables: {string.Join(", ", partialTables.DefaultIfEmpty("several"))}). " +
                "Refusing to guess which migrations it contains; back it up and repair the schema by hand.");

        return presentCount == expectedCount;
    }

    private static async Task<HashSet<string>> ReadNamesAsync(DbContext db, string sql, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(ct);
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(ct))
                names.Add(reader.GetString(0));
            return names;
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}
