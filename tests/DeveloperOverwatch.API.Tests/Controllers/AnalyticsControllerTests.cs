namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Text.Json;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class AnalyticsControllerTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly OverwatchWebApplicationFactory _factory;

    public AnalyticsControllerTests(OverwatchWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
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

    // Startup seeds the team daily budget from CostAlerts:DailyThresholdUsd (50 in appsettings.json).
    [Fact]
    public async Task GetTeamAnalytics_SeededTeamDailyBudget_IsTheThreshold()
    {
        var threshold = await ReadThresholdAsync(_client);

        threshold.GetDecimal().Should().Be(50m);
    }

    // A fresh factory, not WithWebHostBuilder on the shared fixture: that would reuse the fixture's
    // in-memory database, already seeded at 50.
    [Fact]
    public async Task GetTeamAnalytics_NoTeamDailyBudget_WritesThresholdAsExplicitNull()
    {
        using var unseeded = new OverwatchWebApplicationFactory();
        using var factory = unseeded.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["CostAlerts:DailyThresholdUsd"] = "0" })));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);

        var threshold = await ReadThresholdAsync(client);

        threshold.ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<JsonElement> ReadThresholdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/analytics/team");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.TryGetProperty("dailyCostThresholdUsd", out var threshold)
            .Should().BeTrue("the threshold is always written, null included");
        return threshold.Clone();
    }
}
