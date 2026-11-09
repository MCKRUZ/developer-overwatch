namespace DeveloperOverwatch.API.Authentication;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

/// <summary>
/// Always-succeeding scheme used in place of JWT Bearer when Authentication:Authority is not
/// configured, so the dashboard is reachable without OIDC set up (matches documented behavior in
/// README.md's "Dashboard Environment Variables" section).
/// </summary>
public sealed class AnonymousDashboardAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public AnonymousDashboardAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "anonymous"),
            new Claim(ClaimTypes.Name, "anonymous"),
            new Claim("auth_method", "anonymous")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class AnonymousDashboardDefaults
{
    public const string AuthenticationScheme = "AnonymousDashboard";
}
