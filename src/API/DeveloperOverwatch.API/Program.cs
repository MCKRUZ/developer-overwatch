extern alias AzureIdentityAlias;

using System.Reflection;
using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DeveloperOverwatch.API.Authentication;
using DeveloperOverwatch.API.Hubs;
using DeveloperOverwatch.API.Middleware;
using DeveloperOverwatch.Application;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure;
using DeveloperOverwatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString")
    ?? "Data Source=overwatch.db";
var dbProvider = builder.Configuration.GetValue("Database:Provider", "sqlite");

var appInsightsCs = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(appInsightsCs))
{
    // Managed-identity auth for ingestion: the connection string's InstrumentationKey identifies
    // the resource, but the Credential below is what actually authenticates the ingestion call
    // (requires the Monitoring Metrics Publisher role on the container app's identity). Falls back
    // to ikey-only auth locally, where no user-assigned identity/AZURE_CLIENT_ID is present.
    var miClientId = builder.Configuration["AZURE_CLIENT_ID"];
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsCs;
            if (!string.IsNullOrEmpty(miClientId))
            {
                options.Credential = new AzureIdentityAlias::Azure.Identity.ManagedIdentityCredential(miClientId);
            }
        })
        .WithTracing(tracing => tracing.AddSource(ApplicationTelemetry.ActivitySourceName));
}

var healthChecks = builder.Services.AddHealthChecks();
if (string.Equals(dbProvider, "sqlserver", StringComparison.OrdinalIgnoreCase))
    healthChecks.AddSqlServer(connectionString, name: "database", tags: ["ready"]);
else
    healthChecks.AddSqlite(connectionString, name: "database", tags: ["ready"]);

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

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Developer Overwatch API",
        Description = "API for monitoring Claude Code sessions, enforcing governance policies, and providing analytics",
        Version = "v1"
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key for agent authentication"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Bearer token for dashboard authentication"
    });

    options.AddSecurityRequirement(document =>
    {
        var requirement = new OpenApiSecurityRequirement();
        requirement[new OpenApiSecuritySchemeReference("ApiKey", document)] = [];
        requirement[new OpenApiSecuritySchemeReference("Bearer", document)] = [];
        return requirement;
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

builder.Services.AddSignalR();
builder.Services.AddScoped<IOverwatchNotifier, SignalROverwatchNotifier>();

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

var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
var defaultPermitLimit = rateLimitConfig.GetValue("Default:PermitLimit", 100);
var defaultWindowSeconds = rateLimitConfig.GetValue("Default:WindowSeconds", 60);
var webhookPermitLimit = rateLimitConfig.GetValue("Webhook:PermitLimit", 30);
var webhookWindowSeconds = rateLimitConfig.GetValue("Webhook:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("webhook", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = webhookPermitLimit,
                Window = TimeSpan.FromSeconds(webhookWindowSeconds),
                QueueLimit = 0
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (context.Request.Path.StartsWithSegments("/health"))
            return RateLimitPartition.GetNoLimiter("health");

        if (context.Request.Path.StartsWithSegments("/api/webhooks"))
            return RateLimitPartition.GetNoLimiter("webhook-global-bypass");

        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = defaultPermitLimit,
                Window = TimeSpan.FromSeconds(defaultWindowSeconds),
                QueueLimit = 0
            });
    });
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
        return hasOidc
            ? JwtBearerDefaults.AuthenticationScheme
            : AnonymousDashboardDefaults.AuthenticationScheme;
    };
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyDefaults.AuthenticationScheme, options =>
    {
        var keys = builder.Configuration.GetSection("ApiKeys:Keys").Get<string[]>() ?? [];
        options.ValidKeys = new HashSet<string>(keys, StringComparer.Ordinal);
    })
.AddScheme<AuthenticationSchemeOptions, AnonymousDashboardAuthenticationHandler>(
    AnonymousDashboardDefaults.AuthenticationScheme, _ => { })
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
        // OIDC not configured — reject all Bearer tokens (AnonymousDashboard scheme handles
        // dashboard requests instead, per the ForwardDefaultSelector above).
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OverwatchDbContext>();
    if (string.Equals(dbProvider, "sqlserver", StringComparison.OrdinalIgnoreCase))
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseCors();
app.UseMiddleware<WebhookSignatureMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<OverwatchHub>("/hubs/overwatch");
app.MapHealthChecks("/health");

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
