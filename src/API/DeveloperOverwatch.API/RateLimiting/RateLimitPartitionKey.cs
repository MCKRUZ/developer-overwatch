namespace DeveloperOverwatch.API.RateLimiting;

using System.Security.Claims;
using DeveloperOverwatch.API.Authentication;

/// <summary>Chooses whose bucket a request draws from in the global rate limit. Must run after authentication,
/// and trusts only identities a handler has validated, so a caller cannot pick a fresh bucket per request.</summary>
public static class RateLimitPartitionKey
{
    private const string ObjectIdClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public static string For(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var identity = context.User.Identity;
        if (identity is not { IsAuthenticated: true })
            return $"ip:{ip}";

        return identity.AuthenticationType switch
        {
            // Every caller gets the same "anonymous" identity, so it says nothing about who is calling.
            AnonymousDashboardDefaults.AuthenticationScheme => $"ip:{ip}",
            // Every agent gets the same "agent" identity, and in Development with no keys configured any key
            // authenticates, so a bucket per key would let a caller dodge the limit by rotating keys. Agents stay per IP, in
            // their own bucket, so a dashboard over its limit on the same machine cannot stall ingest. Sending
            // or omitting the header can at most double one IP's budget, never open a bucket per request.
            ApiKeyDefaults.AuthenticationScheme => $"agent-ip:{ip}",
            _ => SignedInUser(context.User) is { } user ? $"user:{user}" : $"ip:{ip}",
        };
    }

    // JwtBearer maps "sub" to NameIdentifier and "oid" to its long URI by default; accept either spelling.
    // The display name is last: it is not unique, so two people sharing one is stricter, never looser.
    private static string? SignedInUser(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value
        ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("oid")?.Value
        ?? user.FindFirst(ObjectIdClaim)?.Value
        ?? user.Identity?.Name;
}
