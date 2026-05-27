namespace DeveloperOverwatch.Application.Tests.Governance;

using System.Net;
using System.Text.Json;
using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public class LlmClientTests
{
    private static readonly GovernanceConfig ValidConfig = new()
    {
        LlmApiUrl = "https://api.anthropic.test/",
        LlmApiKey = "sk-test-key",
        LlmModel = "claude-sonnet-4-6",
        LlmMaxTokens = 1024,
        LlmTimeoutSeconds = 30
    };

    [Theory]
    [InlineData(null, "sk-key")]
    [InlineData("", "sk-key")]
    [InlineData("https://api.test/", null)]
    [InlineData("https://api.test/", "")]
    public async Task CompleteAsync_NotConfigured_ReturnsNull(string? url, string? key)
    {
        var config = new GovernanceConfig { LlmApiUrl = url, LlmApiKey = key };
        var handler = new TestHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        var client = new ClaudeLlmClient(
            httpClient,
            Options.Create(config),
            NullLogger<ClaudeLlmClient>.Instance);

        var result = await client.CompleteAsync("system", "user");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_SuccessfulResponse_ReturnsText()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseContent = """{"content":[{"type":"text","text":"the response text"}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, handler);

        var result = await client.CompleteAsync("system prompt", "user prompt");

        result.Should().Be("the response text");
    }

    [Fact]
    public async Task CompleteAsync_HttpError_ReturnsNull()
    {
        var handler = new TestHttpMessageHandler { ThrowOnSend = true };
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, handler);

        var result = await client.CompleteAsync("system", "user");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_EmptyContent_ReturnsNull()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseContent = """{"content":[]}"""
        };
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, handler);

        var result = await client.CompleteAsync("system", "user");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_SendsCorrectPayload()
    {
        var handler = new TestHttpMessageHandler
        {
            ResponseContent = """{"content":[{"type":"text","text":"ok"}]}"""
        };
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, handler);

        await client.CompleteAsync("the system prompt", "the user prompt");

        handler.LastRequest.Should().NotBeNull();
        var body = handler.LastRequestBody;
        body.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be("claude-sonnet-4-6");
        root.GetProperty("max_tokens").GetInt32().Should().Be(1024);
        root.GetProperty("system").GetString().Should().Be("the system prompt");
        root.GetProperty("messages").GetArrayLength().Should().Be(1);

        var message = root.GetProperty("messages")[0];
        message.GetProperty("role").GetString().Should().Be("user");
        message.GetProperty("content").GetString().Should().Be("the user prompt");
    }

    private static ClaudeLlmClient CreateClient(HttpClient httpClient, TestHttpMessageHandler handler)
    {
        return new ClaudeLlmClient(
            httpClient,
            Options.Create(ValidConfig),
            NullLogger<ClaudeLlmClient>.Instance);
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string ResponseContent { get; set; } = "";
        public HttpRequestMessage? LastRequest { get; set; }
        public string? LastRequestBody { get; set; }
        public bool ThrowOnSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ThrowOnSend)
                throw new HttpRequestException("Simulated HTTP error");

            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseContent, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
