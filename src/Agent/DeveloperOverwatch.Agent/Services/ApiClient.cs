using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

public interface IApiClient
{
    Task<ApiResult> SendSessionStartAsync(string payloadJson, CancellationToken ct);
    Task<ApiResult> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct);
    Task<ApiResult> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct);
}

public sealed class ApiClient : IApiClient
{
    private const string SessionNotFoundCode = "session_not_found";
    private const int MaxReasonLength = 500;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<ApiResult> SendSessionStartAsync(string payloadJson, CancellationToken ct) =>
        PostAsync("api/sessions/start", payloadJson, ct);

    public Task<ApiResult> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/events", payloadJson, ct);

    public Task<ApiResult> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/end", payloadJson, ct);

    private async Task<ApiResult> PostAsync(string path, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(path, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return new ApiResult(PostStatus.Ok, ParseItemResults(body));
            // The caller logs the rejection once, with what it was about to drop.
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new ApiResult(PostStatus.Rejected, Reason: FieldLimits.Clamp(body, MaxReasonLength));
            if (response.StatusCode == HttpStatusCode.NotFound && NamesSessionNotFound(body))
                return ApiResult.NotFound;

            _logger.LogWarning("POST {Path} returned {StatusCode}", path, (int)response.StatusCode);
            return ApiResult.Failed;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error posting to {Path}", path);
            return ApiResult.Failed;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout posting to {Path}", path);
            return ApiResult.Failed;
        }
    }

    /// <summary>The per-item outcomes in an <c>/events</c> response. Null when the body carries no
    /// <c>results</c> object: a start or end, or a server older than per-item results.</summary>
    private static ItemResults? ParseItemResults(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Object)
                return null;

            var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in ArrayItems(results, "rejected"))
            {
                if (item.ValueKind == JsonValueKind.Object && StringProperty(item, "id") is { } id)
                    rejected[id] = StringProperty(item, "reason") ?? "rejected";
            }

            return new ItemResults(Ids(results, "accepted"), Ids(results, "duplicate"), rejected);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HashSet<string> Ids(JsonElement results, string name) =>
        ArrayItems(results, name)
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<JsonElement> ArrayItems(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().ToList()
            : [];

    private static string? StringProperty(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Only the server's own session-not-found answer counts. A 404 from a wrong base path or
    /// a proxy must stay retryable, or the session's queued events would be dropped.</summary>
    private static bool NamesSessionNotFound(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && StringProperty(doc.RootElement, "code") == SessionNotFoundCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
