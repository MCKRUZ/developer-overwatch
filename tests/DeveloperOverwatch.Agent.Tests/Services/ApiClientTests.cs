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

        result.Should().Be(ApiResult.Success);
    }

    [Fact]
    public async Task SendSessionStart_ServerError_ReturnsFalse()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().Be(ApiResult.Failed);
    }

    [Fact]
    public async Task SendSessionEvents_Success_ReturnsTrue()
    {
        _handler.StatusCode = HttpStatusCode.OK;

        var result = await _sut.SendSessionEventsAsync("session-1", "{}", CancellationToken.None);

        result.Should().Be(ApiResult.Success);
    }

    [Fact]
    public async Task SendSessionEnd_Success_ReturnsTrue()
    {
        _handler.StatusCode = HttpStatusCode.OK;

        var result = await _sut.SendSessionEndAsync("session-1", "{}", CancellationToken.None);

        result.Should().Be(ApiResult.Success);
    }

    [Fact]
    public async Task SendSessionStart_HttpException_ReturnsFalse()
    {
        _handler.Handler = _ => throw new HttpRequestException("Connection refused");

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().Be(ApiResult.Failed);
    }

    [Theory]
    [InlineData("events")]
    [InlineData("end")]
    public async Task Send_404WithSessionNotFoundCode_ReturnsNotFound(string action)
    {
        _handler.Handler = _ => Respond(HttpStatusCode.NotFound,
            """{"error":"Session s-1 not found","code":"session_not_found"}""");

        var result = action == "end"
            ? await _sut.SendSessionEndAsync("s-1", "{}", CancellationToken.None)
            : await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.Should().Be(ApiResult.NotFound);
    }

    // A wrong base path or a proxy also answers 404. Dropping the session's events on that would lose data.
    [Theory]
    [InlineData("")]
    [InlineData("<html>Not Found</html>")]
    [InlineData("""{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","status":404}""")]
    [InlineData("""{"code":"something_else"}""")]
    public async Task SendSessionEvents_404WithoutTheCode_ReturnsFailed(string body)
    {
        _handler.Handler = _ => Respond(HttpStatusCode.NotFound, body);

        var result = await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.Should().Be(ApiResult.Failed);
    }

    [Fact]
    public async Task SendSessionEvents_400_ReturnsRejected()
    {
        _handler.Handler = _ => Respond(HttpStatusCode.BadRequest, """{"errors":["At most 500 tool failures per batch."]}""");

        var result = await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.Status.Should().Be(PostStatus.Rejected);
        result.Reason.Should().Contain("At most 500 tool failures");
    }

    [Fact]
    public async Task SendSessionEvents_Results_GiveEachItemItsOwnOutcome()
    {
        _handler.Handler = _ => Respond(HttpStatusCode.OK, """
            {"eventsIngested":1,"results":{"accepted":["a"],"duplicate":["d"],"rejected":[{"id":"r","reason":"role is required"}]}}
            """);

        var result = await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.OutcomeOf("a").Should().Be(ItemOutcome.Accepted);
        result.OutcomeOf("d").Should().Be(ItemOutcome.Duplicate);
        result.OutcomeOf("r").Should().Be(ItemOutcome.Rejected);
        result.RejectionReason("r").Should().Be("role is required");
        result.OutcomeOf("missing").Should().BeNull("an id the server did not list stays pending");
    }

    [Fact]
    public async Task SendSessionEvents_OldServerWithoutResults_CountsEveryItemAccepted()
    {
        _handler.Handler = _ => Respond(HttpStatusCode.OK, """{"eventsIngested":3}""");

        var result = await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.OutcomeOf("any").Should().Be(ItemOutcome.Accepted);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("duplicate")]
    public async Task SendSessionEnd_AcceptedOrDuplicate_IsDefinitive(string outcome)
    {
        _handler.Handler = _ => Respond(HttpStatusCode.OK, $$"""{"sessionId":"s-1","outcome":"{{outcome}}"}""");

        var result = await _sut.SendSessionEndAsync("s-1", "{}", CancellationToken.None);

        result.Status.Should().Be(PostStatus.Ok);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task SendSessionEvents_RetryableStatus_IsTransient(HttpStatusCode status)
    {
        _handler.Handler = _ => Respond(status, "");

        var result = await _sut.SendSessionEventsAsync("s-1", "{}", CancellationToken.None);

        result.Status.Should().Be(PostStatus.Transient);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task SendSessionStart_Timeout_ReturnsFalse()
    {
        _handler.Handler = _ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout",
            new TimeoutException());

        var result = await _sut.SendSessionStartAsync("{}", CancellationToken.None);

        result.Should().Be(ApiResult.Failed);
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
