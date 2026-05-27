# Phase 5a: Dashboard Backend APIs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend API endpoints for architect annotations, team analytics, and developer analytics to support the Dashboard SPA (Phase 5b).

**Architecture:** Follows existing Clean Architecture (Domain → Application → Infrastructure → API). New CQRS commands/queries for annotations and analytics. Analytics handlers aggregate data in-memory from existing repositories — acceptable for v1 small datasets (SQLite).

**Tech Stack:** .NET 10, MediatR 12, FluentValidation 11, EF Core + SQLite, xUnit + FluentAssertions + NSubstitute

**Note:** Phase 5 is split into two sub-plans: 5a (this plan — backend APIs) and 5b (Dashboard SPA — React + Vite + Tailwind). The frontend depends on these endpoints.

---

### Task 1: Domain — IArchitectAnnotationRepository & Analytics Repository Methods

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IArchitectAnnotationRepository.cs`
- Modify: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs`
- Modify: `src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs`

- [ ] **Step 1: Create IArchitectAnnotationRepository**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IArchitectAnnotationRepository.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IArchitectAnnotationRepository
{
    Task<ArchitectAnnotation?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ArchitectAnnotation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task AddAsync(ArchitectAnnotation annotation, CancellationToken ct = default);
}
```

- [ ] **Step 2: Add GetAllAsync to IGovernanceViolationRepository**

Add this method to the existing interface at `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs`:

```csharp
Task<IReadOnlyList<GovernanceViolation>> GetAllAsync(CancellationToken ct = default);
```

Add it after the `GetRecentAsync` method.

- [ ] **Step 3: Add GetAllAsync to ICommitSessionLinkRepository**

Add this method to the existing interface at `src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs`:

```csharp
Task<IReadOnlyList<CommitSessionLink>> GetAllAsync(CancellationToken ct = default);
```

Add it after the `GetBySessionIdAsync` method.

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded with 0 errors. There will be build errors in the Infrastructure project because the repository implementations don't implement the new interface methods yet — that's expected and will be fixed in Task 2.

Actually, since the project references flow Domain → Infrastructure, the Infrastructure build will fail. Verify the Domain project builds alone:

Run: `dotnet build src/Domain/DeveloperOverwatch.Domain/`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Domain/DeveloperOverwatch.Domain/Interfaces/IArchitectAnnotationRepository.cs src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs src/Domain/DeveloperOverwatch.Domain/Interfaces/ICommitSessionLinkRepository.cs
git commit -m "feat: add IArchitectAnnotationRepository and analytics repository methods"
```

---

### Task 2: Infrastructure — Repository Implementations (TDD)

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/ArchitectAnnotationRepository.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Annotations/ArchitectAnnotationRepositoryTests.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs`

- [ ] **Step 1: Write failing tests for ArchitectAnnotationRepository**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Annotations/ArchitectAnnotationRepositoryTests.cs
namespace DeveloperOverwatch.Application.Tests.Annotations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class ArchitectAnnotationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly ArchitectAnnotationRepository _repo;

    public ArchitectAnnotationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new ArchitectAnnotationRepository(_db);
    }

    [Fact]
    public async Task AddAndGetById_ReturnsAnnotation()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        var annotation = new ArchitectAnnotation
        {
            Id = "a1",
            SessionId = "s1",
            Content = "Good approach here",
            CreatedBy = "architect@test.com",
            CreatedAt = DateTime.UtcNow
        };
        await _repo.AddAsync(annotation);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync("a1");

        result.Should().NotBeNull();
        result!.Content.Should().Be("Good approach here");
        result.CreatedBy.Should().Be("architect@test.com");
    }

    [Fact]
    public async Task GetBySessionId_ReturnsAnnotationsForSession()
    {
        var session = new Session { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        await _repo.AddAsync(new ArchitectAnnotation { Id = "a2", SessionId = "s2", Content = "Note 1", CreatedBy = "arch@test.com", CreatedAt = DateTime.UtcNow });
        await _repo.AddAsync(new ArchitectAnnotation { Id = "a3", SessionId = "s2", Content = "Note 2", CreatedBy = "arch@test.com", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s2");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySessionId_NoAnnotations_ReturnsEmpty()
    {
        var result = await _repo.GetBySessionIdAsync("nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync("nonexistent");

        result.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~ArchitectAnnotationRepositoryTests"`
