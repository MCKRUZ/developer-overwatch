namespace DeveloperOverwatch.Infrastructure;

using Azure.Identity;
using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using DeveloperOverwatch.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        var provider = configuration.GetValue("Database:Provider", "sqlite");
        var isSqlServer = string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase);
        var dbConfig = configuration.GetSection("Database").Get<DatabaseConfig>() ?? new DatabaseConfig();

        if (isSqlServer && dbConfig.UseManagedIdentityAuth)
        {
            Azure.Core.TokenCredential credential = string.IsNullOrEmpty(dbConfig.ManagedIdentityClientId)
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential(dbConfig.ManagedIdentityClientId);
            services.AddSingleton(new SqlManagedIdentityConnectionInterceptor(credential));
        }

        if (isSqlServer)
        {
            services.AddDbContext<OverwatchDbContext>((sp, options) =>
            {
                options.UseSqlServer(connectionString);
                if (dbConfig.UseManagedIdentityAuth)
                    options.AddInterceptors(sp.GetRequiredService<SqlManagedIdentityConnectionInterceptor>());
            });
        }
        else
        {
            // SQLite has its own migration set, keyed to this subclass.
            services.AddDbContext<OverwatchDbContext, SqliteOverwatchDbContext>(options => options.UseSqlite(connectionString));
        }

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUsageRepository, UsageRepository>();
        services.AddScoped<ICommitSessionLinkRepository, CommitSessionLinkRepository>();
        services.AddScoped<IPullRequestReviewRepository, PullRequestReviewRepository>();
        services.AddScoped<IArchitectAnnotationRepository, ArchitectAnnotationRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IDeveloperPlanRepository, DeveloperPlanRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

        services.Configure<GitHubConfig>(configuration.GetSection("GitHub"));
        services.AddHttpClient<IGitHostingService, GitHubService>();

        services.Configure<TokenPricingOptions>(configuration.GetSection("TokenPricing"));
        services.AddSingleton<ITokenPricingCalculator>(sp =>
            sp.GetRequiredService<IOptions<TokenPricingOptions>>().Value.ToPriceTable());
        services.AddScoped<TurnRepricer>();

        services.Configure<ModelContextWindowsOptions>(configuration.GetSection("ModelContextWindows"));
        services.AddSingleton<IModelContextWindowProvider>(sp =>
            sp.GetRequiredService<IOptions<ModelContextWindowsOptions>>().Value.ToTable());

        services.Configure<SessionActivityConfig>(configuration.GetSection("SessionActivity"));
        services.AddSingleton<ISessionStaleThresholdProvider, SessionStaleThresholdProvider>();

        services.AddScoped<IGovernanceViolationRepository, GovernanceViolationRepository>();

        services.Configure<Governance.GovernanceConfig>(configuration.GetSection("Governance"));
        services.AddSingleton<Governance.PolicyLoader>();
        services.AddSingleton<Governance.PatternRuleEvaluator>();
        services.AddSingleton<Governance.DriftDetector>();
        services.AddHttpClient<Governance.ILlmClient, Governance.ClaudeLlmClient>();
        services.AddScoped<Governance.SemanticRuleEvaluator>();
        services.AddScoped<IGovernanceEngine, Governance.GovernanceEngine>();

        services.Configure<SlackConfig>(configuration.GetSection("Notifications:Slack"));
        services.Configure<EmailConfig>(configuration.GetSection("Notifications:Email"));
        services.AddHttpClient<SlackNotificationService>();
        services.AddScoped<SlackNotificationService>();
        services.AddScoped<EmailNotificationService>();
        services.AddScoped<INotificationService, CompositeNotificationService>();
        services.AddKeyedScoped<IOverwatchNotifier, OverwatchNotifier>(
            Application.Services.CompositeOverwatchNotifier.ChannelKey);

        return services;
    }
}
