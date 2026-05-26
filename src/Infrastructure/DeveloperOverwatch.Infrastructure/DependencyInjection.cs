namespace DeveloperOverwatch.Infrastructure;

using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDependencies(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OverwatchDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OverwatchDbContext>());

        return services;
    }
}
