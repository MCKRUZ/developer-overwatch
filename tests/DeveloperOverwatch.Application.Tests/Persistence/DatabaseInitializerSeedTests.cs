namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

/// <summary>
/// The old fixed $50 daily line became a team daily budget. It is seeded exactly once, by the startup
/// that creates the budgets table, so a lead who deletes it does not get it back on the next restart.
/// </summary>
public sealed class DatabaseInitializerSeedTests : IDisposable
{
    private const string LastMasterSqliteMigrationId = "20261109210452_ScopeProcessedAgentEventsToSessionSqlite";
    private readonly SqliteConnection _connection;

    public DatabaseInitializerSeedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteOverwatchDbContext NewContext() => NewContext(_connection);

    private static SqliteOverwatchDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<SqliteOverwatchDbContext>().UseSqlite(connection).Options);

    [Fact]
    public async Task InitializeAsync_FreshDatabase_SeedsOneTeamDailyBudgetWithTheConfiguredAmount()
    {
        await using (var db = NewContext())
            await DatabaseInitializer.InitializeAsync(db, 50m);

        var budget = await SingleBudgetAsync();
        budget.Scope.Should().Be(BudgetScope.Team);
        budget.SubjectKey.Should().Be(Budget.TeamSubjectKey);
        budget.Period.Should().Be(BudgetPeriod.Daily);
        budget.AmountUsd.Should().Be(50m);
        budget.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        budget.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task InitializeAsync_RunTwice_SeedsOnlyOnce()
    {
        await using (var first = NewContext())
            await DatabaseInitializer.InitializeAsync(first, 50m);
        await using (var second = NewContext())
            await DatabaseInitializer.InitializeAsync(second, 75m);

        (await SingleBudgetAsync()).AmountUsd.Should().Be(50m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task InitializeAsync_NonPositiveSeedAmount_SeedsNothing(int amount)
    {
        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db, amount);

        (await db.Budgets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_DatabaseMigratedByMaster_UpgradesKeepingRowsAndSeeds()
    {
        await using (var master = NewContext())
            await master.GetService<IMigrator>().MigrateAsync(LastMasterSqliteMigrationId);
        await ExecAsync("""
            INSERT INTO Sessions (Id, DeveloperEmail, Status, StartedAt, CreatedAt, LastActivityAt,
                                  TotalInputTokens, TotalOutputTokens, TotalCacheCreationInputTokens,
                                  TotalCacheReadInputTokens, EstimatedCostUsd, Agent)
            VALUES ('s1', 'dev@test.com', 'Completed', '2026-11-01 09:00:00', '2026-11-01 09:00:00',
                    '2026-11-01 09:30:00', 10, 20, 30, 40, 1.5, 'claude-code');
            INSERT INTO ConversationTurns (Id, SessionId, TurnIndex, Role, Content, Timestamp, IsDecision, EstimatedCostUsd)
            VALUES ('t1', 's1', 0, 'assistant', 'hi', '2026-11-01 09:10:00', 0, 0.75);
            """);

        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db, 50m);

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(db.Database.GetMigrations());
        var session = await db.Sessions.AsNoTracking().SingleAsync();
        session.Id.Should().Be("s1");
        session.TotalCacheReadInputTokens.Should().Be(40);
        session.EstimatedCostUsd.Should().Be(1.5m);
        (await db.ConversationTurns.AsNoTracking().SingleAsync()).EstimatedCostUsd.Should().Be(0.75m);
        (await SingleBudgetAsync()).AmountUsd.Should().Be(50m);
    }

    // A history-less database from the EnsureCreated era is adopted first; the budgets table is still
    // pending afterwards, so it is seeded too.
    [Fact]
    public async Task InitializeAsync_EnsureCreatedEraDatabase_IsAdoptedAndSeeded()
    {
        await using (var legacy = NewContext())
            await legacy.GetService<IMigrator>().MigrateAsync(DatabaseInitializer.SqliteBaselineMigrationId);
        await ExecAsync("DROP TABLE __EFMigrationsHistory;");

        await using (var db = NewContext())
            await DatabaseInitializer.InitializeAsync(db, 50m);

        (await SingleBudgetAsync()).AmountUsd.Should().Be(50m);
    }

    // Each restart is a new process: a new connection to the same file.
    [Fact]
    public async Task InitializeAsync_SeedDeletedByLead_StaysGoneAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"overwatch-seed-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            await RestartAsync(connectionString, async db =>
            {
                var seeded = await db.Budgets.SingleAsync();
                db.Budgets.Remove(seeded);
                await db.SaveChangesAsync();
            });

            await RestartAsync(connectionString, async db =>
                (await db.Budgets.CountAsync()).Should().Be(0));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    // A regenerated migration gets a new id; a stale constant would silently stop the seed.
    [Fact]
    public void BudgetsMigrationIds_ExistInTheirProviderSets()
    {
        using var sqlite = NewContext();
        sqlite.Database.GetMigrations().Should().Contain(DatabaseInitializer.SqliteBudgetsMigrationId);

        using var sqlServer = new OverwatchDbContext(new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlServer("Server=unused;Database=unused").Options);
        sqlServer.Database.GetMigrations().Should().Contain(DatabaseInitializer.SqlServerBudgetsMigrationId);
    }

    private static async Task RestartAsync(string connectionString, Func<SqliteOverwatchDbContext, Task> afterStartup)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var db = NewContext(connection);
        await DatabaseInitializer.InitializeAsync(db, 50m);
        await afterStartup(db);
    }

    private async Task<Budget> SingleBudgetAsync()
    {
        await using var db = NewContext();
        return await db.Budgets.AsNoTracking().SingleAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
