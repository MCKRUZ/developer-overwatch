namespace DeveloperOverwatch.API.Telemetry;

using System.Diagnostics;
using Microsoft.Extensions.Primitives;

/// <summary>Keeps a signed-in user's token out of request spans. The SignalR client sends the token as
/// <c>?access_token=</c>, and the ASP.NET Core instrumentation records the query string verbatim in
/// <c>url.query</c>; its built-in redaction covers only a fixed list of signature keys, not this one.</summary>
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
