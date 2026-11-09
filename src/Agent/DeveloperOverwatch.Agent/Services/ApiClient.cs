using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>How a POST to the server ended. <see cref="NotFound"/> means the server said it does not know
/// the session, and <see cref="Rejected"/> that it refused the payload (400); a retry can fix neither.
/// <see cref="Failed"/> is everything else, retried later.</summary>
public enum ApiResult
{
    Success,
    Failed,
    NotFound,
    Rejected
}

public interface IApiClient
{
    Task<ApiResult> SendSessionStartAsync(string payloadJson, CancellationToken ct);
    Task<ApiResult> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct);
    Task<ApiResult> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct);
}

public sealed class ApiClient : IApiClient
{
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

            if (response.IsSuccessStatusCode)
                return ApiResult.Success;

            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogWarning("POST {Path} was rejected (400): {Body}", path, Truncate(body));
                return ApiResult.Rejected;
            }

            _logger.LogWarning("POST {Path} returned {StatusCode}", path, (int)response.StatusCode);
            return response.StatusCode == HttpStatusCode.NotFound && NamesSessionNotFound(body)
                ? ApiResult.NotFound
                : ApiResult.Failed;
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

    /// <summary>Only the server's own session-not-found answer counts. A 404 from a wrong base path or
    /// a proxy must stay retryable, or the session's queued events would be dropped.</summary>
    private static bool NamesSessionNotFound(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() == SessionNotFoundCode;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private const string SessionNotFoundCode = "session_not_found";

    private static string Truncate(string body) => body.Length <= 500 ? body : body[..500] + "…";
}
