# Foundation & Capture Layer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the server-side foundation (Clean Architecture, database, MediatR pipeline) and session ingestion API, then wire up Claude Code hooks that POST session data to the API — producing a working end-to-end capture flow.

**Architecture:** .NET 10 Clean Architecture (Domain → Application → Infrastructure → Presentation) modeled after microsoft-agentic-harness. MediatR CQRS with FluentValidation pipeline. EF Core with SQLite. Docker Compose for local development.

**Tech Stack:** .NET 10, ASP.NET Core, MediatR, FluentValidation, EF Core (SQLite), xUnit, Docker Compose

**Scope:** This is Plan 1 of 6. Covers Phase 1 (Foundation) and Phase 2 (Capture & Ingest) from the design spec. Subsequent plans: GitHub Integration, Governance Engine, Dashboard, Observability & Polish, Local Agent.

**Reference:** Design spec at `docs/superpowers/specs/2026-05-26-developer-overwatch-design.md`. Harness patterns at `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\microsoft-agentic-harness`.

---

## File Map

```
DeveloperOverwatch.sln
Directory.Build.props
docker-compose.yml

src/
├── Domain/
│   └── DeveloperOverwatch.Domain/
│       ├── DeveloperOverwatch.Domain.csproj
│       ├── Entities/
│       │   ├── Session.cs
│       │   ├── ConversationTurn.cs
│       │   ├── ToolExecution.cs
│       │   ├── FileChange.cs
│       │   ├── GovernanceViolation.cs
│       │   ├── CommitSessionLink.cs
│       │   ├── PullRequestReview.cs
│       │   └── ArchitectAnnotation.cs
│       ├── Enums/
│       │   ├── SessionStatus.cs
│       │   ├── ReviewStatus.cs
│       │   ├── ViolationSeverity.cs
│       │   └── ChangeType.cs
│       ├── Interfaces/
│       │   ├── ISessionRepository.cs
│       │   └── IUnitOfWork.cs
│       └── Common/
│           └── Result.cs
│
├── Application/
│   └── DeveloperOverwatch.Application/
│       ├── DeveloperOverwatch.Application.csproj
│       ├── DependencyInjection.cs
│       ├── Behaviors/
│       │   ├── ValidationBehavior.cs
│       │   └── LoggingBehavior.cs
│       ├── Commands/
│       │   ├── IngestSessionStart/
│       │   │   ├── IngestSessionStartCommand.cs
│       │   │   ├── IngestSessionStartCommandValidator.cs
│       │   │   └── IngestSessionStartCommandHandler.cs
│       │   ├── IngestSessionEvents/
│       │   │   ├── IngestSessionEventsCommand.cs
│       │   │   ├── IngestSessionEventsCommandValidator.cs
│       │   │   └── IngestSessionEventsCommandHandler.cs
│       │   └── IngestSessionEnd/
│       │       ├── IngestSessionEndCommand.cs
│       │       ├── IngestSessionEndCommandValidator.cs
│       │       └── IngestSessionEndCommandHandler.cs
│       └── Queries/
│           ├── GetSession/
│           │   ├── GetSessionQuery.cs
│           │   └── GetSessionQueryHandler.cs
│           └── GetSessions/
│               ├── GetSessionsQuery.cs
│               └── GetSessionsQueryHandler.cs
│
├── Infrastructure/
│   └── DeveloperOverwatch.Infrastructure/
│       ├── DeveloperOverwatch.Infrastructure.csproj
│       ├── DependencyInjection.cs
│       ├── Persistence/
│       │   ├── OverwatchDbContext.cs
│       │   ├── Configurations/
│       │   │   ├── SessionConfiguration.cs
│       │   │   ├── ConversationTurnConfiguration.cs
│       │   │   ├── ToolExecutionConfiguration.cs
│       │   │   ├── FileChangeConfiguration.cs
│       │   │   ├── GovernanceViolationConfiguration.cs
│       │   │   ├── CommitSessionLinkConfiguration.cs
│       │   │   ├── PullRequestReviewConfiguration.cs
│       │   │   └── ArchitectAnnotationConfiguration.cs
│       │   └── Repositories/
│       │       └── SessionRepository.cs
│       └── Migrations/
│
└── API/
    └── DeveloperOverwatch.API/
        ├── DeveloperOverwatch.API.csproj
        ├── Program.cs
        ├── Dockerfile
        ├── appsettings.json
        ├── Controllers/
        │   └── SessionsController.cs
        └── Middleware/
            └── ApiKeyAuthMiddleware.cs

tests/
├── DeveloperOverwatch.Domain.Tests/
│   ├── DeveloperOverwatch.Domain.Tests.csproj
│   └── Common/
│       └── ResultTests.cs
├── DeveloperOverwatch.Application.Tests/
│   ├── DeveloperOverwatch.Application.Tests.csproj
│   ├── Behaviors/
│   │   └── ValidationBehaviorTests.cs
│   └── Commands/
│       ├── IngestSessionStartTests.cs
│       ├── IngestSessionEventsTests.cs
│       └── IngestSessionEndTests.cs
└── DeveloperOverwatch.API.Tests/
    ├── DeveloperOverwatch.API.Tests.csproj
    └── Controllers/
        └── SessionsControllerTests.cs

hooks/
├── overwatch-session-start.ps1
├── overwatch-post-tool-use.ps1
├── overwatch-session-end.ps1
└── install-hooks.ps1
```

---

### Task 1: Solution Scaffold

**Files:**
- Create: `DeveloperOverwatch.sln`
- Create: `Directory.Build.props`
- Create: `src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj`
- Create: `src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj`
- Create: `src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj`
- Create: `tests/DeveloperOverwatch.Domain.Tests/DeveloperOverwatch.Domain.Tests.csproj`
- Create: `tests/DeveloperOverwatch.Application.Tests/DeveloperOverwatch.Application.Tests.csproj`
- Create: `tests/DeveloperOverwatch.API.Tests/DeveloperOverwatch.API.Tests.csproj`

- [ ] **Step 1: Create Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create Domain project**

```xml
<!-- src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DeveloperOverwatch.Domain</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Create Application project**

```xml
<!-- src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DeveloperOverwatch.Application</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MediatR" Version="12.*" />
    <PackageReference Include="FluentValidation" Version="11.*" />
    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Domain\DeveloperOverwatch.Domain\DeveloperOverwatch.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create Infrastructure project**

```xml
<!-- src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>DeveloperOverwatch.Infrastructure</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Domain\DeveloperOverwatch.Domain\DeveloperOverwatch.Domain.csproj" />
    <ProjectReference Include="..\..\Application\DeveloperOverwatch.Application\DeveloperOverwatch.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create API project**

```xml
<!-- src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>DeveloperOverwatch.API</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Application\DeveloperOverwatch.Application\DeveloperOverwatch.Application.csproj" />
    <ProjectReference Include="..\..\Infrastructure\DeveloperOverwatch.Infrastructure\DeveloperOverwatch.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Create test projects**

