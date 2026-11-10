namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetRunningSessions;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>Runs the handler against the real repository on in-memory SQLite, so the running filter,
/// the current-context lookup and the burn window are checked as SQL, not as a mock's echo.</summary>
public sealed class GetRunningSessionsTests : IDisposable
{
    private const int StaleAfterMinutes = 30;
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;

    public GetRunningSessionsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new OverwatchDbContext(new DbContextOptionsBuilder<OverwatchDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // The Sessions page labels an Active session Stale when LastActivityAt < now - threshold. Running
    // must be the exact complement, or one session shows as both Stale there and running here.
    [Fact]
    public async Task Handle_SessionLastActiveExactlyAtStaleCutoff_IsRunning()
    {
        await Seed(Session("at-cutoff", lastActivityAt: Now.AddMinutes(-StaleAfterMinutes)));

        var items = await Run();

        items.Select(i => i.Id).Should().Equal("at-cutoff");
    }

    [Fact]
    public async Task Handle_SessionLastActiveOneSecondPastStaleCutoff_IsExcluded()
    {
        await Seed(Session("stale", lastActivityAt: Now.AddMinutes(-StaleAfterMinutes).AddSeconds(-1)));

        var items = await Run();

        items.Should().BeEmpty("a session past the stale threshold never got a SessionEnd; it is not running");
    }

    [Fact]
    public async Task Handle_EndedSessionWithRecentActivity_IsExcluded()
    {
        var ended = Session("ended", lastActivityAt: Now.AddMinutes(-1));
        ended.Status = SessionStatus.Completed;
        await Seed(ended);

        var items = await Run();

        items.Should().BeEmpty("an ended session is finished no matter how recently it was active");
    }

    [Fact]
    public async Task Handle_IdleSession_ReportsSecondsSinceItWentIdle()
    {
        var idle = Session("idle");
        idle.ActivityState = ActivityState.Idle;
        idle.ActivityStateAt = Now.AddMinutes(-4).AddSeconds(-30);
        await Seed(idle);

        var item = (await Run()).Single();

        item.ActivityState.Should().Be(ActivityState.Idle);
        item.IdleForSeconds.Should().Be(270);
    }

    // "Idle for" answers "how long has it been waiting on nobody"; a working or waiting session is not idle.
    [Fact]
    public async Task Handle_WorkingSession_HasNoIdleFor()
    {
        var working = Session("working");
        working.ActivityState = ActivityState.Active;
        working.ActivityStateAt = Now.AddMinutes(-10);
        await Seed(working);

        var item = (await Run()).Single();

        item.IdleForSeconds.Should().BeNull();
    }

    // Burn is "what is this session costing right now": only the last 15 minutes count, divided by the
    // full 15, and the window start is exclusive so a turn exactly 15 minutes old is already history.
    [Fact]
    public async Task Handle_Burn_CountsOnlyTurnsInsideTheLast15Minutes()
    {
        await Seed(Session("s1"),
            Turn("s1", "inside", Now.AddMinutes(-14), input: 1000, output: 200, cacheWrite: 100, cacheRead: 200, cost: 0.30m),
            Turn("s1", "edge", Now.AddMinutes(-15), input: 99_000, output: 0, cacheWrite: 0, cacheRead: 0, cost: 9m),
            Turn("s1", "old", Now.AddHours(-2), input: 50_000, output: 0, cacheWrite: 0, cacheRead: 0, cost: 5m));

        var item = (await Run()).Single();

        item.BurnTokensPerMinute.Should().BeApproximately(1500 / 15.0, 1e-9, "all four token kinds count toward burn");
        item.BurnCostPerMinute.Should().Be(0.30m / 15m);
    }

    [Fact]
    public async Task Handle_Burn_NeverMixesTurnsFromAnotherSession()
    {
        await Seed(Session("quiet"), Session("busy"),
            Turn("busy", "b1", Now.AddMinutes(-1), input: 3000, output: 0, cacheWrite: 0, cacheRead: 0, cost: 1.5m));

        var items = await Run();

        items.Single(i => i.Id == "quiet").BurnTokensPerMinute.Should().Be(0);
        items.Single(i => i.Id == "busy").BurnTokensPerMinute.Should().Be(200);
    }

    // Current context is what the main thread is carrying now. A subagent has its own context window,
    // so its newer, larger turn must not be reported as the session's context.
    [Fact]
    public async Task Handle_CurrentContext_IsLatestMainThreadTurn_IgnoringNewerSubagentTurn()
    {
        var older = Turn("s1", "main-old", Now.AddMinutes(-20), context: 10_000);
        var latestMain = Turn("s1", "main-new", Now.AddMinutes(-5), context: 42_000);
        var subagent = Turn("s1", "sub", Now.AddMinutes(-1), context: 150_000);
        subagent.SubagentName = "Explore";
        var userTurn = Turn("s1", "user", Now.AddSeconds(-30), context: null);
        await Seed(Session("s1"), older, latestMain, subagent, userTurn);

        var item = (await Run()).Single();

        item.CurrentContextTokens.Should().Be(42_000);
    }

    [Fact]
    public async Task Handle_MapsIdentityTotalsAndProject_FallingBackToRepoPathWithoutProjectKey()
    {
        var keyed = Session("keyed");
        keyed.ProjectKey = "github.com/acme/api";
        keyed.RepoPath = "C:/src/api";
        keyed.MachineName = "laptop-1";
        keyed.DeveloperName = "Dev One";
        keyed.TotalInputTokens = 11;
        keyed.TotalOutputTokens = 22;
        keyed.TotalCacheCreationInputTokens = 33;
        keyed.TotalCacheReadInputTokens = 44;
        keyed.EstimatedCostUsd = 1.25m;
        var unkeyed = Session("unkeyed");
        unkeyed.RepoPath = "/home/dev/tool";
        await Seed(keyed, unkeyed);

        var items = await Run();

        var k = items.Single(i => i.Id == "keyed");
        k.Project.Should().Be("github.com/acme/api");
        k.MachineName.Should().Be("laptop-1");
        k.DeveloperEmail.Should().Be("dev@team.dev");
        k.DeveloperName.Should().Be("Dev One");
        (k.InputTokens, k.OutputTokens, k.CacheCreationInputTokens, k.CacheReadInputTokens)
            .Should().Be((11L, 22L, 33L, 44L));
        k.EstimatedCostUsd.Should().Be(1.25m);
        items.Single(i => i.Id == "unkeyed").Project.Should().Be("/home/dev/tool");
    }

    // The usage page shows one developer's or one project's sessions; the running list must match the summary.
    [Theory]
    [InlineData("a@team.dev", null, new[] { "a-keyed", "a-unkeyed" })]
    [InlineData(null, "github.com/acme/api", new[] { "a-keyed", "b-keyed" })]
    [InlineData(null, "/home/dev/tool", new[] { "a-unkeyed" })] // no project key: the repository path is the project
    [InlineData("b@team.dev", "github.com/acme/api", new[] { "b-keyed" })]
    [InlineData("A@TEAM.DEV", null, new string[0])] // exact match, as on the summary
    public async Task Handle_Filters_ReturnOnlyMatchingSessions(string? developer, string? project, string[] expected)
    {
        await Seed(
            Session("a-keyed", email: "a@team.dev", projectKey: "github.com/acme/api", repoPath: "/home/dev/api"),
            Session("a-unkeyed", email: "a@team.dev", repoPath: "/home/dev/tool"),
            Session("b-keyed", email: "b@team.dev", projectKey: "github.com/acme/api", repoPath: "/home/dev/tool"));

        var items = await Run(developer, project);

        items.Select(i => i.Id).Should().BeEquivalentTo(expected);
    }

    // SQLite hands stored times back unmarked; serialised without a Z, the dashboard would read them as local.
    [Fact]
    public async Task Handle_LastActivityAt_IsMarkedUtc()
    {
        await Seed(Session("s1"));

        var item = (await Run()).Single();

        item.LastActivityAt.Kind.Should().Be(DateTimeKind.Utc);
        item.LastActivityAt.Should().Be(Now.AddMinutes(-1));
    }

    private async Task<IReadOnlyList<RunningSessionItem>> Run(string? developer = null, string? project = null)
    {
        var handler = new GetRunningSessionsQueryHandler(
            new UsageRepository(_db), new FixedStaleThreshold(StaleAfterMinutes), new FixedTime(Now));
        var result = await handler.Handle(new GetRunningSessionsQuery(developer, project), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value!;
    }

    private async Task Seed(params object[] entities)
    {
        _db.AddRange(entities);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private static Session Session(string id, DateTime? lastActivityAt = null, string email = "dev@team.dev",
        string? projectKey = null, string? repoPath = null) => new()
    {
        Id = id,
        DeveloperEmail = email,
        ProjectKey = projectKey,
        RepoPath = repoPath,
        StartedAt = Now.AddHours(-1),
        LastActivityAt = lastActivityAt ?? Now.AddMinutes(-1),
    };

    private static ConversationTurn Turn(
        string sessionId, string id, DateTime at,
        int input = 0, int output = 0, int cacheWrite = 0, int cacheRead = 0, decimal cost = 0m,
        long? context = null) => new()
    {
        Id = id,
        SessionId = sessionId,
        Role = "assistant",
        Timestamp = at,
        InputTokens = input,
        OutputTokens = output,
        CacheCreationInputTokens = cacheWrite,
        CacheReadInputTokens = cacheRead,
        EstimatedCostUsd = cost,
        ContextTokens = context,
    };

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FixedStaleThreshold(int minutes) : ISessionStaleThresholdProvider
    {
        public int StaleAfterMinutes { get; } = minutes;
    }
}