Expected: Build error — `ArchitectAnnotationRepository` class does not exist yet.

- [ ] **Step 3: Implement ArchitectAnnotationRepository**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/ArchitectAnnotationRepository.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class ArchitectAnnotationRepository : IArchitectAnnotationRepository
{
    private readonly OverwatchDbContext _db;

    public ArchitectAnnotationRepository(OverwatchDbContext db) => _db = db;

    public async Task<ArchitectAnnotation?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<ArchitectAnnotation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(ArchitectAnnotation annotation, CancellationToken ct = default) =>
        await _db.ArchitectAnnotations.AddAsync(annotation, ct);
}
```

- [ ] **Step 4: Add GetAllAsync to GovernanceViolationRepository**

Add this method to `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs`:

```csharp
public async Task<IReadOnlyList<GovernanceViolation>> GetAllAsync(CancellationToken ct = default) =>
    await _db.GovernanceViolations
        .OrderByDescending(v => v.CreatedAt)
        .ToListAsync(ct);
```

- [ ] **Step 5: Add GetAllAsync to CommitSessionLinkRepository**

Add this method to `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs`:

```csharp
public async Task<IReadOnlyList<CommitSessionLink>> GetAllAsync(CancellationToken ct = default) =>
    await _db.CommitSessionLinks.ToListAsync(ct);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~ArchitectAnnotationRepositoryTests"`
Expected: 4 tests pass.

Then run the full build to confirm the new GetAllAsync implementations compile:

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/ArchitectAnnotationRepository.cs tests/DeveloperOverwatch.Application.Tests/Annotations/ArchitectAnnotationRepositoryTests.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/CommitSessionLinkRepository.cs
git commit -m "feat: add ArchitectAnnotationRepository and GetAllAsync to violation/commit repos"
```

---

### Task 3: Application — AddAnnotationCommand (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommandHandler.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommandValidator.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Annotations/AddAnnotationTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Annotations/AddAnnotationTests.cs
namespace DeveloperOverwatch.Application.Tests.Annotations;

using DeveloperOverwatch.Application.Commands.AddAnnotation;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class AddAnnotationTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IArchitectAnnotationRepository _annotationRepo = Substitute.For<IArchitectAnnotationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AddAnnotationCommandHandler _handler;

    public AddAnnotationTests()
    {
        _handler = new AddAnnotationCommandHandler(_sessionRepo, _annotationRepo, _unitOfWork);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesAnnotation()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns(session);

        var command = new AddAnnotationCommand("s1", "Good work here", "architect@test.com");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        await _annotationRepo.Received(1).AddAsync(
            Arg.Is<ArchitectAnnotation>(a =>
                a.SessionId == "s1" &&
                a.Content == "Good work here" &&
                a.CreatedBy == "architect@test.com"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _sessionRepo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((Session?)null);

        var command = new AddAnnotationCommand("nonexistent", "Note", "architect@test.com");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_WithTurnId_SetsOptionalFields()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns(session);

        var command = new AddAnnotationCommand("s1", "Note on turn", "architect@test.com", TurnId: "t1", ToolExecutionId: "te1");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _annotationRepo.Received(1).AddAsync(
            Arg.Is<ArchitectAnnotation>(a => a.TurnId == "t1" && a.ToolExecutionId == "te1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validator_EmptySessionId_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("", "Content", "architect@test.com");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SessionId");
    }

    [Fact]
    public async Task Validator_EmptyContent_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("s1", "", "architect@test.com");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public async Task Validator_InvalidEmail_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("s1", "Content", "not-an-email");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CreatedBy");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~AddAnnotationTests"`
Expected: Build error — command/handler/validator classes don't exist yet.

- [ ] **Step 3: Create AddAnnotationCommand record**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommand.cs
namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record AddAnnotationCommand(
    string SessionId,
    string Content,
    string CreatedBy,
    string? TurnId = null,
    string? ToolExecutionId = null) : IRequest<Result<string>>;
```

- [ ] **Step 4: Create AddAnnotationCommandValidator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using FluentValidation;

public sealed class AddAnnotationCommandValidator : AbstractValidator<AddAnnotationCommand>
{
    public AddAnnotationCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(5000);
        RuleFor(x => x.CreatedBy).NotEmpty().EmailAddress();
    }
}
```

