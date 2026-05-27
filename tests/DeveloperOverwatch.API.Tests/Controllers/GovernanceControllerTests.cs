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
        _client = factory.CreateAuthenticatedClient();
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
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("no-sql");
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
                new GovernanceViolation { Id = Guid.NewGuid().ToString(), SessionId = sessionId, RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow },
                new GovernanceViolation { Id = Guid.NewGuid().ToString(), SessionId = sessionId, RuleId = "r2", Severity = ViolationSeverity.Info, Message = "info", CreatedAt = DateTime.UtcNow }
            );
            await db.SaveChangesAsync();
        }

        var response = await _client.GetAsync($"/api/governance/violations/{sessionId}?severity=Error");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("err");
        body.Should().NotContain("\"info\"");
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
    public async Task ResolveViolation_NotFound_ReturnsNotFound()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/governance/violations/nonexistent/resolve",
            new { ResolvedBy = "architect@test.com", ResolutionReason = "n/a" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
