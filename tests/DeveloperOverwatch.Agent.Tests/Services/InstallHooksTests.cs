using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Models;
using FluentAssertions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Runs hooks/install-hooks.ps1 under pwsh against a temp USERPROFILE, so the real ~/.claude
/// and ~/.overwatch are never touched. Skipped (pass without running) when pwsh is not installed.</summary>
public sealed class InstallHooksTests : IDisposable
{
    private const string ApiUrl = "https://overwatch.example.com";

    private static readonly IReadOnlyDictionary<string, string> ExpectedHooks = new Dictionary<string, string>
    {
        ["SessionStart"] = "overwatch-session-start.ps1",
        ["PostToolUse"] = "overwatch-post-tool-use.ps1",
        ["PostToolUseFailure"] = "overwatch-tool-failure.ps1",
        ["UserPromptSubmit"] = "overwatch-prompt-submit.ps1",
        ["Notification"] = "overwatch-notification.ps1",
        ["Stop"] = "overwatch-stop.ps1",
        ["SessionEnd"] = "overwatch-session-end.ps1",
    };

    private readonly string _home = Path.Combine(Path.GetTempPath(), $"ow-install-{Guid.NewGuid():N}");

    public InstallHooksTests() => Directory.CreateDirectory(_home);

    public void Dispose()
    {
        try { Directory.Delete(_home, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    private string ConfigPath => Path.Combine(_home, ".overwatch", "config.json");
    private string SettingsPath => Path.Combine(_home, ".claude", "settings.json");
    private string BackupPath => SettingsPath + ".overwatch-backup";

    [Fact]
    public void FreshInstall_IsLocal_WithSevenAsyncHooks_AndNoServerAddress()
    {
        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        AgentConfig.Load(ConfigPath).Mode.Should().Be(AgentMode.Local);
        var config = ReadJson(ConfigPath);
        config["mode"]!.GetValue<string>().Should().Be("local");
        config.ContainsKey("apiUrl").Should().BeFalse();
        config.ContainsKey("apiKey").Should().BeFalse();

        var hooks = ReadJson(SettingsPath)["hooks"]!.AsObject();
        hooks.Select(h => h.Key).Should().BeEquivalentTo(ExpectedHooks.Keys);
        foreach (var (eventName, script) in ExpectedHooks)
        {
            var group = hooks[eventName]!.AsArray().Single()!.AsObject();
            group.ContainsKey("matcher").Should().BeFalse();
            var hook = group["hooks"]!.AsArray().Single()!.AsObject();
            hook["type"]!.GetValue<string>().Should().Be("command");
            hook["async"]!.GetValue<bool>().Should().BeTrue("a cold pwsh costs ~440 ms, which must not block Claude Code");
            hook["command"]!.GetValue<string>().Should().Contain(Path.Combine(Pwsh.HooksDir, script));
        }
        File.Exists(BackupPath).Should().BeFalse("there was no settings file to back up");
    }

    [Fact]
    public void ExistingUserHooksAndSettings_AreKept_AndTheOriginalIsBackedUp()
    {
        const string original = """
            {
              "model": "opus",
              "permissions": { "allow": ["Bash(git status)"] },
              "hooks": {
                "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "my-guard.sh" } ] } ],
                "Stop": [ { "hooks": [
                  { "type": "command", "command": "notify-me.sh" },
                  { "type": "command", "command": "pwsh -NoProfile -File \"C:\\old\\hooks\\overwatch-stop.ps1\"" }
                ] } ]
              }
            }
            """;
        WriteFile(SettingsPath, original);

        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        var settings = ReadJson(SettingsPath);
        settings["model"]!.GetValue<string>().Should().Be("opus");
        settings["permissions"]!["allow"]!.AsArray().Single()!.GetValue<string>().Should().Be("Bash(git status)");
        var hooks = settings["hooks"]!.AsObject();
        Commands(hooks, "PreToolUse").Should().Equal("my-guard.sh");
        hooks["PreToolUse"]![0]!["matcher"]!.GetValue<string>().Should().Be("Bash");
        var stop = Commands(hooks, "Stop");
        stop.Should().HaveCount(2, "the old Overwatch entry is replaced, not kept beside the new one");
        stop[0].Should().Be("notify-me.sh");
        stop[1].Should().Contain(Path.Combine(Pwsh.HooksDir, "overwatch-stop.ps1"));
        File.ReadAllText(BackupPath).Should().Be(original);
    }

    [Fact]
    public void RunningTwice_AddsNoDuplicates_AndKeepsTheFirstBackup()
    {
        const string original = """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "notify-me.sh" } ] } ] } }""";
        WriteFile(SettingsPath, original);

        if (Install() is null) return;
        var second = Install()!;

        second.ExitCode.Should().Be(0, second.Stderr);
        var hooks = ReadJson(SettingsPath)["hooks"]!.AsObject();
        hooks.SelectMany(h => Commands(hooks, h.Key)).Count(c => c.Contains("overwatch-")).Should().Be(7);
        Commands(hooks, "Stop").Should().HaveCount(2).And.Contain("notify-me.sh");
        File.ReadAllText(BackupPath).Should().Be(original, "the backup keeps the file as it was before Overwatch first changed it");
    }

    [Fact]
    public void Pilot_WithoutApiUrl_FailsWithAClearMessage_AndWritesNothing()
    {
        if (Install("-Pilot") is not { } result) return;

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("-ApiUrl");
        File.Exists(ConfigPath).Should().BeFalse();
        File.Exists(SettingsPath).Should().BeFalse();
    }

    [Fact]
    public void Pilot_WithApiUrl_WritesPilotModeAndTheAddress()
    {
        if (Install("-Pilot", "-ApiUrl", ApiUrl) is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        AgentConfig.Load(ConfigPath).Should().BeEquivalentTo(new { Mode = AgentMode.Pilot, ApiUrl });
    }

    [Fact]
    public void ExistingConfigWithoutMode_StaysPilot_AndKeepsItsOtherKeys()
    {
        // What the installer wrote before modes existed, plus a block a user added by hand.
        WriteFile(ConfigPath, $$"""{ "apiUrl": "{{ApiUrl}}", "apiKey": "test-key", "pollIntervalSeconds": 9, "budget": { "dailyUsd": 5 } }""");

        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        AgentConfig.Load(ConfigPath).Mode.Should().Be(AgentMode.Pilot);
        var config = ReadJson(ConfigPath);
        config["mode"]!.GetValue<string>().Should().Be("pilot");
        config["apiUrl"]!.GetValue<string>().Should().Be(ApiUrl);
        config["apiKey"]!.GetValue<string>().Should().Be("test-key");
        config["pollIntervalSeconds"]!.GetValue<int>().Should().Be(9);
        config["budget"]!["dailyUsd"]!.GetValue<int>().Should().Be(5);
        config["batchSize"]!.GetValue<int>().Should().Be(50);
    }

    [Fact]
    public void Local_OnAPilotConfig_SwitchesMode_AndDropsTheServerAddressAndKey()
    {
        WriteFile(ConfigPath, $$"""{ "mode": "pilot", "apiUrl": "{{ApiUrl}}", "apiKey": "test-key" }""");

        if (Install("-Local") is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        AgentConfig.Load(ConfigPath).Mode.Should().Be(AgentMode.Local);
        var config = ReadJson(ConfigPath);
        config.ContainsKey("apiUrl").Should().BeFalse();
        config.ContainsKey("apiKey").Should().BeFalse();
    }

    /// <summary>Always passes -NoStart: without it the script registers a real scheduled task and starts
    /// the real agent.</summary>
    private PwshResult? Install(params string[] args) =>
        Pwsh.Start("install-hooks.ps1", _home, [.. args, "-NoStart"]) is { } process
            ? Pwsh.Finish(process, stdin: "")
            : null;

    private static List<string> Commands(JsonObject hooks, string eventName) =>
        hooks[eventName]!.AsArray()
            .SelectMany(group => group!["hooks"]!.AsArray())
            .Select(hook => hook!["command"]!.GetValue<string>())
            .ToList();

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