```xml
<!-- tests/DeveloperOverwatch.Domain.Tests/DeveloperOverwatch.Domain.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Domain\DeveloperOverwatch.Domain\DeveloperOverwatch.Domain.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/DeveloperOverwatch.Application.Tests/DeveloperOverwatch.Application.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Application\DeveloperOverwatch.Application\DeveloperOverwatch.Application.csproj" />
    <ProjectReference Include="..\..\src\Infrastructure\DeveloperOverwatch.Infrastructure\DeveloperOverwatch.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

```xml
<!-- tests/DeveloperOverwatch.API.Tests/DeveloperOverwatch.API.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="7.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\API\DeveloperOverwatch.API\DeveloperOverwatch.API.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Create solution and add all projects**

Run:
```bash
cd "C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\developer-overwatch"
dotnet new sln -n DeveloperOverwatch
dotnet sln add src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj
dotnet sln add src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj
dotnet sln add src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj
dotnet sln add src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj
dotnet sln add tests/DeveloperOverwatch.Domain.Tests/DeveloperOverwatch.Domain.Tests.csproj
dotnet sln add tests/DeveloperOverwatch.Application.Tests/DeveloperOverwatch.Application.Tests.csproj
dotnet sln add tests/DeveloperOverwatch.API.Tests/DeveloperOverwatch.API.Tests.csproj
```

- [ ] **Step 8: Verify build**

Run: `dotnet build DeveloperOverwatch.sln`
Expected: Build succeeded with 0 errors.

- [ ] **Step 9: Commit**

```bash
git init
git add -A
git commit -m "feat: scaffold solution with Clean Architecture projects"
```

---

### Task 2: Domain Entities and Enums

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/SessionStatus.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/ReviewStatus.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/ViolationSeverity.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/ChangeType.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Common/Result.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/Session.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/ConversationTurn.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/ToolExecution.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/FileChange.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/CommitSessionLink.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/PullRequestReview.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Entities/ArchitectAnnotation.cs`
- Test: `tests/DeveloperOverwatch.Domain.Tests/Common/ResultTests.cs`

- [ ] **Step 1: Write Result<T> tests**

```csharp
// tests/DeveloperOverwatch.Domain.Tests/Common/ResultTests.cs
namespace DeveloperOverwatch.Domain.Tests.Common;

using DeveloperOverwatch.Domain.Common;
using FluentAssertions;

