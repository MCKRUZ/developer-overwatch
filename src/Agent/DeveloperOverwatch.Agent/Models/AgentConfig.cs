using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeveloperOverwatch.Agent.Models;

public sealed class AgentConfig
{
    /// <summary>Pilot unless a config says otherwise: every install from before modes existed has a
    /// config file with no <c>mode</c> key, and must keep shipping.</summary>
    public AgentMode Mode { get; init; } = AgentMode.Pilot;
    public string ApiUrl { get; init; } = "http://localhost:5000";
    public string? ApiKey { get; init; }
    public int PollIntervalSeconds { get; init; } = 5;
    public int BatchSize { get; init; } = 50;
    public int GitFallbackThreshold { get; init; } = 100;
    public IReadOnlyList<string> RedactionPatterns { get; init; } = DefaultRedactionPatterns;
    public bool PrivateSession { get; init; }

    /// <summary>
    /// Optional replacement for the built-in price list, in the server's TokenPricing shape. Kept as raw
    /// JSON so a malformed block can only fail the usage command, never <see cref="Load"/>'s mode decision.
    /// </summary>
    public JsonElement? Pricing { get; init; }

    /// <summary>Optional <c>{ "dailyUsd": n, "monthlyUsd": n }</c>; raw for the same reason as <see cref="Pricing"/>.</summary>
    public JsonElement? Budget { get; init; }

    public static string DefaultOverwatchDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".overwatch");

    public string OverwatchDir { get; init; } = DefaultOverwatchDir;

    public string QueueDbPath => Path.Combine(OverwatchDir, "queue.db");
    public string ConfigPath => Path.Combine(OverwatchDir, "config.json");

    private static readonly JsonSerializerOptions LoadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>
    /// No config file means a machine nobody enrolled, so <see cref="AgentMode.Local"/>. A file
    /// without <c>mode</c> is an existing install, so <see cref="AgentMode.Pilot"/>. An explicit
    /// <c>mode</c> wins. A file that cannot be read fails closed to Local: when the agent cannot
    /// tell what the machine was enrolled as, nothing leaves it.
    /// </summary>
    public static AgentConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
            return new AgentConfig { Mode = AgentMode.Local };

        try
        {
            return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(configPath), LoadOptions)
                ?? new AgentConfig { Mode = AgentMode.Local };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AgentConfig { Mode = AgentMode.Local };
        }
    }

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
