namespace DeveloperOverwatch.API.Controllers;

using System.Text.Json;
using DeveloperOverwatch.Application.Commands.ProcessPrWebhook;
using DeveloperOverwatch.Application.Commands.ProcessPushWebhook;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
[EnableRateLimiting("webhook")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IMediator mediator, ILogger<WebhooksController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("github")]
    public async Task<IActionResult> HandleGitHubWebhook(CancellationToken ct)
    {
        var eventType = Request.Headers["X-GitHub-Event"].ToString();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var payload = JsonDocument.Parse(body);

        return eventType switch
        {
            "push" => await HandlePush(payload, ct),
            "pull_request" => await HandlePullRequest(payload, ct),
            _ => Ok(new { message = $"Event '{eventType}' ignored" })
        };
    }

    private async Task<IActionResult> HandlePush(JsonDocument payload, CancellationToken ct)
    {
        var root = payload.RootElement;
        var repo = root.GetProperty("repository").GetProperty("full_name").GetString()!;

        var commits = new List<PushCommitDto>();
        foreach (var commit in root.GetProperty("commits").EnumerateArray())
        {
            commits.Add(new PushCommitDto
            {
                Sha = commit.GetProperty("id").GetString()!,
                Message = commit.GetProperty("message").GetString()!,
                Timestamp = commit.GetProperty("timestamp").GetDateTime()
            });
        }

        _logger.LogInformation("Processing push webhook for {Repo} with {Count} commits",
            repo, commits.Count);

        var command = new ProcessPushWebhookCommand
        {
            Repo = repo,
            Commits = commits
        };

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { linksCreated = result.Value })
            : BadRequest(new { error = result.Error });
    }

    private async Task<IActionResult> HandlePullRequest(JsonDocument payload, CancellationToken ct)
    {
        var root = payload.RootElement;
        var action = root.GetProperty("action").GetString()!;

        if (action is not ("opened" or "synchronize" or "reopened"))
            return Ok(new { message = $"PR action '{action}' ignored" });

        var repo = root.GetProperty("repository").GetProperty("full_name").GetString()!;
        var prNumber = root.GetProperty("pull_request").GetProperty("number").GetInt32();

        _logger.LogInformation("Processing PR webhook for {Repo} PR #{PrNumber} action={Action}",
            repo, prNumber, action);

        var command = new ProcessPrWebhookCommand
        {
            Repo = repo,
            PrNumber = prNumber,
            Action = action
        };

        var result = await _mediator.Send(command, ct);
        return result.IsSuccess
            ? Ok(new { linksCreated = result.Value })
            : BadRequest(new { error = result.Error });
    }
}