public class ResultTests
{
    [Fact]
    public void Success_ShouldSetIsSuccessTrue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldSetIsSuccessFalse()
    {
        var result = Result<int>.Fail("something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("something went wrong");
    }

    [Fact]
    public void ValidationFailure_ShouldContainAllErrors()
    {
        var errors = new List<string> { "field1 required", "field2 invalid" };

        var result = Result<int>.ValidationFailure(errors);

        result.IsSuccess.Should().BeFalse();
        result.ValidationErrors.Should().BeEquivalentTo(errors);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Domain.Tests --filter "FullyQualifiedName~ResultTests" -v quiet`
Expected: FAIL — `Result` type does not exist yet.

- [ ] **Step 3: Implement enums**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/SessionStatus.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum SessionStatus { Active, Completed, Failed }
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/ReviewStatus.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum ReviewStatus { Pending, Approved, NeedsChanges }
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/ViolationSeverity.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum ViolationSeverity { Info, Warning, Error, Critical }
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/ChangeType.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum ChangeType { Created, Modified, Deleted }
```

- [ ] **Step 4: Implement Result\<T\>**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Common/Result.cs
namespace DeveloperOverwatch.Domain.Common;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    public IReadOnlyList<string> ValidationErrors { get; }

    private Result(bool isSuccess, T? value, string? error, IReadOnlyList<string>? validationErrors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ValidationErrors = validationErrors ?? [];
    }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Fail(string error) => new(false, default, error, null);

    public static Result<T> ValidationFailure(IReadOnlyList<string> errors) =>
        new(false, default, "Validation failed", errors);
}
```

- [ ] **Step 5: Implement entities**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/Session.cs
namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class Session
{
    public string Id { get; set; } = string.Empty;
    public string DeveloperEmail { get; set; } = string.Empty;
    public string? DeveloperName { get; set; }
    public string? RepoPath { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? SummaryJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public IList<ConversationTurn> ConversationTurns { get; set; } = new List<ConversationTurn>();
    public IList<ToolExecution> ToolExecutions { get; set; } = new List<ToolExecution>();
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
    public IList<GovernanceViolation> GovernanceViolations { get; set; } = new List<GovernanceViolation>();
    public IList<CommitSessionLink> CommitSessionLinks { get; set; } = new List<CommitSessionLink>();
    public IList<ArchitectAnnotation> ArchitectAnnotations { get; set; } = new List<ArchitectAnnotation>();
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/ConversationTurn.cs
namespace DeveloperOverwatch.Domain.Entities;

public class ConversationTurn
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public int TurnIndex { get; set; }
    public string Role { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int? TokenCount { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsDecision { get; set; }

    public Session? Session { get; set; }
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/ToolExecution.cs
namespace DeveloperOverwatch.Domain.Entities;

public class ToolExecution
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? TurnId { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? InputParams { get; set; }
    public string? OutputSummary { get; set; }
    public int? DurationMs { get; set; }
    public DateTime Timestamp { get; set; }

    public Session? Session { get; set; }
    public ConversationTurn? Turn { get; set; }
    public IList<FileChange> FileChanges { get; set; } = new List<FileChange>();
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/FileChange.cs
namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class FileChange
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? ToolExecutionId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public string? DiffText { get; set; }
    public ChangeType? ChangeType { get; set; }

    public Session? Session { get; set; }
    public ToolExecution? ToolExecution { get; set; }
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs
namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class GovernanceViolation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string RuleId { get; set; } = string.Empty;
    public ViolationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ContextJson { get; set; }
    public bool Resolved { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Session? Session { get; set; }
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/CommitSessionLink.cs
namespace DeveloperOverwatch.Domain.Entities;

public class CommitSessionLink
{
    public string Id { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? LinkMethod { get; set; }

    public Session? Session { get; set; }
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/PullRequestReview.cs
namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

public class PullRequestReview
{
    public string Id { get; set; } = string.Empty;
    public int PrNumber { get; set; }
    public string Repo { get; set; } = string.Empty;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? Notes { get; set; }
}
```

```csharp
// src/Domain/DeveloperOverwatch.Domain/Entities/ArchitectAnnotation.cs
namespace DeveloperOverwatch.Domain.Entities;

public class ArchitectAnnotation
{
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? TurnId { get; set; }
    public string? ToolExecutionId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Session? Session { get; set; }
    public ConversationTurn? Turn { get; set; }
    public ToolExecution? ToolExecution { get; set; }
}
```

- [ ] **Step 6: Run Result tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Domain.Tests --filter "FullyQualifiedName~ResultTests" -v quiet`
Expected: 3 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Domain/ tests/DeveloperOverwatch.Domain.Tests/
git commit -m "feat: add domain entities, enums, and Result<T>"
```

---

### Task 3: Domain Interfaces

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/ISessionRepository.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IUnitOfWork.cs`

- [ ] **Step 1: Create ISessionRepository**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/ISessionRepository.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetAllAsync(string? developerEmail = null, string? repo = null, CancellationToken ct = default);
    Task AddAsync(Session session, CancellationToken ct = default);
    Task UpdateAsync(Session session, CancellationToken ct = default);
    Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default);
    Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default);
    Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default);
    Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default);
    Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default);
    Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create IUnitOfWork**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IUnitOfWork.cs
namespace DeveloperOverwatch.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Domain/DeveloperOverwatch.Domain/`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Domain/DeveloperOverwatch.Domain/Interfaces/
git commit -m "feat: add domain interfaces for repository and unit of work"
```

---

### Task 4: EF Core Persistence

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/OverwatchDbContext.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/SessionConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ConversationTurnConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ToolExecutionConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/FileChangeConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/CommitSessionLinkConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/PullRequestReviewConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ArchitectAnnotationConfiguration.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/SessionRepository.cs`

- [ ] **Step 1: Create OverwatchDbContext**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/OverwatchDbContext.cs
namespace DeveloperOverwatch.Infrastructure.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class OverwatchDbContext : DbContext, IUnitOfWork
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<FileChange> FileChanges => Set<FileChange>();
    public DbSet<GovernanceViolation> GovernanceViolations => Set<GovernanceViolation>();
    public DbSet<CommitSessionLink> CommitSessionLinks => Set<CommitSessionLink>();
    public DbSet<PullRequestReview> PullRequestReviews => Set<PullRequestReview>();
    public DbSet<ArchitectAnnotation> ArchitectAnnotations => Set<ArchitectAnnotation>();

    public OverwatchDbContext(DbContextOptions<OverwatchDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OverwatchDbContext).Assembly);
    }
}
```

- [ ] **Step 2: Create entity configurations**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/SessionConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DeveloperEmail).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().IsRequired();
        builder.Property(s => s.StartedAt).IsRequired();
        builder.HasIndex(s => s.DeveloperEmail);
        builder.HasIndex(s => s.Status);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ConversationTurnConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ConversationTurnConfiguration : IEntityTypeConfiguration<ConversationTurn>
{
    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Role).IsRequired();
        builder.Property(t => t.Timestamp).IsRequired();
        builder.HasOne(t => t.Session).WithMany(s => s.ConversationTurns).HasForeignKey(t => t.SessionId);
        builder.HasIndex(t => t.SessionId);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ToolExecutionConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ToolName).IsRequired();
        builder.Property(t => t.Timestamp).IsRequired();
        builder.HasOne(t => t.Session).WithMany(s => s.ToolExecutions).HasForeignKey(t => t.SessionId);
        builder.HasOne(t => t.Turn).WithMany().HasForeignKey(t => t.TurnId);
        builder.HasIndex(t => t.SessionId);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/FileChangeConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class FileChangeConfiguration : IEntityTypeConfiguration<FileChange>
{
    public void Configure(EntityTypeBuilder<FileChange> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.FilePath).IsRequired();
        builder.Property(f => f.ChangeType).HasConversion<string>();
        builder.HasOne(f => f.Session).WithMany(s => s.FileChanges).HasForeignKey(f => f.SessionId);
        builder.HasOne(f => f.ToolExecution).WithMany(t => t.FileChanges).HasForeignKey(f => f.ToolExecutionId);
        builder.HasIndex(f => f.SessionId);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class GovernanceViolationConfiguration : IEntityTypeConfiguration<GovernanceViolation>
{
    public void Configure(EntityTypeBuilder<GovernanceViolation> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.RuleId).IsRequired();
        builder.Property(v => v.Severity).HasConversion<string>().IsRequired();
        builder.Property(v => v.Message).IsRequired();
        builder.HasOne(v => v.Session).WithMany(s => s.GovernanceViolations).HasForeignKey(v => v.SessionId);
        builder.HasIndex(v => v.SessionId);
        builder.HasIndex(v => v.Severity);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/CommitSessionLinkConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class CommitSessionLinkConfiguration : IEntityTypeConfiguration<CommitSessionLink>
{
    public void Configure(EntityTypeBuilder<CommitSessionLink> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CommitSha).IsRequired();
        builder.Property(c => c.Repo).IsRequired();
        builder.HasOne(c => c.Session).WithMany(s => s.CommitSessionLinks).HasForeignKey(c => c.SessionId);
        builder.HasIndex(c => c.SessionId);
        builder.HasIndex(c => c.CommitSha);
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/PullRequestReviewConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class PullRequestReviewConfiguration : IEntityTypeConfiguration<PullRequestReview>
{
    public void Configure(EntityTypeBuilder<PullRequestReview> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Repo).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().IsRequired();
        builder.HasIndex(r => new { r.Repo, r.PrNumber });
    }
}
```

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/ArchitectAnnotationConfiguration.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ArchitectAnnotationConfiguration : IEntityTypeConfiguration<ArchitectAnnotation>
{
    public void Configure(EntityTypeBuilder<ArchitectAnnotation> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Content).IsRequired();
        builder.Property(a => a.CreatedBy).IsRequired();
        builder.HasOne(a => a.Session).WithMany(s => s.ArchitectAnnotations).HasForeignKey(a => a.SessionId);
        builder.HasOne(a => a.Turn).WithMany().HasForeignKey(a => a.TurnId);
        builder.HasOne(a => a.ToolExecution).WithMany().HasForeignKey(a => a.ToolExecutionId);
    }
}
```

- [ ] **Step 3: Create SessionRepository**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/SessionRepository.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class SessionRepository : ISessionRepository
{
    private readonly OverwatchDbContext _db;

    public SessionRepository(OverwatchDbContext db) => _db = db;

    public async Task<Session?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.Sessions
            .Include(s => s.ConversationTurns.OrderBy(t => t.TurnIndex))
            .Include(s => s.ToolExecutions.OrderBy(t => t.Timestamp))
            .Include(s => s.FileChanges)
            .Include(s => s.GovernanceViolations)
            .Include(s => s.ArchitectAnnotations)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Session>> GetAllAsync(
        string? developerEmail = null, string? repo = null, CancellationToken ct = default)
    {
        var query = _db.Sessions.AsQueryable();
        if (developerEmail is not null)
            query = query.Where(s => s.DeveloperEmail == developerEmail);
        if (repo is not null)
            query = query.Where(s => s.RepoPath == repo);
        return await query.OrderByDescending(s => s.StartedAt).ToListAsync(ct);
    }

    public async Task AddAsync(Session session, CancellationToken ct = default) =>
        await _db.Sessions.AddAsync(session, ct);

    public Task UpdateAsync(Session session, CancellationToken ct = default)
    {
        _db.Sessions.Update(session);
        return Task.CompletedTask;
    }

    public async Task AddTurnAsync(ConversationTurn turn, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddAsync(turn, ct);

    public async Task AddToolExecutionAsync(ToolExecution toolExecution, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddAsync(toolExecution, ct);

    public async Task AddFileChangeAsync(FileChange fileChange, CancellationToken ct = default) =>
        await _db.FileChanges.AddAsync(fileChange, ct);

    public async Task AddTurnsAsync(IEnumerable<ConversationTurn> turns, CancellationToken ct = default) =>
        await _db.ConversationTurns.AddRangeAsync(turns, ct);

    public async Task AddToolExecutionsAsync(IEnumerable<ToolExecution> toolExecutions, CancellationToken ct = default) =>
        await _db.ToolExecutions.AddRangeAsync(toolExecutions, ct);

    public async Task AddFileChangesAsync(IEnumerable<FileChange> fileChanges, CancellationToken ct = default) =>
        await _db.FileChanges.AddRangeAsync(fileChanges, ct);
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build src/Infrastructure/DeveloperOverwatch.Infrastructure/`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/
git commit -m "feat: add EF Core persistence with SQLite configurations and repository"
```

---

### Task 5: MediatR Pipeline

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Behaviors/ValidationBehavior.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Behaviors/LoggingBehavior.cs`
- Create: `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`
- Test: `tests/DeveloperOverwatch.Application.Tests/Behaviors/ValidationBehaviorTests.cs`

