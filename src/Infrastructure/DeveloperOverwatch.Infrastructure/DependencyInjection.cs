namespace DeveloperOverwatch.Infrastructure;

using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using DeveloperOverwatch.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        var provider = configuration.GetValue("Database:Provider", "sqlite");

        services.AddDbContext<OverwatchDbContext>(options =>
        {
            if (string.Equals(provider, "sqlserver", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString);
            else
                options.UseSqlite(connectionString);
        });

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ICommitSessionLinkRepository, CommitSessionLinkRepository>();
        services.AddScoped<IPullRequestReviewRepository, PullRequestReviewRepository>();
        services.AddScoped<IArchitectAnnotationRepository, ArchitectAnnotationRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

        services.Configure<GitHubConfig>(configuration.GetSection("GitHub"));
        services.AddHttpClient<IGitHostingService, GitHubService>();

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
