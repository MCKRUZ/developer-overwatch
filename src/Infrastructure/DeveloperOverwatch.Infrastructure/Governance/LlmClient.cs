namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface ILlmClient
{
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public sealed class ClaudeLlmClient : ILlmClient
{
    private static readonly string[] CognitiveServicesScope = ["https://cognitiveservices.azure.com/.default"];

    private readonly HttpClient _httpClient;
    private readonly GovernanceConfig _config;
    private readonly ILogger<ClaudeLlmClient> _logger;
    private readonly TokenCredential? _credential;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ClaudeLlmClient(
        HttpClient httpClient,
        IOptions<GovernanceConfig> config,
        ILogger<ClaudeLlmClient> logger)
    {
        _config = config.Value;
        _logger = logger;
        _httpClient = httpClient;

        if (!string.IsNullOrEmpty(_config.LlmApiUrl))
            _httpClient.BaseAddress = new Uri(_config.LlmApiUrl);

        // Claude models served through Azure AI Foundry don't use Anthropic-issued API keys —
        // auth is an Azure AD bearer token, acquired fresh per request below (tokens expire;
        // TokenCredential caches internally so this is cheap, unlike the static x-api-key header
        // which is set once here and never changes).
        if (_config.UseManagedIdentityAuth)
        {
            _credential = string.IsNullOrEmpty(_config.ManagedIdentityClientId)
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential(_config.ManagedIdentityClientId);
        }
        else if (!string.IsNullOrEmpty(_config.LlmApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _config.LlmApiKey);
        }

        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.LlmTimeoutSeconds);
    }

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var isConfigured = _credential is not null || !string.IsNullOrEmpty(_config.LlmApiKey);
        if (string.IsNullOrEmpty(_config.LlmApiUrl) || !isConfigured)
        {
            _logger.LogDebug("LLM not configured");
            return null;
        }

        var request = new MessagesRequest
        {
            Model = _config.LlmModel,
            MaxTokens = _config.LlmMaxTokens,
            System = systemPrompt,
            Messages = [new Message { Role = "user", Content = userPrompt }]
        };

        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

            if (_credential is not null)
            {
                var token = await _credential.GetTokenAsync(new TokenRequestContext(CognitiveServicesScope), ct);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await _httpClient.SendAsync(httpRequest, ct);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonSerializer.DeserializeAsync<MessagesResponse>(stream, JsonOptions, ct);

            return result?.Content is { Count: > 0 } ? result.Content[0].Text : null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "LLM request failed");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "LLM request timed out or was cancelled");
            return null;
        }
    }

    private sealed class MessagesRequest
    {
        public string Model { get; init; } = string.Empty;
        public int MaxTokens { get; init; }
        public string System { get; init; } = string.Empty;
        public List<Message> Messages { get; init; } = [];
    }

    private sealed class Message
    {
        public string Role { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
    }

    private sealed class MessagesResponse
    {
        public List<ContentBlock>? Content { get; init; }
    }

    private sealed class ContentBlock
    {
        public string? Text { get; init; }
    }
}
