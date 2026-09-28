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

    [Fact]
    public void LoadPolicies_RelativePath_ResolvesAgainstRunningAssemblyNotCwd()
    {
        // A relative PoliciesPath must resolve against AppContext.BaseDirectory (where the app's
        // own files live) — not Directory.GetCurrentDirectory(), which depends on how/where the
        // process was launched and silently produced zero policies in every real deployment.
        var relativeDirName = $"overwatch-relative-policies-{Guid.NewGuid():N}";
        var absoluteDir = Path.Combine(AppContext.BaseDirectory, relativeDirName);
        Directory.CreateDirectory(absoluteDir);
        try
        {
            File.WriteAllText(Path.Combine(absoluteDir, "test.yaml"), """
                policies:
                  - id: relative-rule
                    name: "Relative rule"
                    severity: info
                    type: pattern
                    scan: [diffs]
                    patterns: ["pattern1"]
                """);

            var config = Options.Create(new GovernanceConfig { PoliciesPath = relativeDirName });
            var loader = new PolicyLoader(config, NullLogger<PolicyLoader>.Instance);

            var policies = loader.LoadPolicies();

            policies.Should().ContainSingle().Which.Id.Should().Be("relative-rule");
        }
        finally
        {
            Directory.Delete(absoluteDir, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
