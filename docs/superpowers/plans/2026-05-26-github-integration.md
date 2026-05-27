# GitHub Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add GitHub webhook listener, session-commit correlation engine, and PR status API to link Claude Code sessions to git commits and pull requests.

**Architecture:** GitHub sends push/PR webhooks to a new WebhooksController, verified via HMAC-SHA256 middleware. A correlation service matches commits to sessions by comparing file changes with a configurable time window. An IGitHostingService interface abstracts GitHub API calls for Cloud/Enterprise portability. New CQRS commands/queries handle webhook processing and PR queries.

**Tech Stack:** ASP.NET Core 10, MediatR, FluentValidation, EF Core (SQLite), xUnit, FluentAssertions, NSubstitute

---

## File Map

### Domain Layer (`src/Domain/DeveloperOverwatch.Domain/`)

| File | Action | Responsibility |
|------|--------|---------------|
| `Interfaces/ICommitSessionLinkRepository.cs` | Create | Repository interface for commit-session link CRUD |
| `Interfaces/IPullRequestReviewRepository.cs` | Create | Repository interface for PR review CRUD |
| `Interfaces/IGitHostingService.cs` | Create | Abstraction for GitHub API (get commit files, PR commits, post status) |

### Application Layer (`src/Application/DeveloperOverwatch.Application/`)

| File | Action | Responsibility |
|------|--------|---------------|
| `Services/SessionCommitCorrelator.cs` | Create | Core correlation logic: file-change matching + confidence scoring |
| `Commands/ProcessPushWebhook/ProcessPushWebhookCommand.cs` | Create | Command record for push webhook payload |
| `Commands/ProcessPushWebhook/ProcessPushWebhookCommandValidator.cs` | Create | Validates repo, commits array not empty |
| `Commands/ProcessPushWebhook/ProcessPushWebhookCommandHandler.cs` | Create | Parses trailers, runs file correlation, persists links |
| `Commands/ProcessPrWebhook/ProcessPrWebhookCommand.cs` | Create | Command record for PR webhook payload |
| `Commands/ProcessPrWebhook/ProcessPrWebhookCommandValidator.cs` | Create | Validates repo, PR number, action |
| `Commands/ProcessPrWebhook/ProcessPrWebhookCommandHandler.cs` | Create | Correlates all PR commits, creates/updates PullRequestReview |
| `Queries/GetPullRequests/GetPullRequestsQuery.cs` | Create | Query to list PRs with optional filters |
| `Queries/GetPullRequests/GetPullRequestsQueryHandler.cs` | Create | Returns filtered PR list |
| `Queries/GetPullRequestDetail/GetPullRequestDetailQuery.cs` | Create | Query for single PR with linked sessions |
| `Queries/GetPullRequestDetail/GetPullRequestDetailQueryHandler.cs` | Create | Returns PR detail with correlated sessions |
| `Commands/ReviewPullRequest/ReviewPullRequestCommand.cs` | Create | Command to approve/request-changes on a PR |
| `Commands/ReviewPullRequest/ReviewPullRequestCommandValidator.cs` | Create | Validates repo, PR number, status |
| `Commands/ReviewPullRequest/ReviewPullRequestCommandHandler.cs` | Create | Updates PullRequestReview status |

### Infrastructure Layer (`src/Infrastructure/DeveloperOverwatch.Infrastructure/`)

| File | Action | Responsibility |
|------|--------|---------------|
| `Persistence/Repositories/CommitSessionLinkRepository.cs` | Create | EF Core implementation of ICommitSessionLinkRepository |
| `Persistence/Repositories/PullRequestReviewRepository.cs` | Create | EF Core implementation of IPullRequestReviewRepository |
| `Services/GitHubService.cs` | Create | IGitHostingService implementation using HttpClient |
| `DependencyInjection.cs` | Modify | Register new repositories and GitHubService |

### API Layer (`src/API/DeveloperOverwatch.API/`)