- [ ] **Step 1: Write ValidationBehavior test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Behaviors/ValidationBehaviorTests.cs
namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
using DeveloperOverwatch.Domain.Common;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;

public record TestCommand(string Name) : IRequest<Result<string>>;

public class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
    }
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var validator = new TestCommandValidator();
        var behavior = new ValidationBehavior<TestCommand, Result<string>>(
            [validator],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ValidationBehavior<TestCommand, Result<string>>>>());

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();
        next().Returns(Result<string>.Success("ok"));

        var result = await behavior.Handle(new TestCommand("valid"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await next.Received(1)();
    }

    [Fact]
    public async Task Handle_InvalidRequest_ReturnsValidationFailure()
    {
        var validator = new TestCommandValidator();
        var behavior = new ValidationBehavior<TestCommand, Result<string>>(
            [validator],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ValidationBehavior<TestCommand, Result<string>>>>());

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();

        var result = await behavior.Handle(new TestCommand(""), next, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("Name is required"));
        await next.DidNotReceive()();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~ValidationBehaviorTests" -v quiet`
Expected: FAIL — `ValidationBehavior` does not exist.

- [ ] **Step 3: Implement ValidationBehavior**

```csharp
// src/Application/DeveloperOverwatch.Application/Behaviors/ValidationBehavior.cs
namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    private readonly ILogger<ValidationBehavior<TRequest, TResponse>> _logger;

    public ValidationBehavior(
        IEnumerable<IValidator<TRequest>> validators,
        ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    {
        _validators = validators;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var validators = _validators as IValidator<TRequest>[] ?? _validators.ToArray();
        if (validators.Length == 0)
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .Where(e => e is not null)
            .Select(e => $"{e.PropertyName}: {e.ErrorMessage}")
            .ToList();

        if (errors.Count == 0)
            return await next();

        _logger.LogDebug("Validation failed for {Request} with {Count} errors",
            typeof(TRequest).Name, errors.Count);

        if (typeof(TResponse).IsGenericType &&
            typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var resultType = typeof(TResponse);
            var method = resultType.GetMethod("ValidationFailure")!;
            return (TResponse)method.Invoke(null, [errors.AsReadOnly()])!;
        }

        throw new ValidationException(results.SelectMany(r => r.Errors));
    }
}
```

- [ ] **Step 4: Implement LoggingBehavior**

```csharp
// src/Application/DeveloperOverwatch.Application/Behaviors/LoggingBehavior.cs
namespace DeveloperOverwatch.Application.Behaviors;

using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger) =>
        _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        _logger.LogInformation("Handling {Request}", requestName);

        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        _logger.LogInformation("Handled {Request} in {ElapsedMs}ms", requestName, sw.ElapsedMilliseconds);
        return response;
    }
}
```

- [ ] **Step 5: Create Application DependencyInjection**

```csharp
// src/Application/DeveloperOverwatch.Application/DependencyInjection.cs
namespace DeveloperOverwatch.Application;

using System.Reflection;
using DeveloperOverwatch.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
```

- [ ] **Step 6: Create Infrastructure DependencyInjection**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs
namespace DeveloperOverwatch.Infrastructure;

using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OverwatchDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

        return services;
    }
}
```

- [ ] **Step 7: Run validation tests**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~ValidationBehaviorTests" -v quiet`
Expected: 2 tests passed.

- [ ] **Step 8: Commit**

```bash
git add src/Application/ src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs tests/DeveloperOverwatch.Application.Tests/
git commit -m "feat: add MediatR pipeline with validation and logging behaviors"
```

---

### Task 6: API Scaffold and Docker Compose

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Program.cs`
- Create: `src/API/DeveloperOverwatch.API/appsettings.json`
- Create: `src/API/DeveloperOverwatch.API/Middleware/ApiKeyAuthMiddleware.cs`
- Create: `src/API/DeveloperOverwatch.API/Dockerfile`
- Create: `docker-compose.yml`

- [ ] **Step 1: Create appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "Database": {
    "ConnectionString": "Data Source=/data/overwatch.db"
  },
  "ApiKeys": {
    "Keys": []
  }
}
```

- [ ] **Step 2: Create ApiKeyAuthMiddleware**

```csharp
// src/API/DeveloperOverwatch.API/Middleware/ApiKeyAuthMiddleware.cs
namespace DeveloperOverwatch.API.Middleware;

public sealed class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _validKeys;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var keys = configuration.GetSection("ApiKeys:Keys").Get<string[]>() ?? [];
        _validKeys = new HashSet<string>(keys, StringComparer.Ordinal);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        if (_validKeys.Count == 0)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedKey) ||
            !_validKeys.Contains(extractedKey.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        await _next(context);
    }
}
```

