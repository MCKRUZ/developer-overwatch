using DeveloperOverwatch.API.Authentication;
using DeveloperOverwatch.API.Middleware;
using DeveloperOverwatch.Application;
using DeveloperOverwatch.Infrastructure;
using DeveloperOverwatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString")
    ?? "Data Source=overwatch.db";

builder.Services.AddApplicationDependencies();
builder.Services.AddInfrastructureDependencies(connectionString, builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["http://localhost:3000", "http://localhost:5173"])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var authority = builder.Configuration["Authentication:Authority"];
var hasOidc = !string.IsNullOrEmpty(authority);

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "Smart";
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddPolicyScheme("Smart", "Route to API Key or JWT", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (context.Request.Headers.ContainsKey("X-Api-Key"))
            return ApiKeyDefaults.AuthenticationScheme;
        return JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyDefaults.AuthenticationScheme, options =>
    {
        var keys = builder.Configuration.GetSection("ApiKeys:Keys").Get<string[]>() ?? [];
        options.ValidKeys = new HashSet<string>(keys, StringComparer.Ordinal);
    })
.AddJwtBearer(options =>
{
    if (hasOidc)
    {
        options.Authority = authority;
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata =
            builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            RoleClaimType = "roles"
        };
    }
    else
    {
        // OIDC not configured — reject all Bearer tokens
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.NoResult();
                return Task.CompletedTask;
            }
        };
    }
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.Agent, policy =>
        policy.AddAuthenticationSchemes(ApiKeyDefaults.AuthenticationScheme)
              .RequireAuthenticatedUser());

    options.AddPolicy(AuthPolicies.Dashboard, policy =>
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
              .RequireAuthenticatedUser());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseCors();
app.UseMiddleware<WebhookSignatureMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

if (!hasOidc)
{
    app.Logger.LogWarning(
        "Authentication:Authority is not configured — JWT Bearer validation is disabled. " +
        "Set Authentication:Authority to your OIDC provider to enable dashboard authentication");
}

await app.RunAsync();

public partial class Program { }

public static class ApiKeyDefaults
{
    public const string AuthenticationScheme = "ApiKey";
}

public static class AuthPolicies
{
    public const string Agent = "Agent";
    public const string Dashboard = "Dashboard";
}
