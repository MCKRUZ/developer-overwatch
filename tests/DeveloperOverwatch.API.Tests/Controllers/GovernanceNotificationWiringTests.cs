namespace DeveloperOverwatch.API.Tests.Controllers;

using System.Net;
using System.Net.Http.Json;
using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

/// <summary>
/// End-to-end proof that a governance violation raised on session end reaches every
/// notification channel (Slack/email and the SignalR dashboard) through the real DI wiring.
/// Each channel is unit-tested in isolation; this covers how they are registered together.
/// </summary>
public class GovernanceNotificationWiringTests : IClassFixture<OverwatchWebApplicationFactory>
{
    private readonly INotificationService _slackAndEmail = Substitute.For<INotificationService>();
    private readonly IOverwatchHubClient _groupClient = Substitute.For<IOverwatchHubClient>();
    private readonly IOverwatchHubClient _allClient = Substitute.For<IOverwatchHubClient>();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GovernanceNotificationWiringTests(OverwatchWebApplicationFactory factory)
    {
        var engine = Substitute.For<IGovernanceEngine>();
        engine.EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new List<GovernanceViolation>
            {
                new()
                {
                    Id = "v1",
                    SessionId = call.Arg<string>(),
                    RuleId = "no-secrets",
                    Severity = ViolationSeverity.Critical,
                    Message = "Hardcoded secret",
                },
            });

        var hubClients = Substitute.For<IHubClients<IOverwatchHubClient>>();
        hubClients.Group(Arg.Any<string>()).Returns(_groupClient);
        hubClients.All.Returns(_allClient);
        var hub = Substitute.For<IHubContext<OverwatchHub, IOverwatchHubClient>>();
        hub.Clients.Returns(hubClients);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                Replace<IGovernanceEngine>(services, engine);
                Replace<INotificationService>(services, _slackAndEmail);
                Replace<IHubContext<OverwatchHub, IOverwatchHubClient>>(services, hub);
            });
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", OverwatchWebApplicationFactory.TestApiKey);
    }

    private static void Replace<TService>(IServiceCollection services, object instance)
        where TService : class
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(TService)).ToList())
            services.Remove(d);
        services.AddSingleton(typeof(TService), instance);
    }

    private async Task<string> StartAndEndSessionAsync()
    {
        var sessionId = Guid.NewGuid().ToString();

        var start = await _client.PostAsJsonAsync("/api/sessions/start", new
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            DeveloperName = "Dev",
            RepoPath = "/repo",
            StartedAt = DateTime.UtcNow,
        });
        start.StatusCode.Should().Be(HttpStatusCode.OK);

        var end = await _client.PostAsJsonAsync($"/api/sessions/{sessionId}/end", new
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow,
        });
        end.StatusCode.Should().Be(HttpStatusCode.OK);

        return sessionId;
    }

    [Fact]
    public async Task SessionEndWithViolation_SendsSlackAndEmailAlert()
    {
        var sessionId = await StartAndEndSessionAsync();

        await _slackAndEmail.Received(1).NotifyViolationAsync(
            "no-secrets", "Critical", "Hardcoded secret", sessionId, "dev@team.dev",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionEndWithViolation_PushesViolationToDashboard()
    {
        var sessionId = await StartAndEndSessionAsync();

        var expected = new ViolationDto("v1", sessionId, "no-secrets", "Critical", "Hardcoded secret");
        await _groupClient.Received(1).ViolationCreated(Arg.Is<ViolationDto>(d => d == expected));
        await _allClient.Received(1).ViolationCreated(Arg.Is<ViolationDto>(d => d == expected));
    }

    [Fact]
    public async Task ResolvedNotifier_ReachesEveryChannel()
    {
        // Guards any future consumer that injects a single IOverwatchNotifier: it must
        // fan out to all channels, not silently get whichever one was registered last.
        using var scope = _factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<IOverwatchNotifier>();
        var violation = new GovernanceViolation
        {
            Id = "v9", SessionId = "s9", RuleId = "r", Severity = ViolationSeverity.Warning, Message = "m",
        };

        await notifier.ViolationCreatedAsync(violation);

        await _slackAndEmail.Received(1).NotifyViolationAsync(
            "r", "Warning", "m", "s9", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _allClient.Received(1).ViolationCreated(Arg.Any<ViolationDto>());
    }

    [Fact]
    public async Task SessionEndWithViolation_DashboardFailure_StillSendsSlackAndEmailAlert()
    {
        _allClient.ViolationCreated(Arg.Any<ViolationDto>())
            .Returns(Task.FromException(new InvalidOperationException("hub down")));

        var sessionId = await StartAndEndSessionAsync();

        await _slackAndEmail.Received(1).NotifyViolationAsync(
            "no-secrets", "Critical", "Hardcoded secret", sessionId, "dev@team.dev",
            Arg.Any<CancellationToken>());
    }
}
