namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

public class OverwatchWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    public const string TestApiKey = "test-integration-api-key";

    private readonly SqliteConnection _connection;

    public OverwatchWebApplicationFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKeys:Keys:0"] = TestApiKey
            });
        });

        builder.ConfigureServices(services =>
        {
            // Same registration as production SQLite, pointed at an in-memory database, so startup
            // runs the real SQLite migrations.
            services.RemoveAll<DbContextOptions<OverwatchDbContext>>();
            services.RemoveAll<DbContextOptions<SqliteOverwatchDbContext>>();
            services.RemoveAll<OverwatchDbContext>();
            services.AddDbContext<OverwatchDbContext, SqliteOverwatchDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

public class SessionsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SessionsControllerTests(OverwatchWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task FullSessionLifecycle_StartEventsEnd_ReturnsSession()
    {
        var sessionId = Guid.NewGuid().ToString();

        var startCmd = new
        {
            SessionId = sessionId,
            DeveloperEmail = "test@team.dev",
            DeveloperName = "Tester",
            RepoPath = "/test/repo",
            StartedAt = DateTime.UtcNow
        };
        var startResponse = await _client.PostAsJsonAsync("/api/sessions/start", startCmd);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventsCmd = new
        {
            SessionId = sessionId,
            ToolExecutions = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    ToolName = "Edit",
                    InputParams = "{\"file\":\"Program.cs\"}",
                    OutputSummary = "File edited",
                    DurationMs = 100,
                    Timestamp = DateTime.UtcNow
                }
            },
            FileChanges = new[]
            {
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = "Program.cs",
                    ChangeType = "Modified",
                    DiffText = "+var x = 1;"
                }
            },
            ConversationTurns = Array.Empty<object>()
        };
        var eventsResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", eventsCmd);
        eventsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var endCmd = new
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow,
            SummaryJson = "{\"turns\":1,\"tools\":[\"Edit\"],\"files\":[\"Program.cs\"]}"
        };
        var endResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", endCmd);
        endResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/sessions/{sessionId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain(sessionId);
        body.Should().Contain("test@team.dev");
        body.Should().Contain("Edit");
        body.Should().Contain("Program.cs");
    }

    // The Agent redelivers an end whose response it never saw, then ships the session's signals.
    [Fact]
    public async Task EndTwice_BothSucceed_AndEventsAfterTheEndAreStillAccepted()
    {
        var sessionId = Guid.NewGuid().ToString();
        var start = new { SessionId = sessionId, DeveloperEmail = "redeliver@team.dev", RepoPath = "C:/repos/test", StartedAt = DateTime.UtcNow };
        (await _client.PostAsJsonAsync("/api/sessions/start", start)).StatusCode.Should().Be(HttpStatusCode.OK);

        var end = new { SessionId = sessionId, EndedAt = DateTime.UtcNow };
        (await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", end)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", end)).StatusCode.Should().Be(HttpStatusCode.OK);

        var signals = new
        {
            SessionId = sessionId,
            ToolFailures = new[] { new { Id = "f-1", ToolName = "Bash", At = DateTime.UtcNow } }
        };
        (await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", signals)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // The Agent dead-letters a session's events only on this code; any other 404 stays retryable.
    [Theory]
    [InlineData("events")]
    [InlineData("end")]
    public async Task UnknownSession_Returns404WithSessionNotFoundCode(string action)
    {
        var sessionId = Guid.NewGuid().ToString();
        object body = action == "end"
            ? new { SessionId = sessionId, EndedAt = DateTime.UtcNow }
            : new { SessionId = sessionId };

        var response = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/{action}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"code\":\"session_not_found\"");
    }

    [Fact]
    public async Task GetSessions_FilterByDeveloper_ReturnsFiltered()
    {
        var sessionId = Guid.NewGuid().ToString();
        var startCmd = new
        {
            SessionId = sessionId,
            DeveloperEmail = "filter-test@team.dev",
            StartedAt = DateTime.UtcNow
        };
        await _client.PostAsJsonAsync("/api/sessions/start", startCmd);

        var response = await _client.GetAsync("/api/sessions?developer=filter-test@team.dev");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("filter-test@team.dev");
    }

    [Fact]
    public async Task StartAndEvents_WithCaptureFields_GetSessionReturnsThem()
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var startResponse = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            sessionId,
            developerEmail = "capture@team.dev",
            startedAt = now,
            agent = "claude-code",
            machineName = "build-box-7",
            projectKey = "github.com/mckruz/overwatch"
        });
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var eventsResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/events", new
        {
            sessionId,
            conversationTurns = new[]
            {
                new
                {
                    id = Guid.NewGuid().ToString(),
                    role = "assistant",
                    inputTokens = 10,
                    cacheReadInputTokens = 4000,
                    cacheCreationInputTokens = 90,
                    gitBranch = "feat/meter",
                    subagentName = "explorer",
                    timestamp = now
                }
            },
            activityEvents = new[] { new { state = "waiting", at = now } },
            toolFailures = new[] { new { id = Guid.NewGuid().ToString(), toolName = "Bash", at = now } }
        });
        eventsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await (await _client.GetAsync($"/api/sessions/{sessionId}")).Content.ReadAsStringAsync();
        body.Should().Contain("\"agent\":\"claude-code\"");
        body.Should().Contain("\"machineName\":\"build-box-7\"");
        body.Should().Contain("\"projectKey\":\"github.com/mckruz/overwatch\"");
        body.Should().Contain("\"peakContextTokens\":4100");
        body.Should().Contain("\"activityState\":\"Waiting\"");
        body.Should().Contain("\"toolErrorCount\":1");
        body.Should().Contain("\"lastToolErrorName\":\"Bash\"");
    }

    [Fact]
    public async Task StartSession_MissingEmail_Returns400()
    {
        var cmd = new { SessionId = Guid.NewGuid().ToString(), DeveloperEmail = "", StartedAt = DateTime.UtcNow };
        var response = await _client.PostAsJsonAsync("/api/sessions/start", cmd);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StartSession_NoApiKey_Returns401()
    {
        var factory = new OverwatchWebApplicationFactory();
        var client = factory.CreateClient();

        var cmd = new { SessionId = Guid.NewGuid().ToString(), DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        var response = await client.PostAsJsonAsync("/api/sessions/start", cmd);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
