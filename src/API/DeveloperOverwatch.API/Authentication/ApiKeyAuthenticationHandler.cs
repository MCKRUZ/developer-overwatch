namespace DeveloperOverwatch.API.Authentication;

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private const string ApiKeyHeader = "X-Api-Key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var apiKey = apiKeyHeader.ToString();
        if (string.IsNullOrEmpty(apiKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (Options.ValidKeys.Count > 0 && !Options.ValidKeys.Contains(apiKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "agent"),
            new Claim(ClaimTypes.Name, "overwatch-agent"),
            new Claim(ClaimTypes.Role, "Agent"),
            new Claim("auth_method", "api_key")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public HashSet<string> ValidKeys { get; set; } = new(StringComparer.Ordinal);
}
