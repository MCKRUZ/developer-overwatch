namespace DeveloperOverwatch.Infrastructure.Governance;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface ILlmClient
{
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

public sealed class ClaudeLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly GovernanceConfig _config;
    private readonly ILogger<ClaudeLlmClient> _logger;

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

        if (!string.IsNullOrEmpty(_config.LlmApiKey))
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _config.LlmApiKey);

        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.LlmTimeoutSeconds);
    }

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.LlmApiUrl) || string.IsNullOrEmpty(_config.LlmApiKey))
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
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync("v1/messages", content, ct);

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
