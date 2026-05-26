namespace DeveloperOverwatch.Infrastructure.Services;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class GitHubConfig
{
    public string BaseUrl { get; set; } = "https://api.github.com";
    public string? Token { get; set; }
}

public sealed class GitHubService : IGitHostingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GitHubService(HttpClient httpClient, ILogger<GitHubService> logger, IOptions<GitHubConfig> config)
    {
        _httpClient = httpClient;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(config.Value.BaseUrl);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeveloperOverwatch", "1.0"));

        if (!string.IsNullOrWhiteSpace(config.Value.Token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Value.Token);
    }

    public async Task<IReadOnlyList<string>> GetCommitFilesAsync(
        string repo, string commitSha, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/commits/{commitSha}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get commit {Sha} for {Repo}: {Status}",
                commitSha, repo, response.StatusCode);
            return [];
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var files = doc.RootElement.GetProperty("files");
        var result = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.TryGetProperty("filename", out var filename))
                result.Add(filename.GetString()!);
        }
        return result;
    }

    public async Task<IReadOnlyList<CommitInfo>> GetPullRequestCommitsAsync(
        string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/pulls/{prNumber}/commits", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get PR {PrNumber} commits for {Repo}: {Status}",
                prNumber, repo, response.StatusCode);
            return [];
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var result = new List<CommitInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var sha = item.GetProperty("sha").GetString()!;
            var message = item.GetProperty("commit").GetProperty("message").GetString()!;
            var date = item.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTime();
            result.Add(new CommitInfo(sha, message, date));
        }
        return result;
    }

    public async Task<PullRequestInfo?> GetPullRequestAsync(
        string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"/repos/{repo}/pulls/{prNumber}", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get PR {PrNumber} for {Repo}: {Status}",
                prNumber, repo, response.StatusCode);
            return null;
        }

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        return new PullRequestInfo(
            Number: root.GetProperty("number").GetInt32(),
            Title: root.GetProperty("title").GetString()!,
            AuthorEmail: root.GetProperty("user").GetProperty("login").GetString()!,
            HeadBranch: root.GetProperty("head").GetProperty("ref").GetString()!,
            BaseBranch: root.GetProperty("base").GetProperty("ref").GetString()!,
            State: root.GetProperty("state").GetString()!);
    }
}
