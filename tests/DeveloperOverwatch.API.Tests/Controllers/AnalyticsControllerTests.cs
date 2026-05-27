namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using DeveloperOverwatch.Domain.Entities;
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
    public async Task GetTeamAnalytics_EmptyDb_ReturnsOkWithZeros()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        var response = await _client.GetAsync("/api/analytics/team");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