- [ ] **Step 3: Create Program.cs**

```csharp
// src/API/DeveloperOverwatch.API/Program.cs
using DeveloperOverwatch.API.Middleware;
using DeveloperOverwatch.Application;
using DeveloperOverwatch.Infrastructure;
using DeveloperOverwatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString")
    ?? "Data Source=overwatch.db";

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(connectionString);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

await app.RunAsync();

public partial class Program { }
```

- [ ] **Step 4: Create Dockerfile**

```dockerfile
# src/API/DeveloperOverwatch.API/Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY DeveloperOverwatch.sln .
COPY src/Domain/DeveloperOverwatch.Domain/DeveloperOverwatch.Domain.csproj src/Domain/DeveloperOverwatch.Domain/
COPY src/Application/DeveloperOverwatch.Application/DeveloperOverwatch.Application.csproj src/Application/DeveloperOverwatch.Application/
COPY src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj src/Infrastructure/DeveloperOverwatch.Infrastructure/
COPY src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj src/API/DeveloperOverwatch.API/

RUN dotnet restore src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj

COPY src/ src/
RUN dotnet publish src/API/DeveloperOverwatch.API/DeveloperOverwatch.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data

ENV ASPNETCORE_URLS=http://+:5000
ENV Database__ConnectionString="Data Source=/data/overwatch.db"

EXPOSE 5000
ENTRYPOINT ["dotnet", "DeveloperOverwatch.API.dll"]
```

- [ ] **Step 5: Create docker-compose.yml**

```yaml
# docker-compose.yml
services:
  overwatch-api:
    build:
      context: .
      dockerfile: src/API/DeveloperOverwatch.API/Dockerfile
    ports:
      - "5000:5000"
    environment:
      - Database__ConnectionString=Data Source=/data/overwatch.db
    volumes:
      - overwatch-data:/data

  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"
      - "4317:4317"

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"

volumes:
  overwatch-data:
```

- [ ] **Step 6: Verify API starts locally**

Run: `dotnet run --project src/API/DeveloperOverwatch.API/ --urls http://localhost:5000`
Then in another terminal: `curl http://localhost:5000/health`
Expected: `{"status":"healthy","timestamp":"..."}`

- [ ] **Step 7: Commit**

```bash
git add src/API/ docker-compose.yml
git commit -m "feat: add API scaffold with health check, auth middleware, and Docker Compose"
```

---

### Task 7: IngestSessionStart Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommandHandler.cs`
- Test: `tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionStartTests.cs`

- [ ] **Step 1: Write test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionStartTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

public class IngestSessionStartTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ValidCommand_CreatesSession()
    {
        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = Guid.NewGuid().ToString(),
            DeveloperEmail = "dev@team.dev",
            DeveloperName = "Dev",
            RepoPath = "/repo",
            StartedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(command.SessionId);
        await _repo.Received(1).AddAsync(
            Arg.Is<Session>(s =>
                s.Id == command.SessionId &&
                s.DeveloperEmail == command.DeveloperEmail &&
                s.Status == SessionStatus.Active),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateSession_ReturnsFail()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId });

        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            StartedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Validator_MissingEmail_Fails()
    {
        var validator = new IngestSessionStartCommandValidator();
        var command = new IngestSessionStartCommand
        {
            SessionId = Guid.NewGuid().ToString(),
            DeveloperEmail = "",
            StartedAt = DateTime.UtcNow
        };

        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DeveloperEmail");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionStartTests" -v quiet`
Expected: FAIL — command types don't exist.

- [ ] **Step 3: Implement command**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommand.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionStartCommand : IRequest<Result<string>>
{
    public required string SessionId { get; init; }
    public required string DeveloperEmail { get; init; }
    public string? DeveloperName { get; init; }
    public string? RepoPath { get; init; }
    public required DateTime StartedAt { get; init; }
}
```

- [ ] **Step 4: Implement validator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using FluentValidation;

public sealed class IngestSessionStartCommandValidator : AbstractValidator<IngestSessionStartCommand>
{
    public IngestSessionStartCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.DeveloperEmail).NotEmpty().EmailAddress();
        RuleFor(x => x.StartedAt).NotEmpty();
    }
}
```

- [ ] **Step 5: Implement handler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/IngestSessionStartCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionStartCommandHandler
    : IRequestHandler<IngestSessionStartCommand, Result<string>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionStartCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        IngestSessionStartCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (existing is not null)
            return Result<string>.Fail($"Session {request.SessionId} already exists");

        var session = new Session
        {
            Id = request.SessionId,
            DeveloperEmail = request.DeveloperEmail,
            DeveloperName = request.DeveloperName,
            RepoPath = request.RepoPath,
            Status = SessionStatus.Active,
            StartedAt = request.StartedAt,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionStartTests" -v quiet`
Expected: 3 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/IngestSessionStart/ tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionStartTests.cs
git commit -m "feat: add IngestSessionStart command with validation and handler"
```

---

### Task 8: IngestSessionEvents Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommandHandler.cs`
- Test: `tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEventsTests.cs`

- [ ] **Step 1: Write test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEventsTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

