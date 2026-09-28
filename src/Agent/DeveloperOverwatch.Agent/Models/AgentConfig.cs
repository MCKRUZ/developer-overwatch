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
    public string ConfigPath => Path.Combine(OverwatchDir, "config.json");

    private static readonly string[] DefaultRedactionPatterns =
    [
        // Separator is optional and may be whitespace, not just `=`/`:` — HTTP-header-style
        // secrets ("x-api-key <value>", "api key <value>") use a space, and requiring `=`/`:`
        // let those through uncaught. \b...\b on the keyword stops "auth" from matching inside
        // ordinary words like "authorization"/"author". The value must contain a digit and be
        // >=8 chars — real secrets/tokens are near-universally alphanumeric, whereas plain
        // English words following "auth"/"secret" (e.g. "auth alongside", "secrets-detection")
        // are not; a pure length check alone (tried first) still over-matched these badly.
        @"(?i)\b(api[\s_-]?key|tokens?|secrets?|passwords?|credentials?|auth)\b\s*[=:]?\s*(?=\S*\d)[A-Za-z0-9_\-\.\/+]{8,}",
        @"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*",
        @"sk-[A-Za-z0-9]{20,}",
        @"ghp_[A-Za-z0-9]{36}",
        @"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END",
    ];
}
