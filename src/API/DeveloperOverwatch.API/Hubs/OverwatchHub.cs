namespace DeveloperOverwatch.API.Hubs;

using Microsoft.AspNetCore.SignalR;

public sealed class OverwatchHub : Hub<IOverwatchHubClient>
{
    public async Task JoinSession(string sessionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
