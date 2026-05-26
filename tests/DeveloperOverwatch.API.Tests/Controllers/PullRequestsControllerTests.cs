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
    public async Task GetPullRequests_EmptyDatabase_ReturnsEmptyArray()
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