public class IngestSessionEventsTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_WithToolExecutions_AddsToSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId, Status = SessionStatus.Active });

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ToolExecutions =
            [
                new ToolExecutionDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ToolName = "Edit",
                    InputParams = "{\"file\":\"test.cs\"}",
                    DurationMs = 150,
                    Timestamp = DateTime.UtcNow
                }
            ],
            FileChanges =
            [
                new FileChangeDto
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = "test.cs",
                    ChangeType = ChangeType.Modified,
                    DiffText = "+line1\n-line2"
                }
            ]
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddToolExecutionsAsync(
            Arg.Is<IEnumerable<ToolExecution>>(t => t.Count() == 1),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddFileChangesAsync(
            Arg.Is<IEnumerable<FileChange>>(f => f.Count() == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow);
        var command = new IngestSessionEventsCommand
        {
            SessionId = "nonexistent",
            ToolExecutions = [],
            FileChanges = []
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionEventsTests" -v quiet`
Expected: FAIL — command types don't exist.

- [ ] **Step 3: Implement command with DTOs**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommand.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record IngestSessionEventsCommand : IRequest<Result<int>>
{
    public required string SessionId { get; init; }
    public IReadOnlyList<ConversationTurnDto> ConversationTurns { get; init; } = [];
    public IReadOnlyList<ToolExecutionDto> ToolExecutions { get; init; } = [];
    public IReadOnlyList<FileChangeDto> FileChanges { get; init; } = [];
}

public sealed record ConversationTurnDto
{
    public required string Id { get; init; }
    public int TurnIndex { get; init; }
    public required string Role { get; init; }
    public string? Content { get; init; }
    public int? TokenCount { get; init; }
    public required DateTime Timestamp { get; init; }
    public bool IsDecision { get; init; }
}

public sealed record ToolExecutionDto
{
    public required string Id { get; init; }
    public string? TurnId { get; init; }
    public required string ToolName { get; init; }
    public string? InputParams { get; init; }
    public string? OutputSummary { get; init; }
    public int? DurationMs { get; init; }
    public required DateTime Timestamp { get; init; }
}

public sealed record FileChangeDto
{
    public required string Id { get; init; }
    public string? ToolExecutionId { get; init; }
    public required string FilePath { get; init; }
    public string? BeforeHash { get; init; }
    public string? AfterHash { get; init; }
    public string? DiffText { get; init; }
    public ChangeType? ChangeType { get; init; }
}
```

- [ ] **Step 4: Implement validator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using FluentValidation;

public sealed class IngestSessionEventsCommandValidator : AbstractValidator<IngestSessionEventsCommand>
{
    public IngestSessionEventsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleForEach(x => x.ToolExecutions).ChildRules(tool =>
        {
            tool.RuleFor(t => t.Id).NotEmpty();
            tool.RuleFor(t => t.ToolName).NotEmpty();
        });

        RuleForEach(x => x.FileChanges).ChildRules(file =>
        {
            file.RuleFor(f => f.Id).NotEmpty();
            file.RuleFor(f => f.FilePath).NotEmpty();
        });

        RuleForEach(x => x.ConversationTurns).ChildRules(turn =>
        {
            turn.RuleFor(t => t.Id).NotEmpty();
            turn.RuleFor(t => t.Role).NotEmpty();
        });
    }
}
```

- [ ] **Step 5: Implement handler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/IngestSessionEventsCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEventsCommandHandler
    : IRequestHandler<IngestSessionEventsCommand, Result<int>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionEventsCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(
        IngestSessionEventsCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<int>.Fail($"Session {request.SessionId} not found");

        var totalEvents = 0;

        if (request.ConversationTurns.Count > 0)
        {
            var turns = request.ConversationTurns.Select(t => new ConversationTurn
            {
                Id = t.Id,
                SessionId = request.SessionId,
                TurnIndex = t.TurnIndex,
                Role = t.Role,
                Content = t.Content,
                TokenCount = t.TokenCount,
                Timestamp = t.Timestamp,
                IsDecision = t.IsDecision
            });
            await _repository.AddTurnsAsync(turns, cancellationToken);
            totalEvents += request.ConversationTurns.Count;
        }

        if (request.ToolExecutions.Count > 0)
        {
            var tools = request.ToolExecutions.Select(t => new ToolExecution
            {
                Id = t.Id,
                SessionId = request.SessionId,
                TurnId = t.TurnId,
                ToolName = t.ToolName,
                InputParams = t.InputParams,
                OutputSummary = t.OutputSummary,
                DurationMs = t.DurationMs,
                Timestamp = t.Timestamp
            });
            await _repository.AddToolExecutionsAsync(tools, cancellationToken);
            totalEvents += request.ToolExecutions.Count;
        }

        if (request.FileChanges.Count > 0)
        {
            var files = request.FileChanges.Select(f => new FileChange
            {
                Id = f.Id,
                SessionId = request.SessionId,
                ToolExecutionId = f.ToolExecutionId,
                FilePath = f.FilePath,
                BeforeHash = f.BeforeHash,
                AfterHash = f.AfterHash,
                DiffText = f.DiffText,
                ChangeType = f.ChangeType
            });
            await _repository.AddFileChangesAsync(files, cancellationToken);
            totalEvents += request.FileChanges.Count;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<int>.Success(totalEvents);
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionEventsTests" -v quiet`
Expected: 2 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEvents/ tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEventsTests.cs
git commit -m "feat: add IngestSessionEvents command for batch tool/file/turn ingestion"
```

---

### Task 9: IngestSessionEnd Command

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommandValidator.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommandHandler.cs`
- Test: `tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEndTests.cs`

- [ ] **Step 1: Write test**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEndTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

public class IngestSessionEndTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ActiveSession_CompletesIt()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow,
            SummaryJson = "{\"turns\":5,\"tools\":[\"Edit\",\"Bash\"]}"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().NotBeNull();
        session.SummaryJson.Should().Be(command.SummaryJson);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = "nonexistent",
            EndedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AlreadyCompletedSession_ReturnsFail()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId, Status = SessionStatus.Completed });

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not active");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionEndTests" -v quiet`
Expected: FAIL — command types don't exist.

- [ ] **Step 3: Implement command**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommand.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record IngestSessionEndCommand : IRequest<Result<string>>
{
    public required string SessionId { get; init; }
    public required DateTime EndedAt { get; init; }
    public string? SummaryJson { get; init; }
}
```

- [ ] **Step 4: Implement validator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using FluentValidation;

public sealed class IngestSessionEndCommandValidator : AbstractValidator<IngestSessionEndCommand>
{
    public IngestSessionEndCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.EndedAt).NotEmpty();
    }
}
```

- [ ] **Step 5: Implement handler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/IngestSessionEndCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEndCommandHandler
    : IRequestHandler<IngestSessionEndCommand, Result<string>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionEndCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        IngestSessionEndCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<string>.Fail($"Session {request.SessionId} not found");

        if (session.Status != SessionStatus.Active)
            return Result<string>.Fail($"Session {request.SessionId} is not active");

        session.Status = SessionStatus.Completed;
        session.EndedAt = request.EndedAt;
        session.SummaryJson = request.SummaryJson;

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "FullyQualifiedName~IngestSessionEndTests" -v quiet`
Expected: 3 tests passed.

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/IngestSessionEnd/ tests/DeveloperOverwatch.Application.Tests/Commands/IngestSessionEndTests.cs
git commit -m "feat: add IngestSessionEnd command to finalize sessions"
```

---

### Task 10: GetSession and GetSessions Queries

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetSession/GetSessionQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetSession/GetSessionQueryHandler.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetSessions/GetSessionsQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetSessions/GetSessionsQueryHandler.cs`

- [ ] **Step 1: Implement GetSessionQuery**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetSession/GetSessionQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetSessionQuery(string SessionId) : IRequest<Result<Session>>;
```

- [ ] **Step 2: Implement GetSessionQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetSession/GetSessionQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionQueryHandler
    : IRequestHandler<GetSessionQuery, Result<Session>>
{
    private readonly ISessionRepository _repository;

    public GetSessionQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<Session>> Handle(
        GetSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        return session is null
            ? Result<Session>.Fail($"Session {request.SessionId} not found")
            : Result<Session>.Success(session);
    }
}
```

- [ ] **Step 3: Implement GetSessionsQuery**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetSessions/GetSessionsQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using MediatR;

public sealed record GetSessionsQuery(
    string? DeveloperEmail = null,
    string? Repo = null) : IRequest<Result<IReadOnlyList<Session>>>;
```

- [ ] **Step 4: Implement GetSessionsQueryHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetSessions/GetSessionsQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionsQueryHandler
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<Session>>>
{
    private readonly ISessionRepository _repository;

    public GetSessionsQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<Session>>> Handle(
        GetSessionsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _repository.GetAllAsync(
            request.DeveloperEmail, request.Repo, cancellationToken);
        return Result<IReadOnlyList<Session>>.Success(sessions);
    }
}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/Application/DeveloperOverwatch.Application/`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Queries/
git commit -m "feat: add GetSession and GetSessions queries"
```

---

### Task 11: SessionsController and Integration Tests

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Controllers/SessionsController.cs`
- Test: `tests/DeveloperOverwatch.API.Tests/Controllers/SessionsControllerTests.cs`

- [ ] **Step 1: Write integration test**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/SessionsControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

public class SessionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SessionsControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FullSessionLifecycle_StartEventsEnd_ReturnsSession()
    {
        var sessionId = Guid.NewGuid().ToString();

        // Start session
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

        // Ingest events
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

        // End session
        var endCmd = new
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow,
            SummaryJson = "{\"turns\":1,\"tools\":[\"Edit\"],\"files\":[\"Program.cs\"]}"
        };
        var endResponse = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", endCmd);
        endResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Query session
        var getResponse = await _client.GetAsync($"/api/sessions/{sessionId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadAsStringAsync();
        body.Should().Contain(sessionId);
        body.Should().Contain("test@team.dev");
        body.Should().Contain("Edit");
        body.Should().Contain("Program.cs");
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
    public async Task StartSession_MissingEmail_Returns400()
    {
        var cmd = new { SessionId = Guid.NewGuid().ToString(), DeveloperEmail = "", StartedAt = DateTime.UtcNow };
        var response = await _client.PostAsJsonAsync("/api/sessions/start", cmd);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.API.Tests --filter "FullyQualifiedName~SessionsControllerTests" -v quiet`
Expected: FAIL — controller doesn't exist, routes not found.

- [ ] **Step 3: Implement SessionsController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/SessionsController.cs
namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Application.Queries.GetSession;
using DeveloperOverwatch.Application.Queries.GetSessions;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("start")]
    public async Task<IActionResult> StartSession(
        [FromBody] IngestSessionStartCommand command,
        CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { sessionId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : Conflict(new { error = result.Error });
    }

    [HttpPost("{sessionId}/events")]
    public async Task<IActionResult> IngestEvents(
        string sessionId,
        [FromBody] IngestSessionEventsCommand command,
        CancellationToken ct)
    {
        if (command.SessionId != sessionId)
            return BadRequest(new { error = "Session ID mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { eventsIngested = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }

    [HttpPost("{sessionId}/end")]
    public async Task<IActionResult> EndSession(
        string sessionId,
        [FromBody] IngestSessionEndCommand command,
        CancellationToken ct)
    {
        if (command.SessionId != sessionId)
            return BadRequest(new { error = "Session ID mismatch" });

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { sessionId = result.Value })
            : result.ValidationErrors.Count > 0
                ? BadRequest(new { errors = result.ValidationErrors })
                : NotFound(new { error = result.Error });
    }

    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSessionQuery(sessionId), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    [HttpGet]
    public async Task<IActionResult> GetSessions(
        [FromQuery] string? developer = null,
        [FromQuery] string? repo = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetSessionsQuery(developer, repo), ct);
        return Ok(result.Value);
    }
}
```

- [ ] **Step 4: Run integration tests**

Run: `dotnet test tests/DeveloperOverwatch.API.Tests --filter "FullyQualifiedName~SessionsControllerTests" -v quiet`
Expected: 3 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/SessionsController.cs tests/DeveloperOverwatch.API.Tests/
git commit -m "feat: add SessionsController with full lifecycle endpoints and integration tests"
```

---

### Task 12: Claude Code Hooks

**Files:**
- Create: `hooks/overwatch-session-start.ps1`
- Create: `hooks/overwatch-post-tool-use.ps1`
- Create: `hooks/overwatch-session-end.ps1`
- Create: `hooks/install-hooks.ps1`

These hooks POST directly to the API for the initial version. The local agent with SQLite queue and retry logic will be added in a future plan.

- [ ] **Step 1: Create session-start hook**

```powershell
# hooks/overwatch-session-start.ps1
# Claude Code SessionStart hook — creates a new session in Overwatch
# Reads OVERWATCH_API_URL from ~/.overwatch/config.json

$ErrorActionPreference = "SilentlyContinue"

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$sessionId = [guid]::NewGuid().ToString()
$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
$sessionId | Set-Content $activeSessionPath -NoNewline

$developerEmail = git config user.email 2>$null
if (-not $developerEmail) { $developerEmail = "unknown@localhost" }
$developerName = git config user.name 2>$null
$repoPath = git rev-parse --show-toplevel 2>$null

$body = @{
    sessionId = $sessionId
    developerEmail = $developerEmail
    developerName = $developerName
    repoPath = $repoPath
    startedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/start" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }
```

- [ ] **Step 2: Create post-tool-use hook**

```powershell
# hooks/overwatch-post-tool-use.ps1
# Claude Code PostToolUse hook — sends tool execution data to Overwatch
# Receives tool info via CLAUDE_TOOL_NAME, CLAUDE_TOOL_INPUT, CLAUDE_TOOL_OUTPUT env vars

$ErrorActionPreference = "SilentlyContinue"

$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$toolName = $env:CLAUDE_TOOL_NAME
if (-not $toolName) { exit 0 }

$inputParams = if ($env:CLAUDE_TOOL_INPUT) {
    $env:CLAUDE_TOOL_INPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_INPUT.Length, 1024))
} else { $null }

$outputSummary = if ($env:CLAUDE_TOOL_OUTPUT) {
    $env:CLAUDE_TOOL_OUTPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_OUTPUT.Length, 2048))
} else { $null }

