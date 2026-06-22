namespace DeveloperOverwatch.API.Tests.Hubs;

using DeveloperOverwatch.API.Hubs;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

public class OverwatchHubTests
{
    private static OverwatchHub CreateHub(IGroupManager groups, string connectionId = "conn-1")
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        return new OverwatchHub
        {
            Context = context,
            Groups = groups,
        };
    }

    [Fact]
    public async Task JoinSession_AddsConnectionToSessionGroup()
    {
        var groups = Substitute.For<IGroupManager>();
        var hub = CreateHub(groups, "conn-1");

        await hub.JoinSession("session-42");

        await groups.Received(1).AddToGroupAsync("conn-1", "session-42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveSession_RemovesConnectionFromSessionGroup()
    {
        var groups = Substitute.For<IGroupManager>();
        var hub = CreateHub(groups, "conn-1");

        await hub.LeaveSession("session-42");

        await groups.Received(1).RemoveFromGroupAsync("conn-1", "session-42", Arg.Any<CancellationToken>());
    }
}
