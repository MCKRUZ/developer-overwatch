using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>How a POST to the server ended. <see cref="NotFound"/> means the server does not know the
/// session, which a retry can never fix.</summary>
public enum ApiResult
{
    Success,
    Failed,
    NotFound
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

            _logger.LogWarning("POST {Path} returned {StatusCode}", path, (int)response.StatusCode);
            return response.StatusCode == HttpStatusCode.NotFound ? ApiResult.NotFound : ApiResult.Failed;
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
}
