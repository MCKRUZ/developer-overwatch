namespace DeveloperOverwatch.API.Tests.Hubs;

using DeveloperOverwatch.API.Tests.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Xunit;

/// <summary>A WebSocket is authenticated once, when it opens, so without CloseOnAuthenticationExpiration a
/// connection outlives its token and keeps receiving usage data after the user's sign-in has lapsed. Long polling
/// cannot show this: every poll is a new request that authenticates again.</summary>
public class HubTokenExpiryTests : IClassFixture<OidcWebApplicationFactory>
{
    private static readonly TimeSpan ShortLifetime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CloseDeadline = TimeSpan.FromSeconds(10);

    private readonly OidcWebApplicationFactory _factory;

    public HubTokenExpiryTests(OidcWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task WebSocket_TokenExpires_ServerClosesTheConnection()
    {
        var server = _factory.Server;
        await using var expiring = Connect(server, OidcWebApplicationFactory.MintToken("expiring-user", ShortLifetime));
        // Control: a connection whose token is still valid must stay open over the same period.
        await using var valid = Connect(server, OidcWebApplicationFactory.MintToken("valid-user"));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        expiring.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        await expiring.StartAsync();
        await valid.StartAsync();
        var winner = await Task.WhenAny(closed.Task, Task.Delay(CloseDeadline));

        winner.Should().BeSameAs(closed.Task, "the server must close a WebSocket whose token has expired");
        valid.State.Should().Be(HubConnectionState.Connected);
    }

    private static HubConnection Connect(TestServer server, string token) => new HubConnectionBuilder()
        .WithUrl(new Uri(server.BaseAddress, "/hubs/overwatch"), options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.SkipNegotiation = true;
            options.WebSocketFactory = (context, cancellationToken) =>
            {
                var client = server.CreateWebSocketClient();
                client.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {token}";
                return new ValueTask<System.Net.WebSockets.WebSocket>(client.ConnectAsync(context.Uri, cancellationToken));
            };
        })
        .Build();
}