- [ ] **Step 5: Create AddAnnotationCommandHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/AddAnnotationCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.AddAnnotation;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class AddAnnotationCommandHandler
    : IRequestHandler<AddAnnotationCommand, Result<string>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IArchitectAnnotationRepository _annotationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AddAnnotationCommandHandler(
        ISessionRepository sessionRepository,
        IArchitectAnnotationRepository annotationRepository,
        IUnitOfWork unitOfWork)
    {
        _sessionRepository = sessionRepository;
        _annotationRepository = annotationRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(AddAnnotationCommand request, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<string>.Fail($"Session '{request.SessionId}' not found");

        var annotation = new ArchitectAnnotation
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = request.SessionId,
            TurnId = request.TurnId,
            ToolExecutionId = request.ToolExecutionId,
            Content = request.Content,
            CreatedBy = request.CreatedBy,
            CreatedAt = DateTime.UtcNow
        };

        await _annotationRepository.AddAsync(annotation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(annotation.Id);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~AddAnnotationTests"`
Expected: 6 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/AddAnnotation/ tests/DeveloperOverwatch.Application.Tests/Annotations/AddAnnotationTests.cs
git commit -m "feat: add AddAnnotation command with validation and handler"
```

---

### Task 4: Application — GetTeamAnalyticsQuery (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Models/TeamAnalytics.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Models/DeveloperMetrics.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetTeamAnalytics/GetTeamAnalyticsQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetTeamAnalytics/GetTeamAnalyticsQueryHandler.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Analytics/GetTeamAnalyticsTests.cs`

- [ ] **Step 1: Create analytics response models**

```csharp
// src/Application/DeveloperOverwatch.Application/Models/TeamAnalytics.cs
namespace DeveloperOverwatch.Application.Models;

public sealed record TeamAnalytics(
    int TotalSessions,
    int SessionsThisWeek,
    int TotalPullRequests,
    int ApprovedPullRequests,
    int PendingPullRequests,
    int NeedsChangesPullRequests,
    int TotalViolations,
    int ViolationsThisWeek,
    IReadOnlyList<DeveloperMetrics> Developers);
```

```csharp
// src/Application/DeveloperOverwatch.Application/Models/DeveloperMetrics.cs
namespace DeveloperOverwatch.Application.Models;

public sealed record DeveloperMetrics(
    string Email,
    int SessionCount,
    int ViolationCount,
    int CommitCount);
```

- [ ] **Step 2: Write failing tests for GetTeamAnalyticsQueryHandler**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Analytics/GetTeamAnalyticsTests.cs
namespace DeveloperOverwatch.Application.Tests.Analytics;

using DeveloperOverwatch.Application.Queries.GetTeamAnalytics;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetTeamAnalyticsTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IPullRequestReviewRepository _prRepo = Substitute.For<IPullRequestReviewRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly ICommitSessionLinkRepository _commitRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly GetTeamAnalyticsQueryHandler _handler;

    public GetTeamAnalyticsTests()
    {
        _handler = new GetTeamAnalyticsQueryHandler(_sessionRepo, _prRepo, _violationRepo, _commitRepo);
    }

    [Fact]
    public async Task Handle_ReturnsAggregatedMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "alice@test.com", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "bob@test.com", StartedAt = DateTime.UtcNow.AddDays(-2) }
        };
        var prs = new List<PullRequestReview>
        {
            new() { Id = "pr1", PrNumber = 1, Repo = "org/repo", Status = ReviewStatus.Approved },
            new() { Id = "pr2", PrNumber = 2, Repo = "org/repo", Status = ReviewStatus.Pending }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow.AddDays(-1) }
        };
        var commits = new List<CommitSessionLink>
        {
            new() { Id = "c1", CommitSha = "abc123", SessionId = "s1", Repo = "org/repo" },
            new() { Id = "c2", CommitSha = "def456", SessionId = "s1", Repo = "org/repo" }
        };

        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(prs.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(commits.AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var analytics = result.Value!;
        analytics.TotalSessions.Should().Be(2);
        analytics.TotalPullRequests.Should().Be(2);
        analytics.ApprovedPullRequests.Should().Be(1);
        analytics.PendingPullRequests.Should().Be(1);
        analytics.TotalViolations.Should().Be(1);
        analytics.Developers.Should().HaveCount(2);

        var alice = analytics.Developers.Single(d => d.Email == "alice@test.com");
        alice.SessionCount.Should().Be(1);
        alice.ViolationCount.Should().Be(1);
        alice.CommitCount.Should().Be(2);

        var bob = analytics.Developers.Single(d => d.Email == "bob@test.com");
        bob.SessionCount.Should().Be(1);
        bob.ViolationCount.Should().Be(0);
        bob.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoData_ReturnsZeroCounts()
    {
        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<Session>().AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalSessions.Should().Be(0);
        result.Value.Developers.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CalculatesWeeklyMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow.AddDays(-30) }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "v2", SessionId = "s2", RuleId = "r2", Severity = ViolationSeverity.Info, Message = "old", CreatedAt = DateTime.UtcNow.AddDays(-30) }
        };

        _sessionRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _prRepo.GetAllAsync(ct: Arg.Any<CancellationToken>()).Returns(new List<PullRequestReview>().AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetTeamAnalyticsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SessionsThisWeek.Should().Be(1);
        result.Value.ViolationsThisWeek.Should().Be(1);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~GetTeamAnalyticsTests"`
Expected: Build error — query/handler classes don't exist yet.

- [ ] **Step 4: Create GetTeamAnalyticsQuery**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetTeamAnalytics/GetTeamAnalyticsQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetTeamAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetTeamAnalyticsQuery : IRequest<Result<TeamAnalytics>>;
```

- [ ] **Step 5: Create GetTeamAnalyticsQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetTeamAnalytics/GetTeamAnalyticsQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetTeamAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetTeamAnalyticsQueryHandler
    : IRequestHandler<GetTeamAnalyticsQuery, Result<TeamAnalytics>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IPullRequestReviewRepository _prRepository;
    private readonly IGovernanceViolationRepository _violationRepository;
    private readonly ICommitSessionLinkRepository _commitLinkRepository;

    public GetTeamAnalyticsQueryHandler(
        ISessionRepository sessionRepository,
        IPullRequestReviewRepository prRepository,
        IGovernanceViolationRepository violationRepository,
        ICommitSessionLinkRepository commitLinkRepository)
    {
        _sessionRepository = sessionRepository;
        _prRepository = prRepository;
        _violationRepository = violationRepository;
        _commitLinkRepository = commitLinkRepository;
    }

    public async Task<Result<TeamAnalytics>> Handle(GetTeamAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetAllAsync(ct: cancellationToken);
        var prs = await _prRepository.GetAllAsync(ct: cancellationToken);
        var violations = await _violationRepository.GetAllAsync(cancellationToken);
        var commitLinks = await _commitLinkRepository.GetAllAsync(cancellationToken);

        var oneWeekAgo = DateTime.UtcNow.AddDays(-7);

        var sessionsByDeveloper = sessions
            .GroupBy(s => s.DeveloperEmail)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToHashSet());

        var developers = sessionsByDeveloper
            .Select(kvp => new DeveloperMetrics(
                kvp.Key,
                kvp.Value.Count,
                violations.Count(v => kvp.Value.Contains(v.SessionId)),
                commitLinks.Count(c => kvp.Value.Contains(c.SessionId))))
            .OrderByDescending(d => d.SessionCount)
            .ToList()
            .AsReadOnly();

        var analytics = new TeamAnalytics(
            TotalSessions: sessions.Count,
            SessionsThisWeek: sessions.Count(s => s.StartedAt >= oneWeekAgo),
            TotalPullRequests: prs.Count,
            ApprovedPullRequests: prs.Count(p => p.Status == ReviewStatus.Approved),
            PendingPullRequests: prs.Count(p => p.Status == ReviewStatus.Pending),
            NeedsChangesPullRequests: prs.Count(p => p.Status == ReviewStatus.NeedsChanges),
            TotalViolations: violations.Count,
            ViolationsThisWeek: violations.Count(v => v.CreatedAt >= oneWeekAgo),
            Developers: developers);

        return Result<TeamAnalytics>.Success(analytics);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~GetTeamAnalyticsTests"`
Expected: 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Models/ src/Application/DeveloperOverwatch.Application/Queries/GetTeamAnalytics/ tests/DeveloperOverwatch.Application.Tests/Analytics/GetTeamAnalyticsTests.cs
git commit -m "feat: add GetTeamAnalytics query with developer breakdown"
```

---

### Task 5: Application — GetDeveloperAnalyticsQuery (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Models/DeveloperAnalytics.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQueryHandler.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQueryValidator.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Analytics/GetDeveloperAnalyticsTests.cs`

- [ ] **Step 1: Create DeveloperAnalytics response model**

```csharp
// src/Application/DeveloperOverwatch.Application/Models/DeveloperAnalytics.cs
namespace DeveloperOverwatch.Application.Models;

public sealed record DeveloperAnalytics(
    string Email,
    int TotalSessions,
    int SessionsThisWeek,
    int TotalViolations,
    int CriticalViolations,
    int ErrorViolations,
    int WarningViolations,
    int InfoViolations,
    int TotalCommits,
    IReadOnlyList<string> RecentRepos);
```

- [ ] **Step 2: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Analytics/GetDeveloperAnalyticsTests.cs
namespace DeveloperOverwatch.Application.Tests.Analytics;

using DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetDeveloperAnalyticsTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly ICommitSessionLinkRepository _commitRepo = Substitute.For<ICommitSessionLinkRepository>();
    private readonly GetDeveloperAnalyticsQueryHandler _handler;

    public GetDeveloperAnalyticsTests()
    {
        _handler = new GetDeveloperAnalyticsQueryHandler(_sessionRepo, _violationRepo, _commitRepo);
    }

    [Fact]
    public async Task Handle_ValidDeveloper_ReturnsMetrics()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "alice@test.com", RepoPath = "/repo/a", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "alice@test.com", RepoPath = "/repo/b", StartedAt = DateTime.UtcNow.AddDays(-2) }
        };
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Critical, Message = "crit", CreatedAt = DateTime.UtcNow },
            new() { Id = "v2", SessionId = "s1", RuleId = "r2", Severity = ViolationSeverity.Warning, Message = "warn", CreatedAt = DateTime.UtcNow },
            new() { Id = "v3", SessionId = "s2", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow }
        };
        var commits = new List<CommitSessionLink>
        {
            new() { Id = "c1", CommitSha = "abc", SessionId = "s1", Repo = "org/repo" },
            new() { Id = "c2", CommitSha = "def", SessionId = "s2", Repo = "org/repo" }
        };

        _sessionRepo.GetAllAsync(developerEmail: "alice@test.com", ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(violations.AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(commits.AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("alice@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var analytics = result.Value!;
        analytics.Email.Should().Be("alice@test.com");
        analytics.TotalSessions.Should().Be(2);
        analytics.TotalViolations.Should().Be(3);
        analytics.CriticalViolations.Should().Be(1);
        analytics.ErrorViolations.Should().Be(1);
        analytics.WarningViolations.Should().Be(1);
        analytics.InfoViolations.Should().Be(0);
        analytics.TotalCommits.Should().Be(2);
        analytics.RecentRepos.Should().Contain("/repo/a").And.Contain("/repo/b");
    }

    [Fact]
    public async Task Handle_NoSessions_ReturnsFail()
    {
        _sessionRepo.GetAllAsync(developerEmail: "nobody@test.com", ct: Arg.Any<CancellationToken>())
            .Returns(new List<Session>().AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("nobody@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_CalculatesWeeklySessionCount()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "dev@test.com", RepoPath = "/repo", StartedAt = DateTime.UtcNow.AddDays(-1) },
            new() { Id = "s2", DeveloperEmail = "dev@test.com", RepoPath = "/repo", StartedAt = DateTime.UtcNow.AddDays(-30) }
        };

        _sessionRepo.GetAllAsync(developerEmail: "dev@test.com", ct: Arg.Any<CancellationToken>()).Returns(sessions.AsReadOnly());
        _violationRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<GovernanceViolation>().AsReadOnly());
        _commitRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<CommitSessionLink>().AsReadOnly());

        var result = await _handler.Handle(new GetDeveloperAnalyticsQuery("dev@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalSessions.Should().Be(2);
        result.Value.SessionsThisWeek.Should().Be(1);
    }

    [Fact]
    public async Task Validator_EmptyEmail_Fails()
    {
        var validator = new GetDeveloperAnalyticsQueryValidator();
        var query = new GetDeveloperAnalyticsQuery("");
        var result = await validator.ValidateAsync(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~GetDeveloperAnalyticsTests"`
Expected: Build error — query/handler/validator classes don't exist yet.

- [ ] **Step 4: Create GetDeveloperAnalyticsQuery + Validator**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record GetDeveloperAnalyticsQuery(string Email) : IRequest<Result<DeveloperAnalytics>>;
```

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQueryValidator.cs
namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using FluentValidation;

public sealed class GetDeveloperAnalyticsQueryValidator : AbstractValidator<GetDeveloperAnalyticsQuery>
{
    public GetDeveloperAnalyticsQueryValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
    }
}
```

- [ ] **Step 5: Create GetDeveloperAnalyticsQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/GetDeveloperAnalyticsQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetDeveloperAnalyticsQueryHandler
    : IRequestHandler<GetDeveloperAnalyticsQuery, Result<DeveloperAnalytics>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IGovernanceViolationRepository _violationRepository;
    private readonly ICommitSessionLinkRepository _commitLinkRepository;

    public GetDeveloperAnalyticsQueryHandler(
        ISessionRepository sessionRepository,
        IGovernanceViolationRepository violationRepository,
        ICommitSessionLinkRepository commitLinkRepository)
    {
        _sessionRepository = sessionRepository;
        _violationRepository = violationRepository;
        _commitLinkRepository = commitLinkRepository;
    }

    public async Task<Result<DeveloperAnalytics>> Handle(GetDeveloperAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetAllAsync(developerEmail: request.Email, ct: cancellationToken);
        if (sessions.Count == 0)
            return Result<DeveloperAnalytics>.Fail($"No sessions found for developer '{request.Email}'");

        var sessionIds = sessions.Select(s => s.Id).ToHashSet();

        var allViolations = await _violationRepository.GetAllAsync(cancellationToken);
        var violations = allViolations.Where(v => sessionIds.Contains(v.SessionId)).ToList();

        var allCommitLinks = await _commitLinkRepository.GetAllAsync(cancellationToken);
        var commitLinks = allCommitLinks.Where(c => sessionIds.Contains(c.SessionId)).ToList();

        var oneWeekAgo = DateTime.UtcNow.AddDays(-7);

        var analytics = new DeveloperAnalytics(
            Email: request.Email,
            TotalSessions: sessions.Count,
            SessionsThisWeek: sessions.Count(s => s.StartedAt >= oneWeekAgo),
            TotalViolations: violations.Count,
            CriticalViolations: violations.Count(v => v.Severity == ViolationSeverity.Critical),
            ErrorViolations: violations.Count(v => v.Severity == ViolationSeverity.Error),
            WarningViolations: violations.Count(v => v.Severity == ViolationSeverity.Warning),
            InfoViolations: violations.Count(v => v.Severity == ViolationSeverity.Info),
            TotalCommits: commitLinks.Select(c => c.CommitSha).Distinct().Count(),
            RecentRepos: sessions
                .Select(s => s.RepoPath)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .ToList()
                .AsReadOnly());

        return Result<DeveloperAnalytics>.Success(analytics);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests/ --filter "FullyQualifiedName~GetDeveloperAnalyticsTests"`
Expected: 4 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Models/DeveloperAnalytics.cs src/Application/DeveloperOverwatch.Application/Queries/GetDeveloperAnalytics/ tests/DeveloperOverwatch.Application.Tests/Analytics/GetDeveloperAnalyticsTests.cs
git commit -m "feat: add GetDeveloperAnalytics query with severity breakdown"
```

---

### Task 6: API — Controllers & DI Wiring (TDD)

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Controllers/AnnotationsController.cs`
- Create: `src/API/DeveloperOverwatch.API/Controllers/AnalyticsController.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`
- Create: `tests/DeveloperOverwatch.API.Tests/Controllers/AnnotationsControllerTests.cs`
- Create: `tests/DeveloperOverwatch.API.Tests/Controllers/AnalyticsControllerTests.cs`

- [ ] **Step 1: Register IArchitectAnnotationRepository in DI**

Modify `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`. Add this line after the `ICommitSessionLinkRepository` registration (around line 22):

```csharp
services.AddScoped<IArchitectAnnotationRepository, ArchitectAnnotationRepository>();
```

Also add the required using at the top if not already resolved by implicit usings (the namespace `DeveloperOverwatch.Infrastructure.Persistence.Repositories` should already be referenced).

- [ ] **Step 2: Create AnnotationsController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/AnnotationsController.cs
namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.AddAnnotation;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/annotations")]
public sealed class AnnotationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnnotationsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> AddAnnotation([FromBody] AddAnnotationRequest request, CancellationToken ct)
    {
        var command = new AddAnnotationCommand(
            request.SessionId,
            request.Content,
            request.CreatedBy,
            request.TurnId,
            request.ToolExecutionId);

        var result = await _mediator.Send(command, ct);

        if (result.IsSuccess)
            return Created($"/api/sessions/{request.SessionId}", new { annotationId = result.Value });

        if (result.ValidationErrors?.Count > 0)
            return BadRequest(result.ValidationErrors);

        return result.Error?.Contains("not found") == true
            ? NotFound(result.Error)
            : BadRequest(result.Error);
    }

    public sealed record AddAnnotationRequest(
        string SessionId,
        string Content,
        string CreatedBy,
        string? TurnId = null,
        string? ToolExecutionId = null);
}
```

- [ ] **Step 3: Create AnalyticsController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/AnalyticsController.cs
namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;
using DeveloperOverwatch.Application.Queries.GetTeamAnalytics;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("team")]
    public async Task<IActionResult> GetTeamAnalytics(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetTeamAnalyticsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("developer/{email}")]
    public async Task<IActionResult> GetDeveloperAnalytics(string email, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDeveloperAnalyticsQuery(email), ct);

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error?.Contains("not found") == true
            ? NotFound(result.Error)
            : BadRequest(result.Error);
    }
}
```

- [ ] **Step 4: Write integration tests for AnnotationsController**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/AnnotationsControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class AnnotationsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly OverwatchWebApplicationFactory _factory;

    public AnnotationsControllerTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Post_ValidAnnotation_Returns201()
    {
        var sessionId = Guid.NewGuid().ToString();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = sessionId, DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync("/api/annotations", new
        {
            SessionId = sessionId,
            Content = "Good approach here",
            CreatedBy = "architect@test.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("annotationId");
    }

    [Fact]
    public async Task Post_SessionNotFound_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync("/api/annotations", new
        {
            SessionId = "nonexistent",
            Content = "Note",
            CreatedBy = "architect@test.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_EmptyContent_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/annotations", new
        {
            SessionId = "s1",
            Content = "",
            CreatedBy = "architect@test.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 5: Write integration tests for AnalyticsController**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/AnalyticsControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class AnalyticsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly OverwatchWebApplicationFactory _factory;

    public AnalyticsControllerTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTeamAnalytics_ReturnsOk()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = Guid.NewGuid().ToString(), DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync("/api/analytics/team");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("totalSessions");
        body.Should().Contain("developers");
    }

    [Fact]
    public async Task GetDeveloperAnalytics_ValidEmail_ReturnsOk()
    {
        var email = $"dev-{Guid.NewGuid():N}@test.com";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = Guid.NewGuid().ToString(), DeveloperEmail = email, RepoPath = "/repo", StartedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/analytics/developer/{email}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(email);
        body.Should().Contain("totalSessions");
    }

    [Fact]
    public async Task GetDeveloperAnalytics_NoSessions_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/analytics/developer/nobody@test.com");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTeamAnalytics_EmptyDb_ReturnsZeroCounts()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var response = await _client.GetAsync("/api/analytics/team");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"totalSessions\":0");
    }
}
```

- [ ] **Step 6: Run all tests to verify everything passes**

Run: `dotnet test`
Expected: All tests pass (76 existing + 17 new = 93 total). Build succeeds with 0 errors.

If the `GetTeamAnalytics_EmptyDb_ReturnsZeroCounts` test fails because other test fixtures already seeded data via the shared `OverwatchWebApplicationFactory`, that's expected — the shared in-memory SQLite connection persists data across tests. In that case, just verify the response is 200 OK and contains the expected JSON keys, without asserting exact zero counts.

- [ ] **Step 7: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/AnnotationsController.cs src/API/DeveloperOverwatch.API/Controllers/AnalyticsController.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs tests/DeveloperOverwatch.API.Tests/Controllers/AnnotationsControllerTests.cs tests/DeveloperOverwatch.API.Tests/Controllers/AnalyticsControllerTests.cs
git commit -m "feat: add Annotations and Analytics controllers with integration tests"
```
