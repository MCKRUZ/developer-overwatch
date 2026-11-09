namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

/// <summary>
/// Deployed SQLite databases were created with EnsureCreated, so they have no migration history and
/// only the schema of the code that created them. Startup must bring every such database up to the
/// current model without losing rows.
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseInitializerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteOverwatchDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqliteOverwatchDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task InitializeAsync_EmptyDatabase_CreatesCurrentSchemaWithHistory()
    {
        await using var db = NewContext();

        await DatabaseInitializer.InitializeAsync(db);

        await AssertSchemaMatchesModelAsync(db);
        (await AppliedMigrationsAsync(db)).Should().Equal(
            DatabaseInitializer.SqliteBaselineMigrationId, DatabaseInitializer.SqliteUpgradeMigrationId);
    }

    [Fact]
    public async Task InitializeAsync_DatabaseCreatedByMasterCode_UpgradesInPlaceAndKeepsRows()
    {
        await using (var legacy = NewContext())
            await CreateMasterEraDatabaseAsync(legacy);
        await ExecAsync("""
            INSERT INTO Sessions (Id, DeveloperEmail, Status, StartedAt, CreatedAt)
            VALUES ('busy', 'dev@test.com', 'Completed', '2026-06-01 09:00:00', '2026-06-01 09:00:00'),
                   ('quiet', 'dev@test.com', 'Completed', '2026-06-02 08:00:00', '2026-06-02 08:00:00');
            INSERT INTO ConversationTurns (Id, SessionId, TurnIndex, Role, Content, Timestamp, IsDecision)
            VALUES ('t1', 'busy', 0, 'user', 'hi', '2026-06-01 10:00:00', 0);
            INSERT INTO ToolExecutions (Id, SessionId, ToolName, Timestamp)
            VALUES ('x1', 'busy', 'Bash', '2026-06-01 11:30:00');
            """);

        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db);

        await AssertSchemaMatchesModelAsync(db);
        var sessions = await db.Sessions.AsNoTracking().OrderBy(s => s.Id).ToListAsync();
        sessions.Select(s => s.Id).Should().Equal("busy", "quiet");
        // Same backfill as the SQL Server migration: latest activity, else the start time.
        sessions[0].LastActivityAt.Should().Be(new DateTime(2026, 6, 1, 11, 30, 0));
        sessions[1].LastActivityAt.Should().Be(new DateTime(2026, 6, 2, 8, 0, 0));
        (await db.ConversationTurns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InitializeAsync_DatabaseCreatedByCurrentCode_RecordsHistoryWithoutChanges()
    {
        await using (var legacy = NewContext())
            await legacy.Database.EnsureCreatedAsync();
        await ExecAsync("""
            INSERT INTO Sessions (Id, DeveloperEmail, Status, StartedAt, CreatedAt, LastActivityAt,
                                  TotalInputTokens, TotalOutputTokens, TotalCacheCreationInputTokens,
                                  TotalCacheReadInputTokens, EstimatedCostUsd)
            VALUES ('s1', 'dev@test.com', 'Completed', '2026-10-01 09:00:00', '2026-10-01 09:00:00',
                    '2026-10-01 09:30:00', 10, 20, 30, 40, 1.5);
            """);

        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db);

        (await AppliedMigrationsAsync(db)).Should().Equal(
            DatabaseInitializer.SqliteBaselineMigrationId, DatabaseInitializer.SqliteUpgradeMigrationId);
        var session = await db.Sessions.AsNoTracking().SingleAsync();
        session.TotalCacheReadInputTokens.Should().Be(40);
        session.LastActivityAt.Should().Be(new DateTime(2026, 10, 1, 9, 30, 0));
    }

    [Fact]
    public async Task InitializeAsync_PartiallyUpgradedDatabase_RefusesToGuess()
    {
        await using (var legacy = NewContext())
            await CreateMasterEraDatabaseAsync(legacy);
        await ExecAsync("ALTER TABLE Sessions ADD COLUMN Title TEXT NULL;");

        await using var db = NewContext();
        var act = () => DatabaseInitializer.InitializeAsync(db);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Sessions*");
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_IsIdempotent()
    {
        await using (var first = NewContext())
            await DatabaseInitializer.InitializeAsync(first);

        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db);

        (await AppliedMigrationsAsync(db)).Should().HaveCount(2);
    }

    // Every model change needs a migration in BOTH sets; a missing SQLite one would leave deployed
    // SQLite databases without the new columns.
    [Fact]
    public void Model_HasMigrationsForEveryChange_InBothProviderSets()
    {
        using var sqlite = NewContext();
        sqlite.Database.HasPendingModelChanges().Should().BeFalse(
            "run: dotnet ef migrations add <Name> --context SqliteOverwatchDbContext -o Persistence/Migrations/Sqlite");

        using var sqlServer = new OverwatchDbContext(new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options);
        sqlServer.Database.HasPendingModelChanges().Should().BeFalse(
            "run: dotnet ef migrations add <Name> --context OverwatchDbContext");
    }

    // A master-era database: the baseline schema (generated from master's model) with no history,
    // exactly what EnsureCreated produced on master.
    private async Task CreateMasterEraDatabaseAsync(SqliteOverwatchDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync(DatabaseInitializer.SqliteBaselineMigrationId);
        await ExecAsync("DROP TABLE __EFMigrationsHistory;");
    }

    private async Task ExecAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> AppliedMigrationsAsync(DbContext db) =>
        (await db.Database.GetAppliedMigrationsAsync()).ToList();

    private async Task AssertSchemaMatchesModelAsync(DbContext db)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName()!;
            var columns = new HashSet<string>(StringComparer.Ordinal);
            await using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

            var expected = entity.GetProperties().Select(p => p.GetColumnName()).ToList();
            columns.Should().Contain(expected, $"table {table} must have every mapped column");
        }
    }
}
