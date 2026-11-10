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

    /// <summary>A larger reply fails the read with <see cref="HttpRequestException"/>, which is transient.</summary>
    private const long MaxResponseBytes = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient httpClient, ILogger<ApiClient> logger)
    {
        // The setter throws once the client has sent a request, and a shared client may already have.
        if (httpClient.MaxResponseContentBufferSize != MaxResponseBytes)
            httpClient.MaxResponseContentBufferSize = MaxResponseBytes;
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<ApiResult> SendSessionStartAsync(string payloadJson, CancellationToken ct) =>
        PostAsync("api/sessions/start", payloadJson, ReadSessionOutcome, ct);

    public Task<ApiResult> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/events", payloadJson, ReadEventResults, ct);

    public Task<ApiResult> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/end", payloadJson, ReadSessionOutcome, ct);

    /// <param name="readSuccess">Reads a 2xx reply; null when the reply is not one the server sends.</param>
    private async Task<ApiResult> PostAsync(
        string path, string payloadJson, Func<HttpStatusCode, string, ApiResult?> readSuccess, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(path, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
                return readSuccess(response.StatusCode, body) ?? Unrecognised(path, response.StatusCode);
            // A verdict on the whole request: the caller splits it, and logs each item it finally rejects.
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return new ApiResult(PostStatus.Rejected, Reason: FieldLimits.Clamp(body, MaxReasonLength));
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                return new ApiResult(PostStatus.TooLarge, Reason: "request too large (413)");
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

    private ApiResult Unrecognised(string path, HttpStatusCode status)
    {
        _logger.LogWarning("POST {Path} returned {StatusCode} with a reply the server does not send", path, (int)status);
        return ApiResult.Failed;
    }

    /// <summary>An <c>/events</c> reply lists each item's outcome in <c>results</c>. One with only
    /// <c>eventsIngested</c> is from a server older than per-item results, and every item counts as accepted.</summary>
    private static ApiResult? ReadEventResults(HttpStatusCode status, string body)
    {
        using var doc = ParseObject(body);
        if (doc is null)
            return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Object)
            return new ApiResult(PostStatus.Ok, ItemResultsOf(results));
        return root.TryGetProperty("eventsIngested", out _) ? ApiResult.Success : null;
    }

    /// <summary>A start or end reply names its <c>outcome</c>. One without it is from a server older than
    /// outcomes, which answered a plain 200 naming the session.</summary>
    private static ApiResult? ReadSessionOutcome(HttpStatusCode status, string body)
    {
        using var doc = ParseObject(body);
        if (doc is null)
            return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("outcome", out var outcome))
            return outcome.ValueKind == JsonValueKind.String && outcome.GetString() is "accepted" or "duplicate"
                ? ApiResult.Success
                : null;
        return status == HttpStatusCode.OK && root.TryGetProperty("sessionId", out _) ? ApiResult.Success : null;
    }

    private static ItemResults ItemResultsOf(JsonElement results)
    {
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in ArrayItems(results, "rejected"))
        {
            if (item.ValueKind == JsonValueKind.Object && StringProperty(item, "id") is { } id)
                rejected[id] = StringProperty(item, "reason") ?? "rejected";
        }

        return new ItemResults(Ids(results, "accepted"), Ids(results, "duplicate"), rejected);
    }

    /// <returns>Null when the body is not a JSON object.</returns>
    private static JsonDocument? ParseObject(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc;
            doc.Dispose();
            return null;
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
        using var doc = ParseObject(body);
        return doc is not null && StringProperty(doc.RootElement, "code") == SessionNotFoundCode;
    }
}