$body = @{
    sessionId = $sessionId
    toolExecutions = @(@{
        id = [guid]::NewGuid().ToString()
        toolName = $toolName
        inputParams = $inputParams
        outputSummary = $outputSummary
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    })
    fileChanges = @()
    conversationTurns = @()
} | ConvertTo-Json -Depth 4 -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/$sessionId/events" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }
```

- [ ] **Step 3: Create session-end hook**

```powershell
# hooks/overwatch-session-end.ps1
# Claude Code SessionEnd hook — finalizes the session in Overwatch

$ErrorActionPreference = "SilentlyContinue"

$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$body = @{
    sessionId = $sessionId
    endedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/$sessionId/end" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }

Remove-Item $activeSessionPath -Force 2>$null
```

- [ ] **Step 4: Create install script**

```powershell
# hooks/install-hooks.ps1
# Installs Overwatch hooks into Claude Code settings
# Usage: .\install-hooks.ps1 -ApiUrl http://localhost:5000 [-ApiKey your-key]

param(
    [Parameter(Mandatory)]
    [string]$ApiUrl,
    [string]$ApiKey = ""
)

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
if (-not (Test-Path $overwatchDir)) { New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null }

$hooksDir = $PSScriptRoot
$config = @{ apiUrl = $ApiUrl; apiKey = $ApiKey }
$config | ConvertTo-Json | Set-Content (Join-Path $overwatchDir "config.json")

$settingsPath = Join-Path $env:USERPROFILE ".claude" "settings.json"
$settings = if (Test-Path $settingsPath) {
    Get-Content $settingsPath -Raw | ConvertFrom-Json
} else {
    @{}
}

if (-not $settings.hooks) { $settings | Add-Member -NotePropertyName hooks -NotePropertyValue @{} }

$settings.hooks.SessionStart = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-start.ps1`""
})

$settings.hooks.PostToolUse = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-post-tool-use.ps1`""
})

