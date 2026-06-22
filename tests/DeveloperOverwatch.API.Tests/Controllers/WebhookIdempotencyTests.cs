namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net.Http;
using System.Text;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

/// <summary>
/// End-to-end proof that the IdempotencyBehavior actually dedupes GitHub webhook retries
/// through the real HTTP → controller → MediatR pipeline, keyed on the X-GitHub-Delivery id.
/// The unit tests cover the behavior in isolation; this covers the wiring.
/// </summary>
public class WebhookIdempotencyTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly OverwatchWebApplicationFactory _factory;
    private readonly ICommitSessionLinkRepository _linkRepo;
    private readonly HttpClient _client;

    public WebhookIdempotencyTests(OverwatchWebApplicationFactory factory)
    {
        // A commit carrying a Claude-Session trailer yields exactly one link per handler
        // execution (pure parsing, no DB), so AddRangeAsync call-count == handler execution count.
        _linkRepo = Substitute.For<ICommitSessionLinkRepository>();
        _linkRepo.AddRangeAsync(Arg.Any<IEnumerable<CommitSessionLink>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Empty commit files make the correlator short-circuit, leaving only the trailer link.
        var gitStub = Substitute.For<IGitHostingService>();
        gitStub.GetCommitFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _factory = factory;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                Replace<ICommitSessionLinkRepository>(services, _linkRepo);
                Replace<IGitHostingService>(services, gitStub);
            });
        }).CreateClient();
    }

    private static void Replace<TService>(IServiceCollection services, object instance)
        where TService : class
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(TService)).ToList())
            services.Remove(d);
        services.AddSingleton(typeof(TService), instance);
    }

    private const string PushPayload = """
    {
        "repository": { "full_name": "owner/repo" },
        "commits": [
            {
                "id": "abc123",
                "message": "feat: work\n\nClaude-Session: sess-1",
                "timestamp": "2026-05-26T12:00:00Z"
            }
        ]
    }
    """;

    private Task<HttpResponseMessage> PostPushAsync(string deliveryId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(PushPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-GitHub-Event", "push");
        request.Headers.Add("X-GitHub-Delivery", deliveryId);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task DuplicateDelivery_ProcessesHandlerOnce()
    {
        var first = await PostPushAsync("delivery-dup");
        var second = await PostPushAsync("delivery-dup");

        first.IsSuccessStatusCode.Should().BeTrue();
        second.IsSuccessStatusCode.Should().BeTrue();
        await _linkRepo.Received(1).AddRangeAsync(
            Arg.Any<IEnumerable<CommitSessionLink>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DistinctDeliveries_ProcessHandlerEachTime()
    {
        await PostPushAsync("delivery-A");
        await PostPushAsync("delivery-B");

        await _linkRepo.Received(2).AddRangeAsync(
            Arg.Any<IEnumerable<CommitSessionLink>>(), Arg.Any<CancellationToken>());
    }
}
