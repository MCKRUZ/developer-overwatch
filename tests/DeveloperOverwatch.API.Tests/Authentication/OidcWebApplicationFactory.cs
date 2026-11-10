namespace DeveloperOverwatch.API.Tests.Authentication;

using System.Security.Claims;
using System.Text;
using DeveloperOverwatch.API.Tests.Controllers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

/// <summary>The real API with OIDC switched on, validating tokens this factory mints with a local symmetric
/// key instead of fetching an identity provider's metadata, so the production JwtBearer branch of
/// Program.cs runs unchanged.</summary>
public class OidcWebApplicationFactory : OverwatchWebApplicationFactory
{
    public const string Issuer = "https://issuer.test/";
    public const string Audience = "overwatch-api";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("overwatch-test-signing-key-0123456789abcdef"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        // Program.cs reads these before Build(), so they must be host settings, not app configuration.
        builder.UseSetting("Authentication:Authority", Issuer);
        builder.UseSetting("Authentication:Audience", Audience);

        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(SigningKey);
                options.Configuration = configuration;
                // The framework's own post-configure may already have built a manager that fetches the
                // authority's metadata over HTTP; replace it so validation never leaves the process.
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }));
    }

    /// <summary>A token the API accepts, for the given subject.</summary>
    public static string MintToken(string subject) => MintToken(subject, TimeSpan.FromHours(1));

    /// <summary>A token the API accepts for the given subject until <paramref name="lifetime"/> from now.</summary>
    public static string MintToken(string subject, TimeSpan lifetime) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Expires = DateTime.UtcNow.Add(lifetime),
        Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("name", "Shared Display Name")]),
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
    });
}
