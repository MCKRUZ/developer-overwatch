namespace DeveloperOverwatch.Agent.Models;

public sealed class AgentConfig
{
    public string ApiUrl { get; init; } = "http://localhost:5000";
    public string? ApiKey { get; init; }
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;
    public int GitFallbackThreshold { get; init; } = 100;
    public IReadOnlyList<string> RedactionPatterns { get; init; } = DefaultRedactionPatterns;
    public bool PrivateSession { get; init; }

    public string OverwatchDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".overwatch");

    public string QueueDbPath => Path.Combine(OverwatchDir, "queue.db");
    public string ActiveSessionPath => Path.Combine(OverwatchDir, "active-session");
    public string ConfigPath => Path.Combine(OverwatchDir, "config.json");

    private static readonly string[] DefaultRedactionPatterns =
    [
        @"(?i)(api[_-]?key|token|secret|password|credential|auth)\s*[=:]\s*\S+",
        @"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*",
        @"sk-[A-Za-z0-9]{20,}",
        @"ghp_[A-Za-z0-9]{36}",
        @"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END",
    ];
}