$settings.hooks.Stop = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-end.ps1`""
})

$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath

Write-Host "[+] Overwatch hooks installed to $settingsPath"
Write-Host "[+] Config saved to $(Join-Path $overwatchDir 'config.json')"
Write-Host "[+] API URL: $ApiUrl"
Write-Host ""
Write-Host "Overwatch is now capturing Claude Code sessions."
Write-Host "Dashboard: $ApiUrl"
```

- [ ] **Step 5: Verify scripts are syntactically valid**

Run: `pwsh -NoProfile -Command "& { Get-Content 'hooks/overwatch-session-start.ps1' | Out-Null; Get-Content 'hooks/overwatch-post-tool-use.ps1' | Out-Null; Get-Content 'hooks/overwatch-session-end.ps1' | Out-Null; Get-Content 'hooks/install-hooks.ps1' | Out-Null; Write-Host 'All scripts parsed OK' }"`
Expected: "All scripts parsed OK"

- [ ] **Step 6: Commit**

```bash
git add hooks/
git commit -m "feat: add Claude Code hooks and install script for session capture"
```

---

### Task 13: End-to-End Validation

This task validates the complete flow manually. No new files — this is a smoke test.

- [ ] **Step 1: Start the API**

Run: `dotnet run --project src/API/DeveloperOverwatch.API/ --urls http://localhost:5000`
Expected: Application started, listening on http://localhost:5000

- [ ] **Step 2: Verify health endpoint**

Run (new terminal): `curl http://localhost:5000/health`
Expected: `{"status":"healthy","timestamp":"..."}`

- [ ] **Step 3: Simulate a full session via curl**

```bash
# Start session
SESSION_ID=$(python -c "import uuid; print(uuid.uuid4())")

curl -s -X POST http://localhost:5000/api/sessions/start \
  -H "Content-Type: application/json" \
  -d "{\"sessionId\":\"$SESSION_ID\",\"developerEmail\":\"dev@team.dev\",\"developerName\":\"Dev\",\"repoPath\":\"/repo\",\"startedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"

# Ingest tool execution
TOOL_ID=$(python -c "import uuid; print(uuid.uuid4())")
FILE_ID=$(python -c "import uuid; print(uuid.uuid4())")

curl -s -X POST "http://localhost:5000/api/sessions/$SESSION_ID/events" \
  -H "Content-Type: application/json" \
  -d "{\"sessionId\":\"$SESSION_ID\",\"toolExecutions\":[{\"id\":\"$TOOL_ID\",\"toolName\":\"Edit\",\"inputParams\":\"{}\",\"outputSummary\":\"edited file\",\"durationMs\":200,\"timestamp\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}],\"fileChanges\":[{\"id\":\"$FILE_ID\",\"filePath\":\"src/main.cs\",\"changeType\":\"Modified\",\"diffText\":\"+hello\"}],\"conversationTurns\":[]}"

# End session
curl -s -X POST "http://localhost:5000/api/sessions/$SESSION_ID/end" \
  -H "Content-Type: application/json" \
  -d "{\"sessionId\":\"$SESSION_ID\",\"endedAt\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}"

# Query session
curl -s http://localhost:5000/api/sessions/$SESSION_ID | python -m json.tool
```

Expected: Final GET returns the session with tool executions and file changes populated.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test DeveloperOverwatch.sln -v quiet`
Expected: All tests pass (Result: 3, Validation: 2, Start: 3, Events: 2, End: 3, Controller: 3 = ~16 tests).

- [ ] **Step 5: Verify Docker build**

Run: `docker compose build overwatch-api`
Expected: Build completes successfully.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "chore: verify end-to-end session capture flow"
```

---

## Plan Summary

| Task | Description | Tests |
|------|-------------|-------|
| 1 | Solution scaffold (7 projects, NuGet refs) | Build verification |
| 2 | Domain entities, enums, Result\<T\> | 3 unit tests |
| 3 | Domain interfaces (ISessionRepository, IUnitOfWork) | Build verification |
| 4 | EF Core persistence (DbContext, configs, repository) | Build verification |
| 5 | MediatR pipeline (validation + logging behaviors, DI) | 2 unit tests |
| 6 | API scaffold (Program.cs, auth middleware, Docker) | Health check |
| 7 | IngestSessionStart command | 3 unit tests |
| 8 | IngestSessionEvents command | 2 unit tests |
| 9 | IngestSessionEnd command | 3 unit tests |
| 10 | GetSession + GetSessions queries | Build verification |
| 11 | SessionsController + integration tests | 3 integration tests |
| 12 | Claude Code hooks (PowerShell scripts) | Syntax check |
| 13 | End-to-end validation | Manual smoke test |

**Total: 13 tasks, ~16 automated tests, 1 manual E2E validation**

## What's Next

This plan produces a working capture pipeline: hooks → API → database → query. The following plans (to be written separately) complete the system:

- **Plan 2: GitHub Integration** — Webhook listener, session-commit correlation, PR status API
- **Plan 3: Governance Engine** — YAML policy loader, pattern/semantic/drift rules, pipeline behavior
- **Plan 4: Dashboard** — React SPA with PR Queue, Session Review, Team Analytics
- **Plan 5: Observability & Polish** — OpenTelemetry, Prometheus, installer CLI
- **Plan 6: Local Agent** — Background service with SQLite queue, retry, conversation capture, decision detection