| File | Action | Responsibility |
|------|--------|---------------|
| `Middleware/WebhookSignatureMiddleware.cs` | Create | HMAC-SHA256 signature verification for /api/webhooks/* |
| `Controllers/WebhooksController.cs` | Create | POST /api/webhooks/github endpoint |
| `Controllers/PullRequestsController.cs` | Create | GET/POST /api/prs endpoints |
| `Program.cs` | Modify | Register webhook middleware, add GitHub config |
| `appsettings.json` | Modify | Add GitHub configuration section |

### Tests

| File | Action | Responsibility |
|------|--------|---------------|
| `tests/DeveloperOverwatch.Application.Tests/Services/SessionCommitCorrelatorTests.cs` | Create | Unit tests for correlation logic |
| `tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPushWebhookTests.cs` | Create | Unit tests for push webhook handler |
| `tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPrWebhookTests.cs` | Create | Unit tests for PR webhook handler |
| `tests/DeveloperOverwatch.Application.Tests/Commands/ReviewPullRequestTests.cs` | Create | Unit tests for PR review command |
| `tests/DeveloperOverwatch.API.Tests/Middleware/WebhookSignatureMiddlewareTests.cs` | Create | Unit tests for HMAC verification |
| `tests/DeveloperOverwatch.API.Tests/Controllers/WebhooksControllerTests.cs` | Create | Integration tests for webhook endpoint |
| `tests/DeveloperOverwatch.API.Tests/Controllers/PullRequestsControllerTests.cs` | Create | Integration tests for PR endpoints |

---

### Task 1: Domain Interfaces — ICommitSessionLinkRepository and IPullRequestReviewRepository

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IPullRequestReviewRepository.cs`

- [ ] **Step 1: Create ICommitSessionLinkRepository**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface ICommitSessionLinkRepository
{
    Task<IReadOnlyList<CommitSessionLink>> GetByCommitShaAsync(string commitSha, CancellationToken ct = default);
    Task<IReadOnlyList<CommitSessionLink>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task AddAsync(CommitSessionLink link, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<CommitSessionLink> links, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create IPullRequestReviewRepository**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IPullRequestReviewRepository.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IPullRequestReviewRepository
{
    Task<PullRequestReview?> GetByRepoAndNumberAsync(string repo, int prNumber, CancellationToken ct = default);
    Task<IReadOnlyList<PullRequestReview>> GetAllAsync(string? repo = null, CancellationToken ct = default);
    Task AddAsync(PullRequestReview review, CancellationToken ct = default);
    Task UpdateAsync(PullRequestReview review, CancellationToken ct = default);
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs src/Domain/DeveloperOverwatch.Domain/Interfaces/IPullRequestReviewRepository.cs
git commit -m "feat: add repository interfaces for commit-session links and PR reviews"
```

---

### Task 2: Domain Interface — IGitHostingService

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGitHostingService.cs`

- [ ] **Step 1: Create IGitHostingService interface**

This interface abstracts GitHub API operations so the application layer doesn't depend on GitHub-specific details. It must support both GitHub Cloud and Enterprise Server via configurable base URL.

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IGitHostingService.cs
namespace DeveloperOverwatch.Domain.Interfaces;

public interface IGitHostingService
{
    Task<IReadOnlyList<string>> GetCommitFilesAsync(string repo, string commitSha, CancellationToken ct = default);
    Task<IReadOnlyList<CommitInfo>> GetPullRequestCommitsAsync(string repo, int prNumber, CancellationToken ct = default);
    Task<PullRequestInfo?> GetPullRequestAsync(string repo, int prNumber, CancellationToken ct = default);
}

public sealed record CommitInfo(string Sha, string Message, DateTime CommittedAt);

public sealed record PullRequestInfo(
    int Number,
    string Title,
    string AuthorEmail,
    string HeadBranch,
    string BaseBranch,
    string State);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add src/Domain/DeveloperOverwatch.Domain/Interfaces/IGitHostingService.cs
git commit -m "feat: add IGitHostingService interface for GitHub Cloud/Enterprise abstraction"
```

---

### Task 3: Infrastructure — Repository Implementations

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/PullRequestReviewRepository.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create CommitSessionLinkRepository**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class CommitSessionLinkRepository : ICommitSessionLinkRepository
{
    private readonly OverwatchDbContext _db;

    public CommitSessionLinkRepository(OverwatchDbContext db) => _db = db;

    public async Task<IReadOnlyList<CommitSessionLink>> GetByCommitShaAsync(
        string commitSha, CancellationToken ct = default) =>
        await _db.CommitSessionLinks
            .Include(l => l.Session)
            .Where(l => l.CommitSha == commitSha)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CommitSessionLink>> GetBySessionIdAsync(
        string sessionId, CancellationToken ct = default) =>
        await _db.CommitSessionLinks
            .Where(l => l.SessionId == sessionId)
            .ToListAsync(ct);

    public async Task AddAsync(CommitSessionLink link, CancellationToken ct = default) =>
        await _db.CommitSessionLinks.AddAsync(link, ct);

    public async Task AddRangeAsync(IEnumerable<CommitSessionLink> links, CancellationToken ct = default) =>
        await _db.CommitSessionLinks.AddRangeAsync(links, ct);
}
```

- [ ] **Step 2: Create PullRequestReviewRepository**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/PullRequestReviewRepository.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class PullRequestReviewRepository : IPullRequestReviewRepository
{
    private readonly OverwatchDbContext _db;

    public PullRequestReviewRepository(OverwatchDbContext db) => _db = db;

    public async Task<PullRequestReview?> GetByRepoAndNumberAsync(
        string repo, int prNumber, CancellationToken ct = default) =>
        await _db.PullRequestReviews
            .FirstOrDefaultAsync(r => r.Repo == repo && r.PrNumber == prNumber, ct);

    public async Task<IReadOnlyList<PullRequestReview>> GetAllAsync(
        string? repo = null, CancellationToken ct = default)
    {
        var query = _db.PullRequestReviews.AsQueryable();
        if (repo is not null)
            query = query.Where(r => r.Repo == repo);
        return await query.OrderByDescending(r => r.PrNumber).ToListAsync(ct);
    }

    public async Task AddAsync(PullRequestReview review, CancellationToken ct = default) =>
        await _db.PullRequestReviews.AddAsync(review, ct);

    public Task UpdateAsync(PullRequestReview review, CancellationToken ct = default)
    {
        _db.PullRequestReviews.Update(review);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Register repositories in DI**

Modify `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs` to add the two new repository registrations. Add these two lines after the existing `ISessionRepository` registration:

```csharp
services.AddScoped<ICommitSessionLinkRepository, CommitSessionLinkRepository>();
services.AddScoped<IPullRequestReviewRepository, PullRequestReviewRepository>();
```

The updated using block needs:
```csharp
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
```
(already present from existing SessionRepository registration)

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/PullRequestReviewRepository.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs
git commit -m "feat: add EF Core repository implementations for commit links and PR reviews"
```

---

### Task 4: Infrastructure — GitHubService Implementation

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Services/GitHubService.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`
- Modify: `src/API/DeveloperOverwatch.API/appsettings.json`

- [ ] **Step 1: Write failing test for GitHubService**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Services/GitHubServiceConfigTests.cs
namespace DeveloperOverwatch.Application.Tests.Services;

using FluentAssertions;
using Xunit;

public class GitHubServiceConfigTests
{
    [Fact]
    public void GitHubConfig_DefaultBaseUrl_IsGitHubCloud()
    {
        var config = new DeveloperOverwatch.Infrastructure.Services.GitHubConfig();
        config.BaseUrl.Should().Be("https://api.github.com");
    }
}
```

- [ ] **Step 2: Create GitHubConfig and GitHubService**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Services/GitHubService.cs
namespace DeveloperOverwatch.Infrastructure.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class GitHubConfig
{
    public string BaseUrl { get; set; } = "https://api.github.com";
    public string? Token { get; set; }
}

public sealed class GitHubService : IGitHostingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubService(HttpClient httpClient, ILogger<GitHubService> logger, IOptions<GitHubConfig> config)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(config.Value.BaseUrl);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeveloperOverwatch", "1.0"));

        if (!string.IsNullOrWhiteSpace(config.Value.Token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Value.Token);
    }

    public async Task<IReadOnlyList<string>> GetCommitFilesAsync(
        string repo, string commitSha, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/commits/{commitSha}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get commit {Sha} for {Repo}: {Status}",
                commitSha, repo, response.StatusCode);
            return [];
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var files = doc.RootElement.GetProperty("files");
        var result = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("filename", out var filename))
                result.Add(filename.GetString()!);
        }
        return result;
    }

    public async Task<IReadOnlyList<CommitInfo>> GetPullRequestCommitsAsync(
        string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/pulls/{prNumber}/commits", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get PR {PrNumber} commits for {Repo}: {Status}",
                prNumber, repo, response.StatusCode);
            return [];
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var result = new List<CommitInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var sha = item.GetProperty("sha").GetString()!;
            var message = item.GetProperty("commit").GetProperty("message").GetString()!;
            var date = item.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTime();
            result.Add(new CommitInfo(sha, message, date));
        }
        return result;
    }

    public async Task<PullRequestInfo?> GetPullRequestAsync(
        string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/pulls/{prNumber}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get PR {PrNumber} for {Repo}: {Status}",
                prNumber, repo, response.StatusCode);
            return null;
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PullRequestInfo(
            Number: root.GetProperty("number").GetInt32(),
            Title: root.GetProperty("title").GetString()!,
            AuthorEmail: root.GetProperty("user").GetProperty("login").GetString()!,
            HeadBranch: root.GetProperty("head").GetProperty("ref").GetString()!,
            BaseBranch: root.GetProperty("base").GetProperty("ref").GetString()!,
            State: root.GetProperty("state").GetString()!);
    }
}
```

- [ ] **Step 3: Register GitHubService in DI**

Modify `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`. Add after the existing registrations:

```csharp
services.Configure<Services.GitHubConfig>(configuration.GetSection("GitHub"));
services.AddHttpClient<IGitHostingService, Services.GitHubService>();
```

This requires changing the `AddInfrastructureDependencies` method signature to accept an `IConfiguration` parameter:

```csharp
public static IServiceCollection AddInfrastructureDependencies(
    this IServiceCollection services,
    string connectionString,
    IConfiguration configuration)
```

Add using at top:
```csharp
using Microsoft.Extensions.Configuration;
```

- [ ] **Step 4: Update Program.cs to pass configuration**

Modify `src/API/DeveloperOverwatch.API/Program.cs` line 13. Change:
```csharp
builder.Services.AddInfrastructureDependencies(connectionString);
```
to:
```csharp
builder.Services.AddInfrastructureDependencies(connectionString, builder.Configuration);
```

- [ ] **Step 5: Add GitHub config section to appsettings.json**

Add to `src/API/DeveloperOverwatch.API/appsettings.json`:
```json
"GitHub": {
    "BaseUrl": "https://api.github.com",
    "Token": ""
}
```

- [ ] **Step 6: Run test**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter GitHubServiceConfigTests -v n`
Expected: 1 test passed.

- [ ] **Step 7: Build full solution to verify nothing broke**

Run: `dotnet build`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 8: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Services/GitHubService.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs src/API/DeveloperOverwatch.API/Program.cs src/API/DeveloperOverwatch.API/appsettings.json tests/DeveloperOverwatch.Application.Tests/Services/GitHubServiceConfigTests.cs
git commit -m "feat: add GitHubService with configurable base URL for Cloud and Enterprise"
```

---

### Task 5: Application — SessionCommitCorrelator Service

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Services/SessionCommitCorrelator.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Services/SessionCommitCorrelatorTests.cs`

This is the core correlation engine. For a given commit SHA and repo, it:
1. Gets the files changed in the commit via IGitHostingService
2. Queries all completed sessions for that repo that ended within a time window before the commit
3. For each candidate session, computes `confidence = matched_files / total_commit_files`
4. Returns links for sessions with confidence > 0

- [ ] **Step 1: Write failing tests for SessionCommitCorrelator**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Services/SessionCommitCorrelatorTests.cs
namespace DeveloperOverwatch.Application.Tests.Services;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class SessionCommitCorrelatorTests
{
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly SessionCommitCorrelator _sut;

    public SessionCommitCorrelatorTests()
    {
        _sut = new SessionCommitCorrelator(_gitService, _sessionRepo);
    }

    [Fact]
    public async Task CorrelateCommit_MatchingFiles_ReturnsLinkWithConfidence()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;
        var commitFiles = new List<string> { "src/Program.cs", "src/Startup.cs" };

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(commitFiles);

        var session = new Session
        {
            Id = "session-1",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-30),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Program.cs" },
                new() { Id = "fc-2", SessionId = "session-1", FilePath = "src/Other.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().HaveCount(1);
        links[0].CommitSha.Should().Be(commitSha);
        links[0].SessionId.Should().Be("session-1");
        links[0].Confidence.Should().Be(0.5); // 1 matched / 2 commit files
        links[0].LinkMethod.Should().Be("file_match");
    }

    [Fact]
    public async Task CorrelateCommit_NoMatchingFiles_ReturnsEmpty()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Unrelated.cs" });

        var session = new Session
        {
            Id = "session-1",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-30),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Program.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelateCommit_SessionOutsideTimeWindow_Excluded()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Program.cs" });

        var session = new Session
        {
            Id = "session-old",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddDays(-2),
            EndedAt = commitTime.AddDays(-2).AddHours(1),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-old", FilePath = "src/Program.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().BeEmpty();
    }

    [Fact]
    public async Task CorrelateCommit_PerfectMatch_ReturnsConfidence1()
    {
        var commitSha = "abc123";
        var repo = "owner/repo";
        var commitTime = DateTime.UtcNow;

        _gitService.GetCommitFilesAsync(repo, commitSha, Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Program.cs", "src/Startup.cs" });

        var session = new Session
        {
            Id = "session-perfect",
            DeveloperEmail = "dev@test.com",
            RepoPath = repo,
            Status = SessionStatus.Completed,
            StartedAt = commitTime.AddHours(-1),
            EndedAt = commitTime.AddMinutes(-10),
            FileChanges = new List<FileChange>
            {
                new() { Id = "fc-1", SessionId = "session-perfect", FilePath = "src/Program.cs" },
                new() { Id = "fc-2", SessionId = "session-perfect", FilePath = "src/Startup.cs" }
            }
        };

        _sessionRepo.GetAllAsync(null, repo, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { session });

        var links = await _sut.CorrelateCommitAsync(repo, commitSha, commitTime);

        links.Should().HaveCount(1);
        links[0].Confidence.Should().Be(1.0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter SessionCommitCorrelatorTests -v n`
Expected: Build error — `SessionCommitCorrelator` does not exist yet.

- [ ] **Step 3: Implement SessionCommitCorrelator**

```csharp
// src/Application/DeveloperOverwatch.Application/Services/SessionCommitCorrelator.cs
namespace DeveloperOverwatch.Application.Services;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;

public sealed class SessionCommitCorrelator
{
    private static readonly TimeSpan DefaultTimeWindow = TimeSpan.FromHours(2);

    private readonly IGitHostingService _gitService;
    private readonly ISessionRepository _sessionRepo;

    public SessionCommitCorrelator(IGitHostingService gitService, ISessionRepository sessionRepo)
    {
        _gitService = gitService;
        _sessionRepo = sessionRepo;
    }

    public async Task<IReadOnlyList<CommitSessionLink>> CorrelateCommitAsync(
        string repo,
        string commitSha,
        DateTime commitTime,
        TimeSpan? timeWindow = null,
        CancellationToken ct = default)
    {
        var window = timeWindow ?? DefaultTimeWindow;
        var commitFiles = await _gitService.GetCommitFilesAsync(repo, commitSha, ct);

        if (commitFiles.Count == 0)
            return [];

        var commitFileSet = new HashSet<string>(commitFiles, StringComparer.OrdinalIgnoreCase);
        var sessions = await _sessionRepo.GetAllAsync(null, repo, ct);

        var windowStart = commitTime - window;
        var candidates = sessions
            .Where(s => s.Status == SessionStatus.Completed
                        && s.EndedAt.HasValue
                        && s.EndedAt.Value >= windowStart
                        && s.EndedAt.Value <= commitTime);

        var links = new List<CommitSessionLink>();
        foreach (var session in candidates)
        {
            var sessionFilePaths = session.FileChanges
                .Select(fc => fc.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchedCount = commitFileSet.Count(f => sessionFilePaths.Contains(f));
            if (matchedCount == 0)
                continue;

            var confidence = (double)matchedCount / commitFiles.Count;

            links.Add(new CommitSessionLink
            {
                Id = Guid.NewGuid().ToString(),
                CommitSha = commitSha,
                SessionId = session.Id,
                Repo = repo,
                Confidence = Math.Round(confidence, 4),
                LinkMethod = "file_match"
            });
        }

        return links;
    }

    public static IReadOnlyList<CommitSessionLink> ParseTrailers(
        string repo, string commitSha, string commitMessage)
    {
        var links = new List<CommitSessionLink>();
        var lines = commitMessage.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Claude-Session:", StringComparison.OrdinalIgnoreCase))
                continue;

            var sessionId = trimmed["Claude-Session:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(sessionId))
                continue;

            links.Add(new CommitSessionLink
            {
                Id = Guid.NewGuid().ToString(),
                CommitSha = commitSha,
                SessionId = sessionId,
                Repo = repo,
                Confidence = 1.0,
                LinkMethod = "trailer"
            });
        }

        return links;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter SessionCommitCorrelatorTests -v n`
Expected: 4 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Services/SessionCommitCorrelator.cs tests/DeveloperOverwatch.Application.Tests/Services/SessionCommitCorrelatorTests.cs
git commit -m "feat: add session-commit correlation engine with file-change matching"
```

---

### Task 6: Application — ProcessPushWebhook Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommandHandler.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPushWebhookTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPushWebhookTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ProcessPushWebhook;
using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ProcessPushWebhookTests
{
    private readonly ICommitSessionLinkRepository _linkRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_PushWithTrailer_CreatesTrailerLink()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPushWebhookCommandHandler(
            _linkRepo, correlator, _unitOfWork);

        var command = new ProcessPushWebhookCommand
        {
            Repo = "owner/repo",
            Commits = new[]
            {
                new PushCommitDto
                {
                    Sha = "abc123",
                    Message = "feat: add feature\n\nClaude-Session: session-42",
                    Timestamp = DateTime.UtcNow
                }
            }
        };

        _gitService.GetCommitFilesAsync("owner/repo", "abc123", Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        await _linkRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<CommitSessionLink>>(links =>
                links.Any(l => l.LinkMethod == "trailer" && l.SessionId == "session-42")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PushWithFileMatch_CreatesFileMatchLink()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPushWebhookCommandHandler(
            _linkRepo, correlator, _unitOfWork);

        var commitTime = DateTime.UtcNow;
        var command = new ProcessPushWebhookCommand
        {
            Repo = "owner/repo",
            Commits = new[]
            {
                new PushCommitDto
                {
                    Sha = "def456",
                    Message = "feat: update",
                    Timestamp = commitTime
                }
            }
        };

        _gitService.GetCommitFilesAsync("owner/repo", "def456", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/File.cs" });

        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>
            {
                new()
                {
                    Id = "session-99",
                    DeveloperEmail = "dev@test.com",
                    RepoPath = "owner/repo",
                    Status = SessionStatus.Completed,
                    StartedAt = commitTime.AddHours(-1),
                    EndedAt = commitTime.AddMinutes(-10),
                    FileChanges = new List<FileChange>
                    {
                        new() { Id = "fc-1", SessionId = "session-99", FilePath = "src/File.cs" }
                    }
                }
            });

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _linkRepo.Received().AddRangeAsync(
            Arg.Is<IEnumerable<CommitSessionLink>>(links =>
                links.Any(l => l.LinkMethod == "file_match" && l.SessionId == "session-99")),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ProcessPushWebhookTests -v n`
Expected: Build error — types don't exist yet.

- [ ] **Step 3: Create ProcessPushWebhookCommand**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommand.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPushWebhookCommand : IRequest<Result<int>>
{
    public required string Repo { get; init; }
    public required IReadOnlyList<PushCommitDto> Commits { get; init; }
}

public sealed record PushCommitDto
{
    public required string Sha { get; init; }
    public required string Message { get; init; }
    public required DateTime Timestamp { get; init; }
}
```

- [ ] **Step 4: Create ProcessPushWebhookCommandValidator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using FluentValidation;

public sealed class ProcessPushWebhookCommandValidator : AbstractValidator<ProcessPushWebhookCommand>
{
    public ProcessPushWebhookCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.Commits).NotEmpty();
    }
}
```

- [ ] **Step 5: Create ProcessPushWebhookCommandHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ProcessPushWebhookCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPushWebhook;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ProcessPushWebhookCommandHandler
    : IRequestHandler<ProcessPushWebhookCommand, Result<int>>
{
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly SessionCommitCorrelator _correlator;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessPushWebhookCommandHandler(
        ICommitSessionLinkRepository linkRepo,
        SessionCommitCorrelator correlator,
        IUnitOfWork unitOfWork)
    {
        _linkRepo = linkRepo;
        _correlator = correlator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        ProcessPushWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var allLinks = new List<CommitSessionLink>();

        foreach (var commit in request.Commits)
        {
            var trailerLinks = SessionCommitCorrelator.ParseTrailers(
                request.Repo, commit.Sha, commit.Message);
            allLinks.AddRange(trailerLinks);

            var correlatedLinks = await _correlator.CorrelateCommitAsync(
                request.Repo, commit.Sha, commit.Timestamp, ct: cancellationToken);

            var trailerSessionIds = trailerLinks.Select(l => l.SessionId).ToHashSet();
            var nonDuplicateCorrelated = correlatedLinks
                .Where(l => !trailerSessionIds.Contains(l.SessionId));
            allLinks.AddRange(nonDuplicateCorrelated);
        }

        if (allLinks.Count > 0)
        {
            await _linkRepo.AddRangeAsync(allLinks, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(allLinks.Count);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ProcessPushWebhookTests -v n`
Expected: 2 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/ProcessPushWebhook/ tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPushWebhookTests.cs
git commit -m "feat: add ProcessPushWebhook command with trailer parsing and file correlation"
```

---

### Task 7: Application — ProcessPrWebhook Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommandHandler.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPrWebhookTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPrWebhookTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ProcessPrWebhook;
using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ProcessPrWebhookTests
{
    private readonly ICommitSessionLinkRepository _linkRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IGitHostingService _gitService = Substitute.For<IGitHostingService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_NewPr_CreatesPullRequestReview()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPrWebhookCommandHandler(
            _linkRepo, _prRepo, _gitService, correlator, _unitOfWork);

        var commitTime = DateTime.UtcNow;
        var command = new ProcessPrWebhookCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Action = "opened"
        };

        _gitService.GetPullRequestCommitsAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(new List<CommitInfo>
            {
                new("abc123", "feat: add thing", commitTime)
            });

        _gitService.GetCommitFilesAsync("owner/repo", "abc123", Arg.Any<CancellationToken>())
            .Returns(new List<string> { "src/Thing.cs" });

        _sessionRepo.GetAllAsync(null, "owner/repo", Arg.Any<CancellationToken>())
            .Returns(new List<Session>
            {
                new()
                {
                    Id = "session-1",
                    DeveloperEmail = "dev@test.com",
                    RepoPath = "owner/repo",
                    Status = SessionStatus.Completed,
                    StartedAt = commitTime.AddHours(-1),
                    EndedAt = commitTime.AddMinutes(-5),
                    FileChanges = new List<FileChange>
                    {
                        new() { Id = "fc-1", SessionId = "session-1", FilePath = "src/Thing.cs" }
                    }
                }
            });

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns((PullRequestReview?)null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _prRepo.Received(1).AddAsync(
            Arg.Is<PullRequestReview>(r => r.Repo == "owner/repo" && r.PrNumber == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingPr_DoesNotDuplicate()
    {
        var correlator = new SessionCommitCorrelator(_gitService, _sessionRepo);
        var handler = new ProcessPrWebhookCommandHandler(
            _linkRepo, _prRepo, _gitService, correlator, _unitOfWork);

        var command = new ProcessPrWebhookCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Action = "synchronize"
        };

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        _gitService.GetPullRequestCommitsAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(new List<CommitInfo>());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _prRepo.DidNotReceive().AddAsync(
            Arg.Any<PullRequestReview>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ProcessPrWebhookTests -v n`
Expected: Build error — types don't exist yet.

- [ ] **Step 3: Create ProcessPrWebhookCommand**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommand.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ProcessPrWebhookCommand : IRequest<Result<int>>
{
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public required string Action { get; init; }
}
```

- [ ] **Step 4: Create ProcessPrWebhookCommandValidator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using FluentValidation;

public sealed class ProcessPrWebhookCommandValidator : AbstractValidator<ProcessPrWebhookCommand>
{
    public ProcessPrWebhookCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.PrNumber).GreaterThan(0);
        RuleFor(x => x.Action).NotEmpty();
    }
}
```

- [ ] **Step 5: Create ProcessPrWebhookCommandHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ProcessPrWebhookCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.ProcessPrWebhook;

using DeveloperOverwatch.Application.Services;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ProcessPrWebhookCommandHandler
    : IRequestHandler<ProcessPrWebhookCommand, Result<int>>
{
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly IGitHostingService _gitService;
    private readonly SessionCommitCorrelator _correlator;
    private readonly IUnitOfWork _unitOfWork;

    public ProcessPrWebhookCommandHandler(
        ICommitSessionLinkRepository linkRepo,
        IPullRequestReviewRepository prRepo,
        IGitHostingService gitService,
        SessionCommitCorrelator correlator,
        IUnitOfWork unitOfWork)
    {
        _linkRepo = linkRepo;
        _prRepo = prRepo;
        _gitService = gitService;
        _correlator = correlator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        ProcessPrWebhookCommand request,
        CancellationToken cancellationToken)
    {
        var existingReview = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (existingReview is null)
        {
            await _prRepo.AddAsync(new PullRequestReview
            {
                Id = Guid.NewGuid().ToString(),
                Repo = request.Repo,
                PrNumber = request.PrNumber,
                Status = ReviewStatus.Pending
            }, cancellationToken);
        }

        var commits = await _gitService.GetPullRequestCommitsAsync(
            request.Repo, request.PrNumber, cancellationToken);

        var allLinks = new List<CommitSessionLink>();
        foreach (var commit in commits)
        {
            var trailerLinks = SessionCommitCorrelator.ParseTrailers(
                request.Repo, commit.Sha, commit.Message);
            allLinks.AddRange(trailerLinks);

            var correlatedLinks = await _correlator.CorrelateCommitAsync(
                request.Repo, commit.Sha, commit.CommittedAt, ct: cancellationToken);

            var trailerSessionIds = trailerLinks.Select(l => l.SessionId).ToHashSet();
            allLinks.AddRange(correlatedLinks.Where(l => !trailerSessionIds.Contains(l.SessionId)));
        }

        if (allLinks.Count > 0)
        {
            await _linkRepo.AddRangeAsync(allLinks, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(allLinks.Count);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ProcessPrWebhookTests -v n`
Expected: 2 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/ProcessPrWebhook/ tests/DeveloperOverwatch.Application.Tests/Commands/ProcessPrWebhookTests.cs
git commit -m "feat: add ProcessPrWebhook command for PR event handling and correlation"
```

---

### Task 8: Application — PR Queries (GetPullRequests, GetPullRequestDetail)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetPullRequests/GetPullRequestsQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetPullRequests/GetPullRequestsQueryHandler.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetPullRequestDetail/GetPullRequestDetailQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetPullRequestDetail/GetPullRequestDetailQueryHandler.cs`

- [ ] **Step 1: Create GetPullRequestsQuery**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetPullRequests/GetPullRequestsQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetPullRequests;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetPullRequestsQuery(string? Repo = null)
    : IRequest<Result<IReadOnlyList<PullRequestReview>>>;
```

- [ ] **Step 2: Create GetPullRequestsQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetPullRequests/GetPullRequestsQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetPullRequests;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPullRequestsQueryHandler
    : IRequestHandler<GetPullRequestsQuery, Result<IReadOnlyList<PullRequestReview>>>
{
    private readonly IPullRequestReviewRepository _repository;

    public GetPullRequestsQueryHandler(IPullRequestReviewRepository repository) =>
        _repository = repository;

    public async Task<Result<IReadOnlyList<PullRequestReview>>> Handle(
        GetPullRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var reviews = await _repository.GetAllAsync(request.Repo, cancellationToken);
        return Result<IReadOnlyList<PullRequestReview>>.Success(reviews);
    }
}
```

- [ ] **Step 3: Create GetPullRequestDetailQuery**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetPullRequestDetail/GetPullRequestDetailQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetPullRequestDetail;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetPullRequestDetailQuery(string Repo, int PrNumber)
    : IRequest<Result<PullRequestDetailDto>>;

public sealed record PullRequestDetailDto
{
    public required string Id { get; init; }
    public required int PrNumber { get; init; }
    public required string Repo { get; init; }
    public required string Status { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? Notes { get; init; }
    public required IReadOnlyList<LinkedSessionDto> LinkedSessions { get; init; }
}

public sealed record LinkedSessionDto
{
    public required string SessionId { get; init; }
    public required string CommitSha { get; init; }
    public required double Confidence { get; init; }
    public required string LinkMethod { get; init; }
    public string? DeveloperEmail { get; init; }
    public DateTime? SessionStartedAt { get; init; }
    public DateTime? SessionEndedAt { get; init; }
}
```

- [ ] **Step 4: Create GetPullRequestDetailQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetPullRequestDetail/GetPullRequestDetailQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetPullRequestDetail;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetPullRequestDetailQueryHandler
    : IRequestHandler<GetPullRequestDetailQuery, Result<PullRequestDetailDto>>
{
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly IGitHostingService _gitService;

    public GetPullRequestDetailQueryHandler(
        IPullRequestReviewRepository prRepo,
        ICommitSessionLinkRepository linkRepo,
        IGitHostingService gitService)
    {
        _prRepo = prRepo;
        _linkRepo = linkRepo;
        _gitService = gitService;
    }

    public async Task<Result<PullRequestDetailDto>> Handle(
        GetPullRequestDetailQuery request,
        CancellationToken cancellationToken)
    {
        var review = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (review is null)
            return Result<PullRequestDetailDto>.Fail(
                $"PR {request.PrNumber} not found for repo {request.Repo}");

        var commits = await _gitService.GetPullRequestCommitsAsync(
            request.Repo, request.PrNumber, cancellationToken);

        var linkedSessions = new List<LinkedSessionDto>();
        foreach (var commit in commits)
        {
            var links = await _linkRepo.GetByCommitShaAsync(commit.Sha, cancellationToken);
            foreach (var link in links)
            {
                linkedSessions.Add(new LinkedSessionDto
                {
                    SessionId = link.SessionId,
                    CommitSha = link.CommitSha,
                    Confidence = link.Confidence ?? 0,
                    LinkMethod = link.LinkMethod ?? "unknown",
                    DeveloperEmail = link.Session?.DeveloperEmail,
                    SessionStartedAt = link.Session?.StartedAt,
                    SessionEndedAt = link.Session?.EndedAt
                });
            }
        }

        var dto = new PullRequestDetailDto
        {
            Id = review.Id,
            PrNumber = review.PrNumber,
            Repo = review.Repo,
            Status = review.Status.ToString(),
            ReviewedBy = review.ReviewedBy,
            ReviewedAt = review.ReviewedAt,
            Notes = review.Notes,
            LinkedSessions = linkedSessions
        };

        return Result<PullRequestDetailDto>.Success(dto);
    }
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Queries/GetPullRequests/ src/Application/DeveloperOverwatch.Application/Queries/GetPullRequestDetail/
git commit -m "feat: add PR list and detail queries with linked session aggregation"
```

---

### Task 9: Application — ReviewPullRequest Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommandHandler.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Commands/ReviewPullRequestTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/ReviewPullRequestTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ReviewPullRequest;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ReviewPullRequestTests
{
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ExistingPr_UpdatesStatus()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Status = "Approved",
            ReviewedBy = "architect@team.dev",
            Notes = "Looks good"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existingReview.Status.Should().Be(ReviewStatus.Approved);
        existingReview.ReviewedBy.Should().Be("architect@team.dev");
        existingReview.Notes.Should().Be("Looks good");
        existingReview.ReviewedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NonExistentPr_ReturnsFail()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 999, Arg.Any<CancellationToken>())
            .Returns((PullRequestReview?)null);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 999,
            Status = "Approved",
            ReviewedBy = "architect@team.dev"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_InvalidStatus_ReturnsValidationFailure()
    {
        var handler = new ReviewPullRequestCommandHandler(_prRepo, _unitOfWork);

        var command = new ReviewPullRequestCommand
        {
            Repo = "owner/repo",
            PrNumber = 42,
            Status = "InvalidStatus",
            ReviewedBy = "architect@team.dev"
        };

        var existingReview = new PullRequestReview
        {
            Id = "review-1",
            Repo = "owner/repo",
            PrNumber = 42,
            Status = ReviewStatus.Pending
        };

        _prRepo.GetByRepoAndNumberAsync("owner/repo", 42, Arg.Any<CancellationToken>())
            .Returns(existingReview);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ReviewPullRequestTests -v n`
Expected: Build error — types don't exist yet.

- [ ] **Step 3: Create ReviewPullRequestCommand**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommand.cs
namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ReviewPullRequestCommand : IRequest<Result<string>>
{
    public required string Repo { get; init; }
    public required int PrNumber { get; init; }
    public required string Status { get; init; }
    public required string ReviewedBy { get; init; }
    public string? Notes { get; init; }
}
```

- [ ] **Step 4: Create ReviewPullRequestCommandValidator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using FluentValidation;

public sealed class ReviewPullRequestCommandValidator : AbstractValidator<ReviewPullRequestCommand>
{
    public ReviewPullRequestCommandValidator()
    {
        RuleFor(x => x.Repo).NotEmpty();
        RuleFor(x => x.PrNumber).GreaterThan(0);
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.ReviewedBy).NotEmpty();
    }
}
```

- [ ] **Step 5: Create ReviewPullRequestCommandHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ReviewPullRequestCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.ReviewPullRequest;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ReviewPullRequestCommandHandler
    : IRequestHandler<ReviewPullRequestCommand, Result<string>>
{
    private readonly IPullRequestReviewRepository _prRepo;
    private readonly IUnitOfWork _unitOfWork;

    public ReviewPullRequestCommandHandler(
        IPullRequestReviewRepository prRepo,
        IUnitOfWork unitOfWork)
    {
        _prRepo = prRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        ReviewPullRequestCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReviewStatus>(request.Status, ignoreCase: true, out var status))
            return Result<string>.Fail($"Invalid review status: {request.Status}");

        var review = await _prRepo.GetByRepoAndNumberAsync(
            request.Repo, request.PrNumber, cancellationToken);

        if (review is null)
            return Result<string>.Fail(
                $"PR {request.PrNumber} not found for repo {request.Repo}");

        review.Status = status;
        review.ReviewedBy = request.ReviewedBy;
        review.ReviewedAt = DateTime.UtcNow;
        review.Notes = request.Notes;

        await _prRepo.UpdateAsync(review, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(review.Id);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter ReviewPullRequestTests -v n`
Expected: 3 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/ReviewPullRequest/ tests/DeveloperOverwatch.Application.Tests/Commands/ReviewPullRequestTests.cs
git commit -m "feat: add ReviewPullRequest command for architect approvals"
```

---

### Task 10: Application DI — Register SessionCommitCorrelator

**Files:**
- Modify: `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs`

- [ ] **Step 1: Register SessionCommitCorrelator in Application DI**

Add to `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs` after the MediatR registration:

```csharp
services.AddScoped<Services.SessionCommitCorrelator>();
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/DependencyInjection.cs
git commit -m "feat: register SessionCommitCorrelator in application DI"
```

---

### Task 11: API — WebhookSignatureMiddleware (HMAC-SHA256)

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Middleware/WebhookSignatureMiddleware.cs`
- Create: `tests/DeveloperOverwatch.API.Tests/Middleware/WebhookSignatureMiddlewareTests.cs`

- [ ] **Step 1: Write failing test for HMAC verification**

```csharp
// tests/DeveloperOverwatch.API.Tests/Middleware/WebhookSignatureMiddlewareTests.cs
namespace DeveloperOverwatch.API.Tests.Middleware;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class WebhookSignatureMiddlewareTests
{
    private const string TestSecret = "test-webhook-secret";

    private TestServer CreateServer(string? secret = TestSecret)
    {
        var config = new Dictionary<string, string?>
        {
            ["GitHub:WebhookSecret"] = secret
        };

        var builder = new WebHostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(config))
            .ConfigureServices(s => s.AddRouting())
            .Configure(app =>
            {
                app.UseMiddleware<DeveloperOverwatch.API.Middleware.WebhookSignatureMiddleware>();
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/api/webhooks/github", async ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("OK");
                    });
                    endpoints.MapGet("/api/sessions", async ctx =>
                    {
                        ctx.Response.StatusCode = 200;
                        await ctx.Response.WriteAsync("OK");
                    });
                });
            });

        return new TestServer(builder);
    }

    [Fact]
    public async Task ValidSignature_PassesThrough()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var body = "{\"action\":\"push\"}";
        var signature = ComputeSignature(body, TestSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", $"sha256={signature}");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=invalid");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingSignature_Returns401()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonWebhookRoute_PassesThrough()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var response = await client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NoSecretConfigured_PassesThrough()
    {
        using var server = CreateServer(secret: null);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.API.Tests/ --filter WebhookSignatureMiddlewareTests -v n`
Expected: Build error — `WebhookSignatureMiddleware` does not exist yet.

- [ ] **Step 3: Implement WebhookSignatureMiddleware**

```csharp
// src/API/DeveloperOverwatch.API/Middleware/WebhookSignatureMiddleware.cs
namespace DeveloperOverwatch.API.Middleware;

using System.Security.Cryptography;
using System.Text;

public sealed class WebhookSignatureMiddleware
{
    private const string SignatureHeader = "X-Hub-Signature-256";
    private readonly RequestDelegate _next;
    private readonly string? _secret;

    public WebhookSignatureMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _secret = configuration.GetValue<string>("GitHub:WebhookSecret");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_secret))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(SignatureHeader, out var signatureHeader) ||
            string.IsNullOrWhiteSpace(signatureHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing webhook signature" });
            return;
        }

        context.Request.EnableBuffering();
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var expectedSignature = ComputeSignature(body, _secret);
        var actualSignature = signatureHeader.ToString();

        if (actualSignature.StartsWith("sha256="))
            actualSignature = actualSignature["sha256=".Length..];

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(actualSignature)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid webhook signature" });
            return;
        }

        await _next(context);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.API.Tests/ --filter WebhookSignatureMiddlewareTests -v n`
Expected: 5 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Middleware/WebhookSignatureMiddleware.cs tests/DeveloperOverwatch.API.Tests/Middleware/WebhookSignatureMiddlewareTests.cs
git commit -m "feat: add HMAC-SHA256 webhook signature verification middleware"
```

---

### Task 12: API — WebhooksController

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Controllers/WebhooksController.cs`
- Modify: `src/API/DeveloperOverwatch.API/Program.cs`
- Modify: `src/API/DeveloperOverwatch.API/appsettings.json`

- [ ] **Step 1: Create WebhooksController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/WebhooksController.cs
namespace DeveloperOverwatch.API.Controllers;

using System.Text.Json;
using DeveloperOverwatch.Application.Commands.ProcessPrWebhook;
using DeveloperOverwatch.Application.Commands.ProcessPushWebhook;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IMediator mediator, ILogger<WebhooksController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("github")]
    public async Task<IActionResult> HandleGitHubWebhook(CancellationToken ct)
    {
        var eventType = Request.Headers["X-GitHub-Event"].ToString();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var payload = JsonDocument.Parse(body);

        return eventType switch
        {
            "push" => await HandlePush(payload, ct),
            "pull_request" => await HandlePullRequest(payload, ct),
            _ => Ok(new { message = $"Event '{eventType}' ignored" })
        };
    }

    private async Task<IActionResult> HandlePush(JsonDocument payload, CancellationToken ct)
    {
        var root = payload.RootElement;
        var repo = root.GetProperty("repository").GetProperty("full_name").GetString()!;

        var commits = new List<PushCommitDto>();
        foreach (var commit in root.GetProperty("commits").EnumerateArray())
        {
            commits.Add(new PushCommitDto
            {
                Sha = commit.GetProperty("id").GetString()!,
                Message = commit.GetProperty("message").GetString()!,
                Timestamp = commit.GetProperty("timestamp").GetDateTime()
            });
        }

        _logger.LogInformation("Processing push webhook for {Repo} with {Count} commits",
            repo, commits.Count);

        var command = new ProcessPushWebhookCommand
        {
            Repo = repo,
            Commits = commits
        };

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { linksCreated = result.Value })
            : BadRequest(new { error = result.Error });
    }

    private async Task<IActionResult> HandlePullRequest(JsonDocument payload, CancellationToken ct)
    {
        var root = payload.RootElement;
        var action = root.GetProperty("action").GetString()!;

        if (action is not ("opened" or "synchronize" or "reopened"))
            return Ok(new { message = $"PR action '{action}' ignored" });

        var repo = root.GetProperty("repository").GetProperty("full_name").GetString()!;
        var prNumber = root.GetProperty("pull_request").GetProperty("number").GetInt32();

        _logger.LogInformation("Processing PR webhook for {Repo} PR #{PrNumber} action={Action}",
            repo, prNumber, action);

        var command = new ProcessPrWebhookCommand
        {
            Repo = repo,
            PrNumber = prNumber,
            Action = action
        };

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { linksCreated = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
```

- [ ] **Step 2: Register WebhookSignatureMiddleware in Program.cs**

Modify `src/API/DeveloperOverwatch.API/Program.cs`. Add the webhook signature middleware BEFORE the API key middleware (webhooks use their own auth via HMAC):

After `app.UseCors();` and before `app.UseMiddleware<ApiKeyAuthMiddleware>();`, add:
```csharp
app.UseMiddleware<WebhookSignatureMiddleware>();
```

Also update the `ApiKeyAuthMiddleware` to bypass webhook routes. In `src/API/DeveloperOverwatch.API/Middleware/ApiKeyAuthMiddleware.cs`, add after the `/health` check:

```csharp
if (context.Request.Path.StartsWithSegments("/api/webhooks"))
{
    await _next(context);
    return;
}
```

- [ ] **Step 3: Add WebhookSecret to appsettings.json**

Add to the GitHub section in `src/API/DeveloperOverwatch.API/appsettings.json`:
```json
"WebhookSecret": ""
```

So the GitHub section becomes:
```json
"GitHub": {
    "BaseUrl": "https://api.github.com",
    "Token": "",
    "WebhookSecret": ""
}
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 5: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/WebhooksController.cs src/API/DeveloperOverwatch.API/Program.cs src/API/DeveloperOverwatch.API/Middleware/ApiKeyAuthMiddleware.cs src/API/DeveloperOverwatch.API/appsettings.json
git commit -m "feat: add WebhooksController for GitHub push and PR events"
```

---

### Task 13: API — PullRequestsController

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Controllers/PullRequestsController.cs`

- [ ] **Step 1: Create PullRequestsController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/PullRequestsController.cs
namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.ReviewPullRequest;
using DeveloperOverwatch.Application.Queries.GetPullRequestDetail;
using DeveloperOverwatch.Application.Queries.GetPullRequests;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/prs")]
public sealed class PullRequestsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PullRequestsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetPullRequests(
        [FromQuery] string? repo = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPullRequestsQuery(repo), ct);
        return Ok(result.Value);
    }

    [HttpGet("{repo}/{prNumber:int}")]
    public async Task<IActionResult> GetPullRequestDetail(
        string repo, int prNumber, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPullRequestDetailQuery(repo, prNumber), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    [HttpPost("{repo}/{prNumber:int}/review")]
    public async Task<IActionResult> ReviewPullRequest(
        string repo,
        int prNumber,
        [FromBody] ReviewPullRequestCommand command,
        CancellationToken ct)
    {
        if (command.Repo != repo || command.PrNumber != prNumber)
            return BadRequest(new { error = "Route and body mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { reviewId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/PullRequestsController.cs
git commit -m "feat: add PullRequestsController with list, detail, and review endpoints"
```

---

### Task 14: Integration Tests — Webhooks and PR Endpoints

**Files:**
- Create: `tests/DeveloperOverwatch.API.Tests/Controllers/WebhooksControllerTests.cs`
- Create: `tests/DeveloperOverwatch.API.Tests/Controllers/PullRequestsControllerTests.cs`

These integration tests exercise the full request pipeline through `WebApplicationFactory`. They use an in-memory SQLite database and a stub IGitHostingService (since we don't want to call the real GitHub API in tests).

- [ ] **Step 1: Create WebhooksControllerTests**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/WebhooksControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Text;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

public class WebhooksControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public WebhooksControllerTests(OverwatchWebApplicationFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var gitServiceDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IGitHostingService));
                if (gitServiceDescriptor is not null)
                    services.Remove(gitServiceDescriptor);

                var stub = Substitute.For<IGitHostingService>();
                stub.GetCommitFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new List<string>());
                stub.GetPullRequestCommitsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(new List<CommitInfo>());
                services.AddSingleton(stub);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task PushWebhook_ValidPayload_Returns200()
    {
        var payload = """
        {
            "repository": { "full_name": "owner/repo" },
            "commits": [
                {
                    "id": "abc123",
                    "message": "feat: test commit",
                    "timestamp": "2026-05-26T12:00:00Z"
                }
            ]
        }
        """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("linksCreated");
    }

    [Fact]
    public async Task PrWebhook_OpenedAction_Returns200()
    {
        var payload = """
        {
            "action": "opened",
            "repository": { "full_name": "owner/repo" },
            "pull_request": { "number": 42 }
        }
        """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "pull_request");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownEvent_Returns200WithIgnoredMessage()
    {
        var payload = "{}";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "issues");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ignored");
    }
}
```

- [ ] **Step 2: Create PullRequestsControllerTests**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/PullRequestsControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

public class PullRequestsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PullRequestsControllerTests(OverwatchWebApplicationFactory factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var gitServiceDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IGitHostingService));
                if (gitServiceDescriptor is not null)
                    services.Remove(gitServiceDescriptor);

                var stub = Substitute.For<IGitHostingService>();
                stub.GetCommitFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new List<string>());
                stub.GetPullRequestCommitsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(new List<CommitInfo>());
                services.AddSingleton(stub);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task GetPullRequests_EmptyDatabase_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/prs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("[]");
    }

    [Fact]
    public async Task ReviewPullRequest_NonExistentPr_Returns404()
    {
        var review = new
        {
            Repo = "owner/repo",
            PrNumber = 999,
            Status = "Approved",
            ReviewedBy = "architect@team.dev"
        };

        var response = await _client.PostAsJsonAsync("/api/prs/owner/repo/999/review", review);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FullPrWorkflow_WebhookThenQueryThenReview()
    {
        // 1. Send PR webhook to create the PR review record
        var prPayload = """
        {
            "action": "opened",
            "repository": { "full_name": "test-org/test-repo" },
            "pull_request": { "number": 1 }
        }
        """;

        var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(prPayload, Encoding.UTF8, "application/json")
        };
        webhookRequest.Headers.Add("X-GitHub-Event", "pull_request");
        var webhookResponse = await _client.SendAsync(webhookRequest);
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Query PRs — should show the new review record
        var listResponse = await _client.GetAsync("/api/prs?repo=test-org/test-repo");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().Contain("test-org/test-repo");

        // 3. Review the PR
        var review = new
        {
            Repo = "test-org/test-repo",
            PrNumber = 1,
            Status = "Approved",
            ReviewedBy = "architect@team.dev",
            Notes = "LGTM"
        };
        var reviewResponse = await _client.PostAsJsonAsync(
            "/api/prs/test-org/test-repo/1/review", review);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reviewBody = await reviewResponse.Content.ReadAsStringAsync();
        reviewBody.Should().Contain("reviewId");
    }
}
```

- [ ] **Step 3: Add NSubstitute package reference to API.Tests project**

The API.Tests project needs NSubstitute for the IGitHostingService stub. Add to `tests/DeveloperOverwatch.API.Tests/DeveloperOverwatch.API.Tests.csproj`:

```xml
<PackageReference Include="NSubstitute" Version="5.*" />
```

Also add a project reference to Infrastructure (needed for IGitHostingService types):
```xml
<ProjectReference Include="..\..\src\Infrastructure\DeveloperOverwatch.Infrastructure\DeveloperOverwatch.Infrastructure.csproj" />
```

Wait — `IGitHostingService` is in Domain, and `CommitInfo`/`PullRequestInfo` records are also in Domain. The API.Tests project already references the API project which references Domain. But we need the NSubstitute package. Check if we also need Domain project reference for the record types used in the stubs.

Actually, since `CommitInfo` and `PullRequestInfo` are defined in `IGitHostingService.cs` in the Domain namespace, and API.Tests → API → Application → Domain, the types should be transitively available. We just need the NSubstitute package.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.API.Tests/ -v n`
Expected: All tests pass (previous 3 + new integration tests).

- [ ] **Step 5: Run all tests to verify nothing is broken**

Run: `dotnet test -v n`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add tests/DeveloperOverwatch.API.Tests/
git commit -m "feat: add integration tests for webhooks and PR endpoints"
```

---

### Task 15: Route Encoding Fix for PullRequestsController

The `GET /api/prs/{repo}/{prNumber}` and `POST /api/prs/{repo}/{prNumber}/review` routes need to handle the fact that `repo` contains a slash (e.g., `owner/repo`). ASP.NET Core's default route matching will break on this.

**Files:**
- Modify: `src/API/DeveloperOverwatch.API/Controllers/PullRequestsController.cs`

- [ ] **Step 1: Fix route templates to use catch-all parameter**

Update the route templates in `PullRequestsController.cs`:

Change:
```csharp
[HttpGet("{repo}/{prNumber:int}")]
```
to:
```csharp
[HttpGet("{*repo}")]
```

And change:
```csharp
[HttpPost("{repo}/{prNumber:int}/review")]
```
to:
```csharp
[HttpPost("{*repo}")]
```

Wait — catch-all won't work cleanly with the PR number mixed in. A better approach: use separate path segments for owner and repo name.

Update the routes to:
```csharp
[HttpGet("{owner}/{repoName}/{prNumber:int}")]
public async Task<IActionResult> GetPullRequestDetail(
    string owner, string repoName, int prNumber, CancellationToken ct)
{
    var repo = $"{owner}/{repoName}";
    var result = await _mediator.Send(new GetPullRequestDetailQuery(repo, prNumber), ct);
    return result.IsSuccess
        ? Ok(result.Value)
        : NotFound(new { error = result.Error });
}

[HttpPost("{owner}/{repoName}/{prNumber:int}/review")]
public async Task<IActionResult> ReviewPullRequest(
    string owner,
    string repoName,
    int prNumber,
    [FromBody] ReviewPullRequestCommand command,
    CancellationToken ct)
{
    var repo = $"{owner}/{repoName}";
    if (command.Repo != repo || command.PrNumber != prNumber)
        return BadRequest(new { error = "Route and body mismatch" });

    var result = await _mediator.Send(command, ct);
    return result.IsSuccess
        ? Ok(new { reviewId = result.Value })
        : result.ValidationErrors.Count > 0
            ? BadRequest(new { errors = result.ValidationErrors })
            : NotFound(new { error = result.Error });
}
```

Also update the integration test URLs accordingly:
- `"/api/prs/owner/repo/999/review"` stays the same (owner=owner, repoName=repo, prNumber=999)
- `"/api/prs/test-org/test-repo/1/review"` stays the same

- [ ] **Step 2: Build and test**

Run: `dotnet test -v n`
Expected: All tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/PullRequestsController.cs
git commit -m "fix: use separate owner/repo path segments for PR routes"
```

---

## Self-Review

**Spec coverage check:**
- Webhook listener (push + PR events): Tasks 6, 7, 12
- HMAC signature verification: Task 11
- Session-commit correlation (file-change matching + confidence): Task 5
- Git trailer parsing (`Claude-Session:`): Task 5 (`ParseTrailers` static method), used in Tasks 6, 7
- Time window filter (2-hour default): Task 5 (`DefaultTimeWindow`)
- IGitHostingService interface: Task 2
- GitHub implementation with configurable base URL: Task 4
- PR status queries (list, detail, review): Tasks 8, 9, 13
- CommitSessionLink and PullRequestReview entities: already exist from Phase 1
- API endpoints: POST /api/webhooks/github (Task 12), GET /api/prs (Task 13), GET /api/prs/{owner}/{repo}/{number} (Task 13), POST /api/prs/{owner}/{repo}/{number}/review (Task 13)

**Placeholder scan:** No TBD, TODO, or "implement later" references found.

**Type consistency:** All types (`ProcessPushWebhookCommand`, `PushCommitDto`, `ProcessPrWebhookCommand`, `ReviewPullRequestCommand`, `GetPullRequestsQuery`, `GetPullRequestDetailQuery`, `PullRequestDetailDto`, `LinkedSessionDto`, `CommitInfo`, `PullRequestInfo`, `SessionCommitCorrelator`, `GitHubConfig`, `GitHubService`) are consistently referenced across all tasks.
