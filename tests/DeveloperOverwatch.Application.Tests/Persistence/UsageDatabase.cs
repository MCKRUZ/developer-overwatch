namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>An in-memory SQLite database with the real schema, plus builders for the sessions and turns the
/// usage meter reads, so usage queries are checked as SQL rather than against a mock.</summary>
public sealed class UsageDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public UsageDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Db = new OverwatchDbContext(new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(Commands)
            .Options);
        Db.Database.EnsureCreated();
        Repository = new UsageRepository(Db);
    }

    /// <summary>Every command the context has executed, schema creation and seeding included; clear it before
    /// the step a test counts.</summary>
    public CommandLog Commands { get; } = new();

    public OverwatchDbContext Db { get; }

    public UsageRepository Repository { get; }

    public async Task Seed(params Session[] sessions)
    {
        Db.Sessions.AddRange(sessions);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    public static Session Session(
        string id,
        string email = "dev@team.dev",
        string? machine = "laptop",
        string? projectKey = null,
        string? repoPath = "/src/app",
        string agent = "claude-code",
        params ConversationTurn[] turns)
    {
        var session = new Session
        {
            Id = id,
            DeveloperEmail = email,
            MachineName = machine,
            ProjectKey = projectKey,
            RepoPath = repoPath,
            Agent = agent,
            StartedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        foreach (var turn in turns)
        {
            turn.SessionId = id;
            session.ConversationTurns.Add(turn);
        }
        return session;
    }

    public static ConversationTurn Turn(
        DateTime at,
        int input = 0,
        int output = 0,
        int cacheWrite = 0,
        int cacheRead = 0,
        decimal cost = 0m,
        string? model = "claude-sonnet",
        string? branch = "main",
        string? subagent = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Role = "assistant",
        Timestamp = at,
        InputTokens = input,
        OutputTokens = output,
        CacheCreationInputTokens = cacheWrite,
        CacheReadInputTokens = cacheRead,
        EstimatedCostUsd = cost,
        Model = model,
        GitBranch = branch,
        SubagentName = subagent,
    };

    public static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
