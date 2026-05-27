# Governance Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a YAML-driven governance engine that automatically evaluates sessions against configurable policy rules on session end, detects pattern violations via regex, tracks drift across sessions, and provides API endpoints to query and resolve violations.

**Architecture:** GovernanceBehavior (MediatR pipeline) triggers after IngestSessionEnd succeeds. GovernanceEngine orchestrates PolicyLoader (YAML parsing) + PatternRuleEvaluator (regex matching) + DriftDetector (rolling window). All violations are persisted via IGovernanceViolationRepository. Semantic rules are stubbed for future LLM integration. GovernanceController exposes GET/POST endpoints for violations.

**Tech Stack:** .NET 10, MediatR 12, FluentValidation 11, YamlDotNet (new), EF Core + SQLite, xUnit + FluentAssertions + NSubstitute

---

## File Structure

### Create
- `src/Domain/DeveloperOverwatch.Domain/Enums/PolicyType.cs`
- `src/Domain/DeveloperOverwatch.Domain/Enums/ScanTarget.cs`
- `src/Domain/DeveloperOverwatch.Domain/Models/GovernancePolicy.cs`
- `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceEngine.cs`
- `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceConfig.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PolicyLoader.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PatternRuleEvaluator.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceEngine.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs`
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs`
- `src/Application/DeveloperOverwatch.Application/Behaviors/GovernanceBehavior.cs`
- `src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQuery.cs`
- `src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQueryHandler.cs`
- `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommand.cs`
- `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandHandler.cs`
- `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandValidator.cs`
- `src/API/DeveloperOverwatch.API/Controllers/GovernanceController.cs`
- `policies/default.yaml`
- `tests/DeveloperOverwatch.Application.Tests/Governance/PolicyLoaderTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Governance/PatternRuleEvaluatorTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceEngineTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Governance/DriftDetectorTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Behaviors/GovernanceBehaviorTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Queries/GetViolationsTests.cs`
- `tests/DeveloperOverwatch.Application.Tests/Commands/ResolveViolationTests.cs`
- `tests/DeveloperOverwatch.API.Tests/Controllers/GovernanceControllerTests.cs`

### Modify
- `src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs` (add CreatedAt, ResolutionReason)
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs` (map new columns)
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj` (add YamlDotNet)
- `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs` (register governance services)
- `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs` (register GovernanceBehavior)

---

### Task 1: Domain — Policy Models, Interfaces & Entity Enhancement

**Files:**
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/PolicyType.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Enums/ScanTarget.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Models/GovernancePolicy.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceEngine.cs`
- Create: `src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs`
- Modify: `src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs`

- [ ] **Step 1: Create PolicyType enum**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/PolicyType.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum PolicyType
{
    Pattern,
    Semantic
}
```

- [ ] **Step 2: Create ScanTarget enum**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Enums/ScanTarget.cs
namespace DeveloperOverwatch.Domain.Enums;

public enum ScanTarget
{
    Diffs,
    Conversation,
    Session
}
```

- [ ] **Step 3: Create GovernancePolicy record**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Models/GovernancePolicy.cs
namespace DeveloperOverwatch.Domain.Models;

using DeveloperOverwatch.Domain.Enums;

public sealed record GovernancePolicy
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ViolationSeverity Severity { get; init; }
    public required PolicyType Type { get; init; }
    public required IReadOnlyList<ScanTarget> Scan { get; init; }
    public IReadOnlyList<string> Patterns { get; init; } = [];
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
    public string? Rule { get; init; }
}
```

- [ ] **Step 4: Create IGovernanceEngine interface**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceEngine.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IGovernanceEngine
{
    Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(string sessionId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Create IGovernanceViolationRepository interface**

```csharp
// src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs
namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;

public interface IGovernanceViolationRepository
{
    Task<GovernanceViolation?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, ViolationSeverity? severity, CancellationToken ct = default);
    Task<IReadOnlyList<GovernanceViolation>> GetRecentAsync(TimeSpan window, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<GovernanceViolation> violations, CancellationToken ct = default);
    Task UpdateAsync(GovernanceViolation violation, CancellationToken ct = default);
}
```

- [ ] **Step 6: Add CreatedAt and ResolutionReason to GovernanceViolation**

Update `src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs` — add two properties:

```csharp
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
    public string? ResolutionReason { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Session? Session { get; set; }
}
```

- [ ] **Step 7: Update GovernanceViolationConfiguration**

Update `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs` — map new columns:

```csharp
namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
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
        builder.Property(v => v.ResolutionReason).HasMaxLength(1000);
        builder.HasOne(v => v.Session)
               .WithMany(s => s.GovernanceViolations)
               .HasForeignKey(v => v.SessionId);
        builder.HasIndex(v => v.SessionId);
        builder.HasIndex(v => v.Severity);
        builder.HasIndex(v => v.CreatedAt);
    }
}
```

- [ ] **Step 8: Verify build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings

- [ ] **Step 9: Commit**

```bash
git add src/Domain/DeveloperOverwatch.Domain/Enums/PolicyType.cs src/Domain/DeveloperOverwatch.Domain/Enums/ScanTarget.cs src/Domain/DeveloperOverwatch.Domain/Models/GovernancePolicy.cs src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceEngine.cs src/Domain/DeveloperOverwatch.Domain/Interfaces/IGovernanceViolationRepository.cs src/Domain/DeveloperOverwatch.Domain/Entities/GovernanceViolation.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Configurations/GovernanceViolationConfiguration.cs
git commit -m "feat: add governance domain models, interfaces, and entity enhancements"
```

---

### Task 2: Infrastructure — PolicyLoader (TDD)

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceConfig.cs`
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PolicyLoader.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Governance/PolicyLoaderTests.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj`

- [ ] **Step 1: Add YamlDotNet to Infrastructure csproj**

Add to `src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj` inside the existing `<ItemGroup>` with PackageReferences:

```xml
<PackageReference Include="YamlDotNet" Version="16.*" />
```

- [ ] **Step 2: Create GovernanceConfig options class**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceConfig.cs
namespace DeveloperOverwatch.Infrastructure.Governance;

public sealed class GovernanceConfig
{
    public string PoliciesPath { get; set; } = "policies";
    public int DriftWindowHours { get; set; } = 72;
    public int DriftThreshold { get; set; } = 3;
}
```

