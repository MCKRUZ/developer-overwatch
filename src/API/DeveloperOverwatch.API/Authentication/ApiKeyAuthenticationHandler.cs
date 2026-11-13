namespace DeveloperOverwatch.API.Authentication;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

        if (!Options.AcceptAnyKey && !Options.Matches(apiKey))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "agent"),
            new Claim(ClaimTypes.Name, "overwatch-agent"),
            new Claim(ClaimTypes.Role, AgentRole),
            new Claim("auth_method", "api_key")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>The role every agent key grants. The Agent policy requires it, so a route that also carries a
    /// dashboard policy cannot be opened by a dashboard sign-in or by anonymous dashboard access.</summary>
    public const string AgentRole = "Agent";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    private IReadOnlyList<byte[]> _keyHashes = [];

    /// <summary>True only in Development with no key configured, so a local stack works without setting one up.
    /// Everywhere else an unknown key is refused, and startup fails when no key is configured.</summary>
    public bool AcceptAnyKey { get; set; }

    public int KeyCount => _keyHashes.Count;

    /// <summary>Sets the accepted keys. Blank entries are dropped: a missing secret arrives as an empty value. Keys
    /// are trimmed, as the installer trims the key it reads and Kestrel trims header values, so a secret saved with
    /// a trailing newline still matches what agents send.</summary>
    public void SetKeys(IEnumerable<string?> keys) =>
        _keyHashes = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => Hash(key!.Trim()))
            .ToArray();

    /// <summary>Compares against every key without stopping at a match, and compares fixed-length hashes, so the time
    /// taken says nothing about how much of a key was right or which key matched.</summary>
    public bool Matches(string presented)
    {
        var presentedHash = Hash(presented);
        var matched = false;
        foreach (var keyHash in _keyHashes)
            matched |= CryptographicOperations.FixedTimeEquals(presentedHash, keyHash);
        return matched;
    }

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
}
