namespace DeveloperOverwatch.API.Tests.Hubs;

using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

public class SignalROverwatchNotifierTests
{
    [Fact]
    public async Task ViolationCreatedAsync_SendsToSessionGroupAndAllClients_WithMappedDto()
    {
        var hub = Substitute.For<IHubContext<OverwatchHub, IOverwatchHubClient>>();
        var clients = Substitute.For<IHubClients<IOverwatchHubClient>>();
        var groupClient = Substitute.For<IOverwatchHubClient>();
        var allClient = Substitute.For<IOverwatchHubClient>();

        hub.Clients.Returns(clients);
        clients.Group("session-1").Returns(groupClient);
        clients.All.Returns(allClient);

        var notifier = new SignalROverwatchNotifier(hub);

        var violation = new GovernanceViolation
        {
            Id = "v1",
            SessionId = "session-1",
            RuleId = "no-secrets",
            Severity = ViolationSeverity.Critical,
            Message = "Hardcoded secret",
        };

        await notifier.ViolationCreatedAsync(violation, CancellationToken.None);

        var expected = new ViolationDto("v1", "session-1", "no-secrets", "Critical", "Hardcoded secret");

        await groupClient.Received(1).ViolationCreated(Arg.Is<ViolationDto>(d => d == expected));
        await allClient.Received(1).ViolationCreated(Arg.Is<ViolationDto>(d => d == expected));
    }

    [Fact]
    public async Task ViolationCreatedAsync_TargetsGroupKeyedBySessionId()
    {
        var hub = Substitute.For<IHubContext<OverwatchHub, IOverwatchHubClient>>();
        var clients = Substitute.For<IHubClients<IOverwatchHubClient>>();
        clients.Group(Arg.Any<string>()).Returns(Substitute.For<IOverwatchHubClient>());
        clients.All.Returns(Substitute.For<IOverwatchHubClient>());
        hub.Clients.Returns(clients);

        var notifier = new SignalROverwatchNotifier(hub);
        var violation = new GovernanceViolation
        {
            Id = "v2", SessionId = "abc-123", RuleId = "r", Severity = ViolationSeverity.Warning, Message = "m",
        };

        await notifier.ViolationCreatedAsync(violation, CancellationToken.None);

        clients.Received(1).Group("abc-123");
    }
}
