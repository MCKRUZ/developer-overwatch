namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Tests.Budgets;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

/// <summary>The real events handler over an in-memory SQLite database with the real schema, so what an
/// ingest stores is read back as rows rather than asserted against a mock. The tracker is cleared after
/// every ingest, so each one starts from the database like a new request.</summary>
public sealed class IngestDatabase : IDisposable
{
    public static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;

    public IngestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Db = new OverwatchDbContext(new DbContextOptionsBuilder<OverwatchDbContext>().UseSqlite(_connection).Options);
        Db.Database.EnsureCreated();
    }

    public OverwatchDbContext Db { get; }

    public async Task<string> StartSession(string id = "s-1")
    {
        Db.Sessions.Add(new Session { Id = id, DeveloperEmail = "dev@team.dev", StartedAt = Now.AddHours(-1) });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return id;
    }

    public async Task<Result<IngestSessionEventsResult>> Ingest(IngestSessionEventsCommand command)
    {
        var handler = new IngestSessionEventsCommandHandler(
            new SessionRepository(Db),
            Db,
            Substitute.For<ITokenPricingCalculator>(),
            new GovernanceViolationRepository(Db),
            Substitute.For<ISessionStaleThresholdProvider>(),
            new FixedTime(Now));
        var result = await handler.Handle(command, CancellationToken.None);
        Db.ChangeTracker.Clear();
        return result;
    }

    public Task<SessionContextSnapshot> StoredSnapshot(string sessionId = "s-1") =>
        Db.SessionContextSnapshots.AsNoTracking().SingleAsync(s => s.SessionId == sessionId);

    /// <summary>Counts a table's rows in SQL, so owned rows left behind by a replacement are seen even though
    /// no entity reaches them.</summary>
    public async Task<long> RowCount(string table)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static ContextSnapshotDto Snapshot(DateTime takenAt, string model = "claude-opus-5-5") => new()
    {
        TakenAt = takenAt,
        ClaudeCodeVersion = "2.1.40",
        Model = model,
        MeasuredContextTokens = 1000,
        Categories =
        [
            new() { Category = ContextCategory.SystemPrompt, Tokens = 100 },
            new() { Category = ContextCategory.McpTools, Server = "github", Tokens = 300 },
            new() { Category = ContextCategory.McpTools, Server = "(other servers)", Tokens = 50 },
            new() { Category = ContextCategory.Conversation, Tokens = 550 },
        ],
        Tools =
        [
            new() { Group = "github", IsMcp = true, ToolCount = 3, TokensPerRequest = 300, Used = false },
            new() { Group = "built-in", IsMcp = false, ToolCount = 12, TokensPerRequest = 900, Used = true },
        ],
        Compactions =
        [
            new() { At = takenAt.AddHours(-2), Trigger = "auto", PreTokens = 180000, PostTokens = 20000 },
            new() { At = takenAt.AddHours(-1), Trigger = "manual", PreTokens = 90000, PostTokens = 15000 },
        ],
    };

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
