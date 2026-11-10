namespace DeveloperOverwatch.API.Hubs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

// DashboardRead: a signed-in user when OIDC is configured, anonymous otherwise, never the agent API key.
// The JwtBearer-only Dashboard policy would lock the hub out of local runs without OIDC.
[Authorize(Policy = AuthPolicies.DashboardRead)]
public sealed class OverwatchHub : Hub<IOverwatchHubClient>
{
    public async Task JoinSession(string sessionId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

    public async Task LeaveSession(string sessionId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
}
