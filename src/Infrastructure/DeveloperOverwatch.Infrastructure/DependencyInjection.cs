namespace DeveloperOverwatch.Infrastructure;

using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
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
        services.AddDbContext<OverwatchDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ICommitSessionLinkRepository, CommitSessionLinkRepository>();
        services.AddScoped<IPullRequestReviewRepository, PullRequestReviewRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

        services.Configure<Services.GitHubConfig>(configuration.GetSection("GitHub"));
        services.AddHttpClient<IGitHostingService, Services.GitHubService>();

        return services;
    }
}
