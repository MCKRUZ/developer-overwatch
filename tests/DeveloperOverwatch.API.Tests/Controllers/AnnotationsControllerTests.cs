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
