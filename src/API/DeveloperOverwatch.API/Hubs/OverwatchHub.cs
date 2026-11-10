namespace DeveloperOverwatch.API.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

// Plain [Authorize] uses the Smart scheme: a signed-in user when OIDC is configured, anonymous otherwise.
// The JwtBearer-only Dashboard policy would lock the hub out of local runs without OIDC.
[Authorize]
public sealed class OverwatchHub : Hub<IOverwatchHubClient>
{
    public async Task JoinSession(string sessionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
