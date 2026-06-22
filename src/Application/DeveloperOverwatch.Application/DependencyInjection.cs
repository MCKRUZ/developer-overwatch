namespace DeveloperOverwatch.Application;

using System.Reflection;
using DeveloperOverwatch.Application.Behaviors;
using DeveloperOverwatch.Application.Idempotency;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Behaviors execute in registration order (outermost first): tracing spans the whole
        // pipeline; idempotency short-circuits duplicates before validation runs.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RequestTracingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient<IPipelineBehavior<Commands.IngestSessionEnd.IngestSessionEndCommand, Domain.Common.Result<string>>, GovernanceBehavior>();

        services.AddScoped<Services.SessionCommitCorrelator>();

        return services;
    }
}
