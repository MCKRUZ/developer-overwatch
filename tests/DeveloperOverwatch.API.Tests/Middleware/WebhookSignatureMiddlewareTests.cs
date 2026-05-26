namespace DeveloperOverwatch.API.Tests.Middleware;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public class WebhookSignatureMiddlewareTests
{
    private const string TestSecret = "test-webhook-secret";

    private static TestServer CreateServer(string? secret = TestSecret)
    {
        var config = new Dictionary<string, string?>
        {
            ["GitHub:WebhookSecret"] = secret
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(config))
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s => s.AddRouting());
                web.Configure(app =>
                {
                    app.UseMiddleware<DeveloperOverwatch.API.Middleware.WebhookSignatureMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost("/api/webhooks/github", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("OK");
                        });
                        endpoints.MapGet("/api/sessions", async ctx =>
                        {
                            ctx.Response.StatusCode = 200;
                            await ctx.Response.WriteAsync("OK");
                        });
                    });
                });
            })
            .Build();

        host.Start();
        return host.GetTestServer();
    }

    [Fact]
    public async Task ValidSignature_PassesThrough()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var body = "{\"action\":\"push\"}";
        var signature = ComputeSignature(body, TestSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", $"sha256={signature}");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=invalid");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MissingSignature_Returns401()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NonWebhookRoute_PassesThrough()
    {
        using var server = CreateServer();
        var client = server.CreateClient();

        var response = await client.GetAsync("/api/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NoSecretConfigured_PassesThrough()
    {
        using var server = CreateServer(secret: null);
        var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hash);
    }
}
