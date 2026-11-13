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
    private const string Vault = "test-kv";
    private const string VaultKey = "vault-key";

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
    private string OriginalBackupPath => BackupPath + ".original";
    private string FakeBinDir => Path.Combine(_home, "fake-bin");
    private string AzArgsPath => Path.Combine(_home, "az-args.txt");

    private const string RestartWarning = "[!] Agent not restarted";

    [Fact]
    public void FreshInstall_IsLocal_WithSessionStartSyncAndSixAsyncHooks_AndNoServerAddress()
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
            if (eventName == "SessionStart")
                hook.ContainsKey("async").Should().BeFalse(
                    "every other hook needs the mapping SessionStart writes, so it must finish before they run");
            else
                hook["async"]!.GetValue<bool>().Should().BeTrue("a cold pwsh costs ~440 ms, which must not block Claude Code");
            hook["command"]!.GetValue<string>().Should().Contain(Path.Combine(Pwsh.HooksDir, script));
        }
        File.Exists(BackupPath).Should().BeFalse("there was no settings file to back up");
        File.Exists(OriginalBackupPath).Should().BeFalse();
        result.Stdout.Should().NotContain(RestartWarning, "a fresh install has no agent holding an old config");
    }

    [Fact]
    public void UserHookWhoseNameStartsWithOverwatch_IsKept()
    {
        const string userHook = "pwsh -File \"C:\\tools\\overwatch-lint.ps1\"";
        WriteFile(SettingsPath, $$"""
            { "hooks": {
                "Stop": [ { "hooks": [ { "type": "command", "command": {{System.Text.Json.JsonSerializer.Serialize(userHook)}} } ] } ],
                "PreToolUse": [ { "hooks": [ { "type": "command", "command": "C:\\tools\\overwatch-lint.ps1" } ] } ]
            } }
            """);

        if (Install() is not { } first) return;
        var second = Install()!;

        first.ExitCode.Should().Be(0, first.Stderr);
        second.ExitCode.Should().Be(0, second.Stderr);
        var hooks = ReadJson(SettingsPath)["hooks"]!.AsObject();
        Commands(hooks, "PreToolUse").Should().Equal(@"C:\tools\overwatch-lint.ps1");
        var stop = Commands(hooks, "Stop");
        stop.Should().HaveCount(2);
        stop[0].Should().Be(userHook, "only the scripts this installer registers are its own");
    }

    [Fact]
    public void DateStringsInSettings_ComeBackUnchanged_TheOriginalBackupIsKept_AndNoTempFileIsLeft()
    {
        const string original = """{ "lastChecked": "2026-01-02T03:04:05.123+02:00", "model": "opus" }""";
        WriteFile(SettingsPath, original);

        if (Install() is null) return;
        var second = Install()!;

        second.ExitCode.Should().Be(0, second.Stderr);
        File.ReadAllText(SettingsPath).Should().Contain("\"2026-01-02T03:04:05.123+02:00\"",
            "a user's date string must not be parsed into a date and written back reformatted");
        File.ReadAllText(BackupPath).Should().Contain("\"2026-01-02T03:04:05.123+02:00\"");
        File.ReadAllText(OriginalBackupPath).Should().Be(original);
        Directory.GetFiles(_home, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void OldSingleBackup_IsPromotedToTheOriginal_BeforeBeingOverwritten()
    {
        // Before .original existed, the one backup was written once and held the pre-Overwatch file.
        const string preOverwatch = """{ "model": "sonnet" }""";
        WriteFile(BackupPath, preOverwatch);
        WriteFile(SettingsPath, """{ "model": "opus" }""");

        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        File.ReadAllText(OriginalBackupPath).Should().Be(preOverwatch);
        File.ReadAllText(BackupPath).Should().Be("""{ "model": "opus" }""");
    }

    [Theory]
    [InlineData("""{ "mode": "pilot", "apiUrl": "https://overwatch.example.com", "apiKey": "k" }""", "-Local")]
    [InlineData("""{ "apiUrl": "https://overwatch.example.com" }""", "-Local")]
    [InlineData("""{ "mode": "local" }""", "-Pilot -ApiUrl https://overwatch.example.com -KeyVault test-kv")]
    [InlineData("""{ "mode": "pilot", "apiUrl": "https://overwatch.example.com", "apiKey": "old" }""", "-Pilot -KeyVault test-kv")]
    [InlineData("""{ "mode": "pilot", "apiUrl": "https://old.example.com", "apiKey": "k" }""", "-Pilot -ApiUrl https://overwatch.example.com")]
    public void ChangingModeAddressOrKey_NeedsARestart(string existingConfig, string args)
    {
        WriteFile(ConfigPath, existingConfig);

        if (Install(args.Split(' ')) is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain(RestartWarning,
            "a running agent keeps its old config in memory, so a pilot agent would keep shipping after -Local");
    }

    [Theory]
    [InlineData("""{ "mode": "local" }""", "-Local")]
    [InlineData("""{ "mode": "local" }""", "")]
    [InlineData("""{ "mode": "pilot", "apiUrl": "https://overwatch.example.com", "apiKey": "k" }""", "-Pilot")]
    [InlineData("""{ "mode": "pilot", "apiUrl": "https://overwatch.example.com", "apiKey": "vault-key" }""", "-Pilot -ApiUrl https://overwatch.example.com -KeyVault test-kv")]
    public void SameModeRerun_NeedsNoRestart(string existingConfig, string args)
    {
        WriteFile(ConfigPath, existingConfig);

        if (Install(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)) is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().NotContain(RestartWarning);
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
        File.ReadAllText(OriginalBackupPath).Should().Be(original);
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
        File.ReadAllText(OriginalBackupPath).Should().Be(original,
            "the original backup keeps the file as it was before Overwatch first changed it");
        File.ReadAllText(BackupPath).Should().NotBe(original, "the rolling backup holds the file as this run found it");
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
    public void Pilot_WithApiUrlAndKeyVault_WritesPilotModeTheAddressAndTheVaultKey()
    {
        if (Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault) is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        AgentConfig.Load(ConfigPath).Should().BeEquivalentTo(new { Mode = AgentMode.Pilot, ApiUrl, ApiKey = VaultKey });
        File.ReadAllText(AzArgsPath).Should().Contain($"keyvault secret show --vault-name {Vault} --name agent-api-key");
    }

    // The key reaches every developer machine; it must not end up in a terminal, a log or a screenshot.
    [Fact]
    public void Pilot_WithKeyVault_NeverPrintsTheKey()
    {
        if (Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault) is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().NotContain(VaultKey);
        result.Stderr.Should().NotContain(VaultKey);
    }

    // A pilot agent with no key is refused by the server on every send, so the install must not look successful.
    [Fact]
    public void Pilot_FreshWithoutKeyVault_FailsWithAClearMessage_AndWritesNothing()
    {
        if (Install("-Pilot", "-ApiUrl", ApiUrl) is not { } result) return;

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("-KeyVault");
        File.Exists(ConfigPath).Should().BeFalse();
        File.Exists(SettingsPath).Should().BeFalse();
    }

    [Fact]
    public void Pilot_KeyVaultReadFails_FailsNamingTheVaultAndSecret_AndWritesNothing()
    {
        WriteFakeAz(stdout: "", stderr: "ERROR: Caller is not authorized to perform action", exitCode: 1);

        if (Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault) is not { } result) return;

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain(Vault).And.Contain("agent-api-key").And.Contain("not authorized");
        File.Exists(ConfigPath).Should().BeFalse();
        File.Exists(SettingsPath).Should().BeFalse();
    }

    // An empty reply must never be saved as the key, whatever az's exit code says.
    [Fact]
    public void Pilot_KeyVaultReturnsNothing_Fails_AndWritesNothing()
    {
        WriteFakeAz(stdout: "", stderr: "", exitCode: 0);

        if (Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault) is not { } result) return;

        result.ExitCode.Should().NotBe(0);
        File.Exists(ConfigPath).Should().BeFalse();
    }

    [Fact]
    public void Local_WithKeyVault_FailsBecauseLocalModeNeverShips()
    {
        if (Install("-Local", "-KeyVault", Vault) is not { } result) return;

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("-KeyVault");
        File.Exists(AzArgsPath).Should().BeFalse("local mode must not read the key at all");
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

    // A dotfiles setup symlinks settings.json; replacing the link with a plain file would detach it.
    // Skipped (passes) where creating a symlink needs admin rights, i.e. Windows without Developer Mode.
    [Fact]
    public void SymlinkedSettings_StayALink_AndTheTargetGetsTheHooks()
    {
        var target = Path.Combine(_home, "dotfiles", "claude-settings.json");
        WriteFile(target, """{"theme":"dark"}""");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        try
        {
            File.CreateSymbolicLink(SettingsPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        new FileInfo(SettingsPath).LinkTarget.Should().NotBeNull("the link must survive");
        var settings = ReadJson(target);
        settings["theme"]!.GetValue<string>().Should().Be("dark");
        Commands(settings["hooks"]!.AsObject(), "SessionEnd").Should().ContainSingle(c => c.Contains("overwatch-session-end.ps1"));
        File.Exists(target + ".tmp").Should().BeFalse();
    }

    // A write that fails after the agent was stopped must say what happened, not dump a raw exception.
    [Fact]
    public void WriteFails_PrintsAClearMessage_NotARawException()
    {
        Directory.CreateDirectory(ConfigPath + ".tmp");

        if (Install() is not { } result) return;

        result.ExitCode.Should().Be(1);
        result.Stderr.Should().Contain("[x]").And.Contain(ConfigPath).And.NotContain("At line:");
        File.Exists(SettingsPath).Should().BeFalse("nothing after the failed write runs");
    }

    // pilotSince is the enrolment cutoff: the agent never ships an event queued before it.
    [Fact]
    public void PilotSince_FreshPilotAndLocalToPilotWriteIt_PilotRerunKeepsIt_LocalRemovesIt()
    {
        if (Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault) is not { } fresh) return;
        fresh.ExitCode.Should().Be(0, fresh.Stderr);
        var written = ReadJson(ConfigPath)["pilotSince"]!.GetValue<string>();
        AgentConfig.Load(ConfigPath).PilotSince.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        Install("-Pilot")!.ExitCode.Should().Be(0);
        ReadJson(ConfigPath)["pilotSince"]!.GetValue<string>().Should().Be(written, "a re-run is not a new enrolment");

        Install("-Local")!.ExitCode.Should().Be(0);
        ReadJson(ConfigPath).ContainsKey("pilotSince").Should().BeFalse();

        Install("-Pilot", "-ApiUrl", ApiUrl, "-KeyVault", Vault)!.ExitCode.Should().Be(0);
        ReadJson(ConfigPath).ContainsKey("pilotSince").Should().BeTrue("Local to Pilot is a new enrolment");
    }

    // An install from before the cutoff existed keeps its whole queue: no pilotSince is invented for it.
    [Fact]
    public void PilotSince_LegacyPilotConfigRerun_StaysAbsent_AndTheRetiredFallbackKeyIsRemoved()
    {
        WriteFile(ConfigPath, $$"""{"apiUrl":"{{ApiUrl}}","apiKey":"k","gitFallbackThreshold":100}""");

        if (Install("-Pilot") is not { } result) return;

        result.ExitCode.Should().Be(0, result.Stderr);
        var config = ReadJson(ConfigPath);
        config.ContainsKey("pilotSince").Should().BeFalse();
        config.ContainsKey("gitFallbackThreshold").Should().BeFalse();
    }

    /// <summary>Always passes -NoStart and -NoProcessControl: without them the script registers a real
    /// scheduled task, and stops and starts the real agent.</summary>
    /// A stand-in Azure CLI is first on PATH, so -KeyVault never reaches the real az or a real vault.
    private PwshResult? Install(params string[] args)
    {
        if (!File.Exists(Path.Combine(FakeBinDir, "az")))
            WriteFakeAz(stdout: VaultKey, stderr: "", exitCode: 0);
        return Pwsh.Start("install-hooks.ps1", _home, FakeBinDir, [.. args, "-NoStart", "-NoProcessControl"]) is { } process
            ? Pwsh.Finish(process, stdin: "")
            : null;
    }

    /// <summary>Writes an az that records its arguments, prints <paramref name="stdout"/> and
    /// <paramref name="stderr"/>, and exits with <paramref name="exitCode"/>: az.cmd for Windows, a shell
    /// script for Linux (CI), since a real native command is what the installer runs.</summary>
    private void WriteFakeAz(string stdout, string stderr, int exitCode)
    {
        Directory.CreateDirectory(FakeBinDir);

        File.WriteAllText(Path.Combine(FakeBinDir, "az.cmd"),
            "@echo off\r\n" +
            $"echo %*> \"{AzArgsPath}\"\r\n" +
            (stdout.Length == 0 ? "" : $"echo {stdout}\r\n") +
            (stderr.Length == 0 ? "" : $"echo {stderr} 1>&2\r\n") +
            $"exit /b {exitCode}\r\n");

        var shell = Path.Combine(FakeBinDir, "az");
        File.WriteAllText(shell,
            "#!/bin/sh\n" +
            $"echo \"$@\" > '{AzArgsPath}'\n" +
            (stdout.Length == 0 ? "" : $"echo '{stdout}'\n") +
            (stderr.Length == 0 ? "" : $"echo '{stderr}' 1>&2\n") +
            $"exit {exitCode}\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

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
