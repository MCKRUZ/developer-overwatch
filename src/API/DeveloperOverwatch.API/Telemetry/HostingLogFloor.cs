namespace DeveloperOverwatch.API.Telemetry;

using Microsoft.Extensions.Logging;

/// <summary>Holds ASP.NET Core's hosting log, which writes each request's full URL ("Request starting ..."), at
/// Warning for every logging provider, whatever configuration says. The SignalR client sends the signed-in user's
/// token as <c>?access_token=</c>, and with Application Insights on every log record is exported, so lowering
/// <c>Microsoft.AspNetCore</c> to Information in configuration must not put the token in the logs.</summary>
public static class HostingLogFloor
{
    public const string Category = "Microsoft.AspNetCore.Hosting.Diagnostics";

    // The alias of the provider Azure Monitor registers, named even when configuration has no section for it.
    private const string OpenTelemetryProvider = "OpenTelemetry";

    /// <summary>Appends the floor after every configured rule; runs again whenever configuration reloads.</summary>
    public static IServiceCollection AddHostingLogFloor(this IServiceCollection services) =>
        services.PostConfigure<LoggerFilterOptions>(Apply);

    // For one provider the logger picks a provider-specific rule over any provider-less one, then the longest
    // matching category, then the last of equals. So a provider-less rule alone loses to, for example,
    // Logging:Console:LogLevel:Microsoft.AspNetCore; each named provider gets its own rule as well.
    private static void Apply(LoggerFilterOptions options)
    {
        var providers = options.Rules
            .Select(rule => rule.ProviderName)
            .OfType<string>()
            .Append(OpenTelemetryProvider)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        options.Rules.Add(new LoggerFilterRule(null, Category, LogLevel.Warning, null));
        foreach (var provider in providers)
            options.Rules.Add(new LoggerFilterRule(provider, Category, LogLevel.Warning, null));
    }
}