- [ ] **Step 3: Write the failing tests for PolicyLoader**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Governance/PolicyLoaderTests.cs
namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class PolicyLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PolicyLoader _loader;

    public PolicyLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"overwatch-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var config = Options.Create(new GovernanceConfig { PoliciesPath = _tempDir });
        _loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);
    }

    [Fact]
    public void LoadPolicies_ValidYaml_ReturnsPolicies()
    {
        var yaml = """
            policies:
              - id: no-raw-sql
                name: "No raw SQL"
                severity: error
                type: pattern
                scan: [diffs, conversation]
                patterns:
                  - "SqlCommand|SqlConnection"
                exclude_paths:
                  - "**/Repositories/**"
            """;
        File.WriteAllText(Path.Combine(_tempDir, "test.yaml"), yaml);

        var policies = _loader.LoadPolicies();

        policies.Should().HaveCount(1);
        var policy = policies[0];
        policy.Id.Should().Be("no-raw-sql");
        policy.Name.Should().Be("No raw SQL");
        policy.Severity.Should().Be(ViolationSeverity.Error);
        policy.Type.Should().Be(PolicyType.Pattern);
        policy.Scan.Should().BeEquivalentTo([ScanTarget.Diffs, ScanTarget.Conversation]);
        policy.Patterns.Should().ContainSingle().Which.Should().Be("SqlCommand|SqlConnection");
        policy.ExcludePaths.Should().ContainSingle().Which.Should().Be("**/Repositories/**");
    }

    [Fact]
    public void LoadPolicies_SemanticRule_ParsesRule()
    {
        var yaml = """
            policies:
              - id: auth-required
                name: "Auth required"
                severity: error
                type: semantic
                scan: [diffs]
                rule: "All endpoints must have [Authorize]"
            """;
        File.WriteAllText(Path.Combine(_tempDir, "semantic.yaml"), yaml);

        var policies = _loader.LoadPolicies();

        policies.Should().HaveCount(1);
        policies[0].Type.Should().Be(PolicyType.Semantic);
        policies[0].Rule.Should().Be("All endpoints must have [Authorize]");
    }

    [Fact]
    public void LoadPolicies_MultipleFiles_MergesAll()
    {
        var yaml1 = """
            policies:
              - id: rule-1
                name: "Rule 1"
                severity: info
                type: pattern
                scan: [diffs]
                patterns: ["pattern1"]
            """;
        var yaml2 = """
            policies:
              - id: rule-2
                name: "Rule 2"
                severity: warning
                type: pattern
                scan: [conversation]
                patterns: ["pattern2"]
            """;
        File.WriteAllText(Path.Combine(_tempDir, "a.yaml"), yaml1);
        File.WriteAllText(Path.Combine(_tempDir, "b.yml"), yaml2);

        var policies = _loader.LoadPolicies();

        policies.Should().HaveCount(2);
        policies.Select(p => p.Id).Should().BeEquivalentTo(["rule-1", "rule-2"]);
    }

    [Fact]
    public void LoadPolicies_MissingDirectory_ReturnsEmpty()
    {
        var config = Options.Create(new GovernanceConfig { PoliciesPath = "/nonexistent/path" });
        var loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);

        var policies = loader.LoadPolicies();

        policies.Should().BeEmpty();
    }

    [Fact]
    public void LoadPolicies_EmptyDirectory_ReturnsEmpty()
    {
        var policies = _loader.LoadPolicies();

        policies.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "PolicyLoaderTests" -v m`
Expected: FAIL — `PolicyLoader` class does not exist

- [ ] **Step 5: Implement PolicyLoader**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PolicyLoader.cs
namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public sealed class PolicyLoader
{
    private readonly GovernanceConfig _config;
    private readonly ILogger<PolicyLoader> _logger;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public PolicyLoader(IOptions<GovernanceConfig> config, ILogger<PolicyLoader> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public IReadOnlyList<GovernancePolicy> LoadPolicies()
    {
        if (!Directory.Exists(_config.PoliciesPath))
        {
            _logger.LogDebug("Policies directory {Path} not found, returning empty", _config.PoliciesPath);
            return [];
        }

        var files = Directory.GetFiles(_config.PoliciesPath, "*.yaml")
            .Concat(Directory.GetFiles(_config.PoliciesPath, "*.yml"))
            .ToArray();

        if (files.Length == 0)
            return [];

        var policies = new List<GovernancePolicy>();

        foreach (var file in files)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var doc = Deserializer.Deserialize<PolicyFileDto>(yaml);
                if (doc?.Policies is null) continue;

                foreach (var dto in doc.Policies)
                {
                    policies.Add(new GovernancePolicy
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        Severity = Enum.Parse<ViolationSeverity>(dto.Severity, ignoreCase: true),
                        Type = Enum.Parse<PolicyType>(dto.Type, ignoreCase: true),
                        Scan = dto.Scan.Select(s => Enum.Parse<ScanTarget>(s, ignoreCase: true)).ToList(),
                        Patterns = dto.Patterns ?? [],
                        ExcludePaths = dto.ExcludePaths ?? [],
                        Rule = dto.Rule
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse policy file {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} governance policies from {Dir}", policies.Count, _config.PoliciesPath);
        return policies;
    }

    private sealed class PolicyFileDto
    {
        public List<PolicyDto>? Policies { get; set; }
    }

    private sealed class PolicyDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> Scan { get; set; } = [];
        public List<string>? Patterns { get; set; }
        public List<string>? ExcludePaths { get; set; }
        public string? Rule { get; set; }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "PolicyLoaderTests" -v m`
Expected: 5 passed

- [ ] **Step 7: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/DeveloperOverwatch.Infrastructure.csproj src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceConfig.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PolicyLoader.cs tests/DeveloperOverwatch.Application.Tests/Governance/PolicyLoaderTests.cs
git commit -m "feat: add YAML policy loader with GovernanceConfig"
```

---

### Task 3: Infrastructure — PatternRuleEvaluator (TDD)

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PatternRuleEvaluator.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Governance/PatternRuleEvaluatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Governance/PatternRuleEvaluatorTests.cs
namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Xunit;

public class PatternRuleEvaluatorTests
{
    private readonly PatternRuleEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_PatternInDiff_ReturnsViolation()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand|SqlConnection"]);
        var session = CreateSession(
            diffs: [("src/Services/DataService.cs", "var cmd = new SqlCommand(query);")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("no-sql");
        violations[0].Severity.Should().Be(ViolationSeverity.Error);
        violations[0].Message.Should().Contain("SqlCommand");
        violations[0].Message.Should().Contain("DataService.cs");
    }

    [Fact]
    public void Evaluate_PatternInConversation_ReturnsViolation()
    {
        var policy = CreatePolicy("no-secrets", ViolationSeverity.Critical,
            [ScanTarget.Conversation], ["password\\s*=\\s*\"[^\"]+\""]);
        var session = CreateSession(
            conversations: [("assistant", "Here: password = \"hunter2\"")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().ContainSingle();
        violations[0].Message.Should().Contain("conversation turn");
    }

    [Fact]
    public void Evaluate_ExcludedPath_SkipsFile()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"],
            excludePaths: ["**/Repositories/**"]);
        var session = CreateSession(
            diffs: [("src/Repositories/UserRepo.cs", "var cmd = new SqlCommand();")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsEmpty()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"]);
        var session = CreateSession(
            diffs: [("src/Service.cs", "var result = await _db.Users.ToListAsync();")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NullDiffText_SkipsGracefully()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand"]);
        var session = new Session
        {
            Id = "s1",
            FileChanges = { new FileChange { Id = "fc1", SessionId = "s1", FilePath = "test.cs", DiffText = null } }
        };

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MultiplePatterns_FindsAll()
    {
        var policy = CreatePolicy("no-sql", ViolationSeverity.Error,
            [ScanTarget.Diffs], ["SqlCommand", "ExecuteSqlRaw"]);
        var session = CreateSession(
            diffs: [
                ("a.cs", "new SqlCommand()"),
                ("b.cs", "db.ExecuteSqlRaw(sql)")
            ]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_BothScanTargets_ScansAll()
    {
        var policy = CreatePolicy("no-secrets", ViolationSeverity.Critical,
            [ScanTarget.Diffs, ScanTarget.Conversation], ["api_key\\s*="]);
        var session = CreateSession(
            diffs: [("config.cs", "api_key = \"abc123\"")],
            conversations: [("user", "set api_key = xyz")]);

        var violations = _evaluator.Evaluate(policy, session);

        violations.Should().HaveCount(2);
    }

    private static GovernancePolicy CreatePolicy(
        string id, ViolationSeverity severity,
        List<ScanTarget> scan, List<string> patterns,
        List<string>? excludePaths = null) => new()
    {
        Id = id,
        Name = $"Test: {id}",
        Severity = severity,
        Type = PolicyType.Pattern,
        Scan = scan,
        Patterns = patterns,
        ExcludePaths = excludePaths ?? []
    };

    private static Session CreateSession(
        (string path, string diff)[]? diffs = null,
        (string role, string content)[]? conversations = null)
    {
        var session = new Session { Id = Guid.NewGuid().ToString() };

        if (diffs is not null)
        {
            foreach (var (path, diff) in diffs)
            {
                session.FileChanges.Add(new FileChange
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    FilePath = path,
                    DiffText = diff
                });
            }
        }

        if (conversations is not null)
        {
            for (var i = 0; i < conversations.Length; i++)
            {
                session.ConversationTurns.Add(new ConversationTurn
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = session.Id,
                    TurnIndex = i,
                    Role = conversations[i].role,
                    Content = conversations[i].content,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        return session;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "PatternRuleEvaluatorTests" -v m`
Expected: FAIL — `PatternRuleEvaluator` class does not exist

- [ ] **Step 3: Implement PatternRuleEvaluator**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PatternRuleEvaluator.cs
namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text.RegularExpressions;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;

public sealed class PatternRuleEvaluator
{
    public IReadOnlyList<GovernanceViolation> Evaluate(GovernancePolicy policy, Session session)
    {
        var violations = new List<GovernanceViolation>();
        var regexes = policy.Patterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();

        foreach (var target in policy.Scan)
        {
            switch (target)
            {
                case ScanTarget.Diffs:
                    EvaluateDiffs(policy, session, regexes, violations);
                    break;
                case ScanTarget.Conversation:
                    EvaluateConversation(policy, session, regexes, violations);
                    break;
            }
        }

        return violations;
    }

    private static void EvaluateDiffs(
        GovernancePolicy policy, Session session, Regex[] regexes, List<GovernanceViolation> violations)
    {
        foreach (var fileChange in session.FileChanges)
        {
            if (fileChange.DiffText is null)
                continue;
            if (IsExcluded(fileChange.FilePath, policy.ExcludePaths))
                continue;

            foreach (var regex in regexes)
            {
                var match = regex.Match(fileChange.DiffText);
                if (match.Success)
                {
                    violations.Add(CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in {fileChange.FilePath}"));
                }
            }
        }
    }

    private static void EvaluateConversation(
        GovernancePolicy policy, Session session, Regex[] regexes, List<GovernanceViolation> violations)
    {
        foreach (var turn in session.ConversationTurns)
        {
            if (turn.Content is null)
                continue;

            foreach (var regex in regexes)
            {
                var match = regex.Match(turn.Content);
                if (match.Success)
                {
                    violations.Add(CreateViolation(policy, session.Id,
                        $"Pattern '{match.Value}' found in conversation turn {turn.TurnIndex}"));
                }
            }
        }
    }

    private static bool IsExcluded(string filePath, IReadOnlyList<string> excludePaths)
    {
        foreach (var pattern in excludePaths)
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/\\\\]*") + "$";
            if (Regex.IsMatch(filePath, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    private static GovernanceViolation CreateViolation(GovernancePolicy policy, string sessionId, string message) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = sessionId,
        RuleId = policy.Id,
        Severity = policy.Severity,
        Message = message,
        CreatedAt = DateTime.UtcNow
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "PatternRuleEvaluatorTests" -v m`
Expected: 7 passed

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/PatternRuleEvaluator.cs tests/DeveloperOverwatch.Application.Tests/Governance/PatternRuleEvaluatorTests.cs
git commit -m "feat: add pattern rule evaluator with regex matching and path exclusions"
```

---

### Task 4: Infrastructure — GovernanceViolationRepository (TDD)

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceViolationRepositoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceViolationRepositoryTests.cs
namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class GovernanceViolationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly GovernanceViolationRepository _repo;

    public GovernanceViolationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new GovernanceViolationRepository(_db);
    }

    [Fact]
    public async Task AddRangeAndGetBySessionId_ReturnsViolations()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        var violations = new[]
        {
            new GovernanceViolation { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test 1", CreatedAt = DateTime.UtcNow },
            new GovernanceViolation { Id = "v2", SessionId = "s1", RuleId = "r2", Severity = ViolationSeverity.Warning, Message = "test 2", CreatedAt = DateTime.UtcNow }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s1");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySessionId_WithSeverityFilter_FiltersCorrectly()
    {
        var session = new Session { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violations = new[]
        {
            new GovernanceViolation { Id = "v3", SessionId = "s2", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow },
            new GovernanceViolation { Id = "v4", SessionId = "s2", RuleId = "r2", Severity = ViolationSeverity.Info, Message = "info", CreatedAt = DateTime.UtcNow }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s2", ViolationSeverity.Error);

        result.Should().ContainSingle().Which.Id.Should().Be("v3");
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsWithinWindow()
    {
        var session = new Session { Id = "s3", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violations = new[]
        {
            new GovernanceViolation { Id = "v5", SessionId = "s3", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "recent", CreatedAt = DateTime.UtcNow.AddHours(-1) },
            new GovernanceViolation { Id = "v6", SessionId = "s3", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "old", CreatedAt = DateTime.UtcNow.AddDays(-10) }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetRecentAsync(TimeSpan.FromHours(24));

        result.Should().ContainSingle().Which.Id.Should().Be("v5");
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var session = new Session { Id = "s4", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violation = new GovernanceViolation { Id = "v7", SessionId = "s4", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test", CreatedAt = DateTime.UtcNow };
        await _repo.AddRangeAsync([violation]);
        await _db.SaveChangesAsync();

        violation.Resolved = true;
        violation.ResolvedBy = "architect@test.com";
        violation.ResolutionReason = "false positive";
        violation.ResolvedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(violation);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync("v7");
        fetched.Should().NotBeNull();
        fetched!.Resolved.Should().BeTrue();
        fetched.ResolutionReason.Should().Be("false positive");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceViolationRepositoryTests" -v m`
Expected: FAIL — `GovernanceViolationRepository` class does not exist

- [ ] **Step 3: Implement GovernanceViolationRepository**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs
namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class GovernanceViolationRepository : IGovernanceViolationRepository
{
    private readonly OverwatchDbContext _db;

    public GovernanceViolationRepository(OverwatchDbContext db) => _db = db;

    public async Task<GovernanceViolation?> GetByIdAsync(string id, CancellationToken ct = default) =>
        await _db.GovernanceViolations.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default) =>
        await _db.GovernanceViolations
            .Where(v => v.SessionId == sessionId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GovernanceViolation>> GetBySessionIdAsync(string sessionId, ViolationSeverity? severity, CancellationToken ct = default)
    {
        var query = _db.GovernanceViolations.Where(v => v.SessionId == sessionId);
        if (severity.HasValue)
            query = query.Where(v => v.Severity == severity.Value);
        return await query.OrderByDescending(v => v.CreatedAt).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<GovernanceViolation>> GetRecentAsync(TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;
        return await _db.GovernanceViolations
            .Where(v => v.CreatedAt >= cutoff)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<GovernanceViolation> violations, CancellationToken ct = default) =>
        await _db.GovernanceViolations.AddRangeAsync(violations, ct);

    public Task UpdateAsync(GovernanceViolation violation, CancellationToken ct = default)
    {
        _db.GovernanceViolations.Update(violation);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceViolationRepositoryTests" -v m`
Expected: 4 passed

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Persistence/Repositories/GovernanceViolationRepository.cs tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceViolationRepositoryTests.cs
git commit -m "feat: add GovernanceViolationRepository with filtering and time-window queries"
```

---

### Task 5: Infrastructure — GovernanceEngine (TDD)

**Files:**
- Create: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceEngine.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceEngineTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceEngineTests.cs
namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public class GovernanceEngineTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IGovernanceViolationRepository _violationRepo = Substitute.For<IGovernanceViolationRepository>();
    private readonly PolicyLoader _policyLoader;
    private readonly PatternRuleEvaluator _patternEvaluator = new();
    private readonly DriftDetector _driftDetector;
    private readonly string _policiesDir;

    public GovernanceEngineTests()
    {
        _policiesDir = Path.Combine(Path.GetTempPath(), $"overwatch-engine-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_policiesDir);

        var config = Microsoft.Extensions.Options.Options.Create(
            new GovernanceConfig { PoliciesPath = _policiesDir, DriftWindowHours = 72, DriftThreshold = 3 });
        _policyLoader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);
        _driftDetector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
    }

    [Fact]
    public async Task EvaluateAsync_WithPatternMatch_CreatesViolation()
    {
        WritePolicyFile("no-sql", ViolationSeverity.Error, "SqlCommand");
        var session = CreateSessionWithDiff("svc.cs", "var cmd = new SqlCommand();");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _violationRepo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().ContainSingle();
        violations[0].RuleId.Should().Be("no-sql");
        await _violationRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<GovernanceViolation>>(v => v.Count() == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EvaluateAsync_NoPolicies_ReturnsEmpty()
    {
        var session = CreateSessionWithDiff("svc.cs", "var cmd = new SqlCommand();");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var emptyDir = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        var config = Microsoft.Extensions.Options.Options.Create(
            new GovernanceConfig { PoliciesPath = emptyDir });
        var loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);
        var detector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
        var engine = new GovernanceEngine(
            _sessionRepo, _violationRepo, loader, _patternEvaluator, detector,
            Substitute.For<IUnitOfWork>(), NullLogger<GovernanceEngine>.Instance);

        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().BeEmpty();
        Directory.Delete(emptyDir, true);
    }

    [Fact]
    public async Task EvaluateAsync_SessionNotFound_ReturnsEmpty()
    {
        _sessionRepo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((Session?)null);

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync("nonexistent");

        violations.Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_SemanticRule_SkipsWithoutError()
    {
        var yaml = """
            policies:
              - id: auth-check
                name: "Auth required"
                severity: error
                type: semantic
                scan: [diffs]
                rule: "All endpoints must have [Authorize]"
            """;
        File.WriteAllText(Path.Combine(_policiesDir, "semantic.yaml"), yaml);

        var session = CreateSessionWithDiff("ctrl.cs", "[HttpGet] public IActionResult Get()");
        _sessionRepo.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _violationRepo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var engine = CreateEngine();
        var violations = await engine.EvaluateAsync(session.Id);

        violations.Should().BeEmpty();
    }

    private GovernanceEngine CreateEngine() => new(
        _sessionRepo, _violationRepo, _policyLoader, _patternEvaluator, _driftDetector,
        Substitute.For<IUnitOfWork>(), NullLogger<GovernanceEngine>.Instance);

    private void WritePolicyFile(string ruleId, ViolationSeverity severity, string pattern)
    {
        var yaml = $"""
            policies:
              - id: {ruleId}
                name: "Test: {ruleId}"
                severity: {severity.ToString().ToLower()}
                type: pattern
                scan: [diffs]
                patterns:
                  - "{pattern}"
            """;
        File.WriteAllText(Path.Combine(_policiesDir, $"{ruleId}.yaml"), yaml);
    }

    private static Session CreateSessionWithDiff(string filePath, string diffText)
    {
        var session = new Session { Id = Guid.NewGuid().ToString(), DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        session.FileChanges.Add(new FileChange
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = session.Id,
            FilePath = filePath,
            DiffText = diffText
        });
        return session;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceEngineTests" -v m`
Expected: FAIL — `GovernanceEngine` and `DriftDetector` classes do not exist

- [ ] **Step 3: Create DriftDetector stub** (full implementation in Task 9, but needed for GovernanceEngine to compile)

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs
namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class DriftDetector
{
    private readonly GovernanceConfig _config;
    private readonly ILogger<DriftDetector> _logger;

    public DriftDetector(IOptions<GovernanceConfig> config, ILogger<DriftDetector> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> DetectAsync(
        string sessionId,
        IReadOnlyList<GovernanceViolation> currentViolations,
        IGovernanceViolationRepository repository,
        CancellationToken ct = default)
    {
        // Stub — full implementation in Task 9
        return [];
    }
}
```

- [ ] **Step 4: Implement GovernanceEngine**

```csharp
// src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceEngine.cs
namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

public sealed class GovernanceEngine : IGovernanceEngine
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IGovernanceViolationRepository _violationRepo;
    private readonly PolicyLoader _policyLoader;
    private readonly PatternRuleEvaluator _patternEvaluator;
    private readonly DriftDetector _driftDetector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GovernanceEngine> _logger;

    public GovernanceEngine(
        ISessionRepository sessionRepo,
        IGovernanceViolationRepository violationRepo,
        PolicyLoader policyLoader,
        PatternRuleEvaluator patternEvaluator,
        DriftDetector driftDetector,
        IUnitOfWork unitOfWork,
        ILogger<GovernanceEngine> logger)
    {
        _sessionRepo = sessionRepo;
        _violationRepo = violationRepo;
        _policyLoader = policyLoader;
        _patternEvaluator = patternEvaluator;
        _driftDetector = driftDetector;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId, ct);
        if (session is null)
        {
            _logger.LogWarning("Governance: session {SessionId} not found", sessionId);
            return [];
        }

        var policies = _policyLoader.LoadPolicies();
        if (policies.Count == 0)
        {
            _logger.LogDebug("No governance policies loaded, skipping evaluation");
            return [];
        }

        var allViolations = new List<GovernanceViolation>();

        foreach (var policy in policies)
        {
            switch (policy.Type)
            {
                case PolicyType.Pattern:
                    var violations = _patternEvaluator.Evaluate(policy, session);
                    allViolations.AddRange(violations);
                    break;

                case PolicyType.Semantic:
                    _logger.LogDebug("Semantic rule '{RuleId}' skipped — LLM integration not configured", policy.Id);
                    break;
            }
        }

        var driftViolations = await _driftDetector.DetectAsync(sessionId, allViolations, _violationRepo, ct);
        allViolations.AddRange(driftViolations);

        if (allViolations.Count > 0)
        {
            await _violationRepo.AddRangeAsync(allViolations, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Governance: {Count} violations found for session {SessionId}",
                allViolations.Count, sessionId);
        }

        return allViolations;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceEngineTests" -v m`
Expected: 4 passed

- [ ] **Step 6: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/GovernanceEngine.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs tests/DeveloperOverwatch.Application.Tests/Governance/GovernanceEngineTests.cs
git commit -m "feat: add GovernanceEngine orchestrating policy evaluation with DriftDetector stub"
```

---

### Task 6: Application — GovernanceBehavior (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Behaviors/GovernanceBehavior.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Behaviors/GovernanceBehaviorTests.cs`
- Modify: `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Behaviors/GovernanceBehaviorTests.cs
namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public class GovernanceBehaviorTests
{
    private readonly IGovernanceEngine _engine = Substitute.For<IGovernanceEngine>();
    private readonly GovernanceBehavior _behavior;

    public GovernanceBehaviorTests()
    {
        _behavior = new GovernanceBehavior(_engine, NullLogger<GovernanceBehavior>.Instance);
    }

    [Fact]
    public async Task Handle_SuccessfulSessionEnd_TriggersGovernance()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>
            {
                new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
            });

        RequestHandlerDelegate<Result<string>> next = () =>
            Task.FromResult(Result<string>.Success("s1"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _engine.Received(1).EvaluateAsync("s1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedSessionEnd_SkipsGovernance()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };

        RequestHandlerDelegate<Result<string>> next = () =>
            Task.FromResult(Result<string>.Fail("Session not found"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await _engine.DidNotReceive().EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GovernanceThrows_DoesNotFailSessionEnd()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Policy loading failed"));

        RequestHandlerDelegate<Result<string>> next = () =>
            Task.FromResult(Result<string>.Success("s1"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceBehaviorTests" -v m`
Expected: FAIL — `GovernanceBehavior` class does not exist

- [ ] **Step 3: Implement GovernanceBehavior**

```csharp
// src/Application/DeveloperOverwatch.Application/Behaviors/GovernanceBehavior.cs
namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class GovernanceBehavior
    : IPipelineBehavior<IngestSessionEndCommand, Result<string>>
{
    private readonly IGovernanceEngine _engine;
    private readonly ILogger<GovernanceBehavior> _logger;

    public GovernanceBehavior(IGovernanceEngine engine, ILogger<GovernanceBehavior> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task<Result<string>> Handle(
        IngestSessionEndCommand request,
        RequestHandlerDelegate<Result<string>> next,
        CancellationToken cancellationToken)
    {
        var result = await next();

        if (!result.IsSuccess)
            return result;

        try
        {
            var violations = await _engine.EvaluateAsync(request.SessionId, cancellationToken);
            if (violations.Count > 0)
                _logger.LogInformation("Governance: {Count} violations for session {SessionId}",
                    violations.Count, request.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Governance evaluation failed for session {SessionId} — non-blocking",
                request.SessionId);
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GovernanceBehaviorTests" -v m`
Expected: 3 passed

- [ ] **Step 5: Register GovernanceBehavior in Application DI**

In `src/Application/DeveloperOverwatch.Application/DependencyInjection.cs`, add after the existing `LoggingBehavior` registration:

```csharp
services.AddTransient<IPipelineBehavior<Commands.IngestSessionEnd.IngestSessionEndCommand, Domain.Common.Result<string>>, GovernanceBehavior>();
```

The full method should read:

```csharp
public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
{
    var assembly = Assembly.GetExecutingAssembly();

    services.AddValidatorsFromAssembly(assembly);
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
    services.AddTransient<IPipelineBehavior<Commands.IngestSessionEnd.IngestSessionEndCommand, Domain.Common.Result<string>>, GovernanceBehavior>();

    services.AddScoped<Services.SessionCommitCorrelator>();

    return services;
}
```

- [ ] **Step 6: Verify build**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Behaviors/GovernanceBehavior.cs src/Application/DeveloperOverwatch.Application/DependencyInjection.cs tests/DeveloperOverwatch.Application.Tests/Behaviors/GovernanceBehaviorTests.cs
git commit -m "feat: add GovernanceBehavior pipeline that triggers on session end"
```

---

### Task 7: Application — GetViolationsQuery (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQuery.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQueryHandler.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Queries/GetViolationsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Queries/GetViolationsTests.cs
namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetViolations;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetViolationsTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();

    [Fact]
    public async Task Handle_ReturnsViolationsForSession()
    {
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
        };
        _repo.GetBySessionIdAsync("s1", (ViolationSeverity?)null, Arg.Any<CancellationToken>())
            .Returns(violations);

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithSeverityFilter_FiltersCorrectly()
    {
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v2", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Critical, Message = "critical" }
        };
        _repo.GetBySessionIdAsync("s1", ViolationSeverity.Critical, Arg.Any<CancellationToken>())
            .Returns(violations);

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1", Severity = ViolationSeverity.Critical },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Severity.Should().Be(ViolationSeverity.Critical);
    }

    [Fact]
    public async Task Handle_NoViolations_ReturnsEmptyList()
    {
        _repo.GetBySessionIdAsync("s1", (ViolationSeverity?)null, Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GetViolationsTests" -v m`
Expected: FAIL — `GetViolationsQuery` does not exist

- [ ] **Step 3: Implement GetViolationsQuery and handler**

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQuery.cs
namespace DeveloperOverwatch.Application.Queries.GetViolations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record GetViolationsQuery : IRequest<Result<IReadOnlyList<GovernanceViolation>>>
{
    public required string SessionId { get; init; }
    public ViolationSeverity? Severity { get; init; }
}
```

```csharp
// src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQueryHandler.cs
namespace DeveloperOverwatch.Application.Queries.GetViolations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetViolationsQueryHandler
    : IRequestHandler<GetViolationsQuery, Result<IReadOnlyList<GovernanceViolation>>>
{
    private readonly IGovernanceViolationRepository _repository;

    public GetViolationsQueryHandler(IGovernanceViolationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<GovernanceViolation>>> Handle(
        GetViolationsQuery request, CancellationToken cancellationToken)
    {
        var violations = await _repository.GetBySessionIdAsync(
            request.SessionId, request.Severity, cancellationToken);
        return Result<IReadOnlyList<GovernanceViolation>>.Success(violations);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "GetViolationsTests" -v m`
Expected: 3 passed

- [ ] **Step 5: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQuery.cs src/Application/DeveloperOverwatch.Application/Queries/GetViolations/GetViolationsQueryHandler.cs tests/DeveloperOverwatch.Application.Tests/Queries/GetViolationsTests.cs
git commit -m "feat: add GetViolationsQuery with optional severity filter"
```

---

### Task 8: Application — ResolveViolationCommand (TDD)

**Files:**
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommand.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandHandler.cs`
- Create: `src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandValidator.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Commands/ResolveViolationTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Commands/ResolveViolationTests.cs
namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ResolveViolation;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ResolveViolationTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ExistingViolation_ResolvesIt()
    {
        var violation = new GovernanceViolation
        {
            Id = "v1", SessionId = "s1", RuleId = "r1",
            Severity = ViolationSeverity.Error, Message = "test"
        };
        _repo.GetByIdAsync("v1", Arg.Any<CancellationToken>()).Returns(violation);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "v1",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "false positive"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        violation.Resolved.Should().BeTrue();
        violation.ResolvedBy.Should().Be("architect@test.com");
        violation.ResolutionReason.Should().Be("false positive");
        violation.ResolvedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ViolationNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((GovernanceViolation?)null);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "nonexistent",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "n/a"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AlreadyResolved_ReturnsFail()
    {
        var violation = new GovernanceViolation
        {
            Id = "v2", SessionId = "s1", RuleId = "r1",
            Severity = ViolationSeverity.Error, Message = "test",
            Resolved = true, ResolvedBy = "someone@test.com"
        };
        _repo.GetByIdAsync("v2", Arg.Any<CancellationToken>()).Returns(violation);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "v2",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "duplicate"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already resolved");
    }

    [Fact]
    public void Validator_MissingFields_ReturnsErrors()
    {
        var validator = new ResolveViolationCommandValidator();
        var command = new ResolveViolationCommand
        {
            ViolationId = "",
            ResolvedBy = "",
            ResolutionReason = ""
        };

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "ResolveViolationTests" -v m`
Expected: FAIL — classes do not exist

- [ ] **Step 3: Implement ResolveViolationCommand**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommand.cs
namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using DeveloperOverwatch.Domain.Common;
using MediatR;

public sealed record ResolveViolationCommand : IRequest<Result<string>>
{
    public required string ViolationId { get; init; }
    public required string ResolvedBy { get; init; }
    public required string ResolutionReason { get; init; }
}
```

- [ ] **Step 4: Implement ResolveViolationCommandHandler**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandHandler.cs
namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class ResolveViolationCommandHandler
    : IRequestHandler<ResolveViolationCommand, Result<string>>
{
    private readonly IGovernanceViolationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ResolveViolationCommandHandler(IGovernanceViolationRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(ResolveViolationCommand request, CancellationToken cancellationToken)
    {
        var violation = await _repository.GetByIdAsync(request.ViolationId, cancellationToken);
        if (violation is null)
            return Result<string>.Fail($"Violation {request.ViolationId} not found");

        if (violation.Resolved)
            return Result<string>.Fail($"Violation {request.ViolationId} is already resolved");

        violation.Resolved = true;
        violation.ResolvedBy = request.ResolvedBy;
        violation.ResolutionReason = request.ResolutionReason;
        violation.ResolvedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(violation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(violation.Id);
    }
}
```

- [ ] **Step 5: Implement ResolveViolationCommandValidator**

```csharp
// src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandValidator.cs
namespace DeveloperOverwatch.Application.Commands.ResolveViolation;

using FluentValidation;

public sealed class ResolveViolationCommandValidator : AbstractValidator<ResolveViolationCommand>
{
    public ResolveViolationCommandValidator()
    {
        RuleFor(x => x.ViolationId).NotEmpty();
        RuleFor(x => x.ResolvedBy).NotEmpty().EmailAddress();
        RuleFor(x => x.ResolutionReason).NotEmpty().MaximumLength(1000);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "ResolveViolationTests" -v m`
Expected: 4 passed

- [ ] **Step 7: Commit**

```bash
git add src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommand.cs src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandHandler.cs src/Application/DeveloperOverwatch.Application/Commands/ResolveViolation/ResolveViolationCommandValidator.cs tests/DeveloperOverwatch.Application.Tests/Commands/ResolveViolationTests.cs
git commit -m "feat: add ResolveViolationCommand with validation"
```

---

### Task 9: Infrastructure — DriftDetector (TDD)

**Files:**
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs`
- Create: `tests/DeveloperOverwatch.Application.Tests/Governance/DriftDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/DeveloperOverwatch.Application.Tests/Governance/DriftDetectorTests.cs
namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

public class DriftDetectorTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();
    private readonly DriftDetector _detector;

    public DriftDetectorTests()
    {
        var config = Options.Create(new GovernanceConfig { DriftWindowHours = 72, DriftThreshold = 3 });
        _detector = new DriftDetector(config, NullLogger<DriftDetector>.Instance);
    }

    [Fact]
    public async Task DetectAsync_RepeatedRule_CreatesDriftViolation()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", CreatedAt = DateTime.UtcNow.AddHours(-2) },
            new() { Id = "v2", SessionId = "s2", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old2", CreatedAt = DateTime.UtcNow.AddHours(-5) },
            new() { Id = "v3", SessionId = "s3", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old3", CreatedAt = DateTime.UtcNow.AddHours(-10) }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().ContainSingle();
        driftViolations[0].RuleId.Should().Be("drift:no-sql");
        driftViolations[0].Severity.Should().Be(ViolationSeverity.Warning);
        driftViolations[0].Message.Should().Contain("no-sql");
        driftViolations[0].Message.Should().Contain("4"); // 3 historical + 1 current
    }

    [Fact]
    public async Task DetectAsync_BelowThreshold_ReturnsEmpty()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", CreatedAt = DateTime.UtcNow.AddHours(-2) }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_ResolvedViolations_NotCounted()
    {
        var currentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v-new", SessionId = "s5", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "new" }
        };

        var recentViolations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old1", Resolved = true },
            new() { Id = "v2", SessionId = "s2", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old2", Resolved = true },
            new() { Id = "v3", SessionId = "s3", RuleId = "no-sql", Severity = ViolationSeverity.Error, Message = "old3", Resolved = true }
        };

        _repo.GetRecentAsync(TimeSpan.FromHours(72), Arg.Any<CancellationToken>()).Returns(recentViolations);

        var driftViolations = await _detector.DetectAsync("s5", currentViolations, _repo);

        driftViolations.Should().BeEmpty();
    }

    [Fact]
    public async Task DetectAsync_NoCurrentViolations_ReturnsEmpty()
    {
        _repo.GetRecentAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var driftViolations = await _detector.DetectAsync("s5", [], _repo);

        driftViolations.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "DriftDetectorTests" -v m`
Expected: FAIL — DetectAsync returns empty (stub), first test fails

- [ ] **Step 3: Implement DriftDetector**

Replace the stub in `src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs`:

```csharp
namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class DriftDetector
{
    private readonly GovernanceConfig _config;
    private readonly ILogger<DriftDetector> _logger;

    public DriftDetector(IOptions<GovernanceConfig> config, ILogger<DriftDetector> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> DetectAsync(
        string sessionId,
        IReadOnlyList<GovernanceViolation> currentViolations,
        IGovernanceViolationRepository repository,
        CancellationToken ct = default)
    {
        if (currentViolations.Count == 0)
            return [];

        var window = TimeSpan.FromHours(_config.DriftWindowHours);
        var recentViolations = await repository.GetRecentAsync(window, ct);

        var driftViolations = new List<GovernanceViolation>();
        var checkedRules = new HashSet<string>();

        foreach (var violation in currentViolations)
        {
            if (!checkedRules.Add(violation.RuleId))
                continue;

            var unresolvedCount = recentViolations
                .Count(v => v.RuleId == violation.RuleId && !v.Resolved);

            var totalCount = unresolvedCount + currentViolations.Count(v => v.RuleId == violation.RuleId);

            if (totalCount >= _config.DriftThreshold)
            {
                _logger.LogWarning("Drift detected: rule '{RuleId}' violated {Count} times in {Window}h",
                    violation.RuleId, totalCount, _config.DriftWindowHours);

                driftViolations.Add(new GovernanceViolation
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    RuleId = $"drift:{violation.RuleId}",
                    Severity = ViolationSeverity.Warning,
                    Message = $"Drift detected: rule '{violation.RuleId}' violated {totalCount} times in the last {_config.DriftWindowHours}h",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return driftViolations;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/DeveloperOverwatch.Application.Tests --filter "DriftDetectorTests" -v m`
Expected: 4 passed

- [ ] **Step 5: Commit**

```bash
git add src/Infrastructure/DeveloperOverwatch.Infrastructure/Governance/DriftDetector.cs tests/DeveloperOverwatch.Application.Tests/Governance/DriftDetectorTests.cs
git commit -m "feat: implement DriftDetector with rolling window and threshold detection"
```

---

### Task 10: API — GovernanceController & DI Wiring

**Files:**
- Create: `src/API/DeveloperOverwatch.API/Controllers/GovernanceController.cs`
- Modify: `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`
- Create: `tests/DeveloperOverwatch.API.Tests/Controllers/GovernanceControllerTests.cs`

- [ ] **Step 1: Wire up governance services in Infrastructure DI**

Update `src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs`. Add the following registrations inside `AddInfrastructureDependencies` after the existing service registrations:

```csharp
services.Configure<Governance.GovernanceConfig>(configuration.GetSection("Governance"));
services.AddScoped<IGovernanceViolationRepository, GovernanceViolationRepository>();
services.AddSingleton<Governance.PolicyLoader>();
services.AddSingleton<Governance.PatternRuleEvaluator>();
services.AddSingleton<Governance.DriftDetector>();
services.AddScoped<IGovernanceEngine, Governance.GovernanceEngine>();
```

The full method:

```csharp
public static IServiceCollection AddInfrastructureDependencies(
    this IServiceCollection services,
    string connectionString,
    IConfiguration configuration)
{
    services.AddDbContext<OverwatchDbContext>(options =>
        options.UseSqlite(connectionString));

    services.AddScoped<ISessionRepository, SessionRepository>();
    services.AddScoped<ICommitSessionLinkRepository, CommitSessionLinkRepository>();
    services.AddScoped<IPullRequestReviewRepository, PullRequestReviewRepository>();
    services.AddScoped<IGovernanceViolationRepository, GovernanceViolationRepository>();
    services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

    services.Configure<Services.GitHubConfig>(configuration.GetSection("GitHub"));
    services.AddHttpClient<IGitHostingService, Services.GitHubService>();

    services.Configure<Governance.GovernanceConfig>(configuration.GetSection("Governance"));
    services.AddSingleton<Governance.PolicyLoader>();
    services.AddSingleton<Governance.PatternRuleEvaluator>();
    services.AddSingleton<Governance.DriftDetector>();
    services.AddScoped<IGovernanceEngine, Governance.GovernanceEngine>();

    return services;
}
```

- [ ] **Step 2: Create GovernanceController**

```csharp
// src/API/DeveloperOverwatch.API/Controllers/GovernanceController.cs
namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Commands.ResolveViolation;
using DeveloperOverwatch.Application.Queries.GetViolations;
using DeveloperOverwatch.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/governance")]
public class GovernanceController : ControllerBase
{
    private readonly IMediator _mediator;

    public GovernanceController(IMediator mediator) => _mediator = mediator;

    [HttpGet("violations/{sessionId}")]
    public async Task<IActionResult> GetViolations(
        string sessionId,
        [FromQuery] ViolationSeverity? severity = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new GetViolationsQuery { SessionId = sessionId, Severity = severity }, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("violations/{violationId}/resolve")]
    public async Task<IActionResult> ResolveViolation(
        string violationId,
        [FromBody] ResolveViolationRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResolveViolationCommand
        {
            ViolationId = violationId,
            ResolvedBy = request.ResolvedBy,
            ResolutionReason = request.ResolutionReason
        }, ct);
        return result.IsSuccess ? Ok(new { violationId = result.Value }) : BadRequest(result.Error);
    }

    public sealed record ResolveViolationRequest(string ResolvedBy, string ResolutionReason);
}
```

- [ ] **Step 3: Verify build compiles**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Write integration tests**

```csharp
// tests/DeveloperOverwatch.API.Tests/Controllers/GovernanceControllerTests.cs
namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class GovernanceControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly OverwatchWebApplicationFactory _factory;

    public GovernanceControllerTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetViolations_ReturnsViolationsForSession()
    {
        var sessionId = Guid.NewGuid().ToString();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = sessionId, DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow });
            db.GovernanceViolations.Add(new GovernanceViolation
            {
                Id = Guid.NewGuid().ToString(),
                SessionId = sessionId,
                RuleId = "no-sql",
                Severity = ViolationSeverity.Error,
                Message = "SqlCommand found",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/governance/violations/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var violations = await response.Content.ReadFromJsonAsync<GovernanceViolation[]>();
        violations.Should().ContainSingle().Which.RuleId.Should().Be("no-sql");
    }

    [Fact]
    public async Task GetViolations_WithSeverityFilter_FiltersCorrectly()
    {
        var sessionId = Guid.NewGuid().ToString();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = sessionId, DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow });
            db.GovernanceViolations.AddRange(
                new GovernanceViolation { Id = "v-err", SessionId = sessionId, RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow },
                new GovernanceViolation { Id = "v-info", SessionId = sessionId, RuleId = "r2", Severity = ViolationSeverity.Info, Message = "info", CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/governance/violations/{sessionId}?severity=Error");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var violations = await response.Content.ReadFromJsonAsync<GovernanceViolation[]>();
        violations.Should().ContainSingle().Which.Severity.Should().Be(ViolationSeverity.Error);
    }

    [Fact]
    public async Task ResolveViolation_ExistingViolation_ResolvesIt()
    {
        var sessionId = Guid.NewGuid().ToString();
        var violationId = Guid.NewGuid().ToString();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Sessions.Add(new Session { Id = sessionId, DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow });
            db.GovernanceViolations.Add(new GovernanceViolation
            {
                Id = violationId,
                SessionId = sessionId,
                RuleId = "no-sql",
                Severity = ViolationSeverity.Error,
                Message = "test",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/governance/violations/{violationId}/resolve",
            new { ResolvedBy = "architect@test.com", ResolutionReason = "false positive" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResolveViolation_NotFound_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/governance/violations/nonexistent/resolve",
            new { ResolvedBy = "architect@test.com", ResolutionReason = "n/a" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test -v m`
Expected: All tests pass (previous 38 + new governance tests)

- [ ] **Step 6: Commit**

```bash
git add src/API/DeveloperOverwatch.API/Controllers/GovernanceController.cs src/Infrastructure/DeveloperOverwatch.Infrastructure/DependencyInjection.cs tests/DeveloperOverwatch.API.Tests/Controllers/GovernanceControllerTests.cs
git commit -m "feat: add GovernanceController with violations query and resolve endpoints"
```

---

### Task 11: Sample Policies & Final Verification

**Files:**
- Create: `policies/default.yaml`

- [ ] **Step 1: Create sample policy file**

```yaml
# policies/default.yaml
policies:
  - id: no-raw-sql
    name: "No raw SQL outside repositories"
    severity: error
    type: pattern
    scan: [diffs]
    patterns:
      - "SqlCommand|SqlConnection"
      - "ExecuteSqlRaw|FromSqlRaw"
    exclude_paths:
      - "**/Repositories/**"
      - "**/Migrations/**"

  - id: no-hardcoded-secrets
    name: "No hardcoded secrets"
    severity: critical
    type: pattern
    scan: [diffs]
    patterns:
      - "password\\s*=\\s*[\"'][^\"']+[\"']"
      - "api[_-]?key\\s*=\\s*[\"'][^\"']+[\"']"
      - "connectionString.*=.*[\"']Server="

  - id: no-console-log
    name: "No console.log or Console.WriteLine in production code"
    severity: warning
    type: pattern
    scan: [diffs]
    patterns:
      - "console\\.log\\("
      - "Console\\.Write(Line)?\\("
    exclude_paths:
      - "**/Tests/**"
      - "**/test/**"

  - id: auth-middleware-required
    name: "API endpoints must have auth"
    severity: error
    type: semantic
    scan: [diffs]
    rule: >
      Any new controller action or minimal API endpoint
      must have [Authorize] or RequireAuthorization()

  - id: test-coverage-required
    name: "New public methods need tests"
    severity: info
    type: semantic
    scan: [session]
    rule: >
      If session created new public methods, check if
      corresponding test files were also created or modified
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test -v m`
Expected: All tests pass

- [ ] **Step 3: Verify build is clean**

Run: `dotnet build --no-restore`
Expected: 0 errors, 0 warnings

- [ ] **Step 4: Commit**

```bash
git add policies/default.yaml
git commit -m "feat: add default governance policies for SQL, secrets, console output"
```

- [ ] **Step 5: Run final count verification**

Run: `dotnet test --list-tests | grep -c "test$"` or `dotnet test -v m` and count total
Expected: ~60+ tests total (38 existing + ~26 new governance tests)
