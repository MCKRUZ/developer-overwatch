using System.Net;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public class ApiClientTests : IDisposable
{
    private readonly TestHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly ApiClient _sut;

    public ApiClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost/") };
        var logger = Substitute.For<ILogger<ApiClient>>();
        _sut = new ApiClient(_httpClient, logger);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task SendSessionStart_Success_ReturnsTrue()
    {
        _handler.StatusCode = HttpStatusCode.OK;

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendSessionStart_ServerError_ReturnsFalse()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendSessionEvents_Success_ReturnsTrue()
    {
        _handler.StatusCode = HttpStatusCode.OK;

        var result = await _sut.SendSessionEventsAsync("session-1", "{}", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendSessionEnd_Success_ReturnsTrue()
    {
        _handler.StatusCode = HttpStatusCode.OK;

        var result = await _sut.SendSessionEndAsync("session-1", "{}", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendSessionStart_HttpException_ReturnsFalse()
    {
        _handler.Handler = _ => throw new HttpRequestException("Connection refused");

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendSessionStart_Timeout_ReturnsFalse()
    {
        _handler.Handler = _ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout",
            new TimeoutException());

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().BeFalse();
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public Func<HttpRequestMessage, HttpResponseMessage>? Handler { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Handler is not null)
                return Task.FromResult(Handler(request));

            return Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }
}
