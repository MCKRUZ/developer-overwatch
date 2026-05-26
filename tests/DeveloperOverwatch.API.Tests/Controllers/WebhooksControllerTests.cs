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
                // Remove all IGitHostingService-related registrations (AddHttpClient registers several)
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IGitHostingService))
                    .ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                var stub = Substitute.For<IGitHostingService>();
                stub.GetCommitFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new List<string>());
                stub.GetPullRequestCommitsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(new List<CommitInfo>());
                stub.GetPullRequestAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns((PullRequestInfo?)null);
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
