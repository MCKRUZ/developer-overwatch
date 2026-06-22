namespace DeveloperOverwatch.Application;

/// <summary>Shared telemetry identifiers for the Application layer.</summary>
public static class ApplicationTelemetry
{
    /// <summary>
    /// ActivitySource name for MediatR pipeline spans. The API host registers this with the
    /// tracer provider so spans are exported alongside ASP.NET Core and HttpClient traces.
    /// </summary>
    public const string ActivitySourceName = "DeveloperOverwatch.Application";
}
