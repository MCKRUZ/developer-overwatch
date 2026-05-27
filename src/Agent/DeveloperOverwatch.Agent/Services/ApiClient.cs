using System.Text;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

public interface IApiClient
{
    Task<bool> SendSessionStartAsync(string payloadJson, CancellationToken ct);
    Task<bool> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct);
    Task<bool> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct);
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

    public Task<bool> SendSessionStartAsync(string payloadJson, CancellationToken ct) =>
        PostAsync("api/sessions/start", payloadJson, ct);

    public Task<bool> SendSessionEventsAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/events", payloadJson, ct);

    public Task<bool> SendSessionEndAsync(string sessionId, string payloadJson, CancellationToken ct) =>
        PostAsync($"api/sessions/{sessionId}/end", payloadJson, ct);

    private async Task<bool> PostAsync(string path, string payloadJson, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(path, content, ct);

            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("POST {Path} returned {StatusCode}", path, (int)response.StatusCode);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "HTTP error posting to {Path}", path);
            return false;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Timeout posting to {Path}", path);
            return false;
        }
    }
}
