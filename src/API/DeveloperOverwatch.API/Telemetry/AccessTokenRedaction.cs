namespace DeveloperOverwatch.API.Telemetry;

using System.Diagnostics;
using Microsoft.Extensions.Primitives;

/// <summary>Keeps a signed-in user's token out of request spans. The SignalR client sends the token as
/// <c>?access_token=</c>. OpenTelemetry.Instrumentation.AspNetCore 1.15.2 would replace every query value with
/// "Redacted" (HttpInListener.OnStartActivity, RedactionHelper), but Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0
/// sets OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=true unless it is already configured
/// (OpenTelemetryBuilderExtensions, citing GHSA-vh2m-22xx-q94f), so with Application Insights on the query is
/// recorded verbatim in <c>url.query</c>. HubTelemetryTests pins that behaviour for the resolved versions.</summary>
public static class AccessTokenRedaction
{
    public const string QueryKey = "access_token";
    public const string Placeholder = "Redacted";
    private const string QueryTag = "url.query";

    /// <summary>Replaces the token's value in the span's recorded query, leaving the other parameters as sent.</summary>
    public static void Apply(Activity activity, HttpRequest request)
    {
        if (!request.Query.ContainsKey(QueryKey))
            return;

        var redacted = request.Query.Select(p => KeyValuePair.Create(
            p.Key, p.Key.Equals(QueryKey, StringComparison.OrdinalIgnoreCase) ? new StringValues(Placeholder) : p.Value));
        activity.SetTag(QueryTag, QueryString.Create(redacted).Value);
    }
}
