using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The hooks see the user's prompt, tool inputs and error text. The new hooks must never
/// read or forward any of it. The pwsh runs prove it end to end and are skipped (pass without
/// running) when pwsh is not installed.</summary>
public sealed class HookScriptTests
{
    private static readonly string[] NewHooks =
        ["overwatch-prompt-submit.ps1", "overwatch-notification.ps1", "overwatch-tool-failure.ps1"];

    private const string Sentinel = "SENTINEL-PRIVATE";

    private static readonly string HooksDir = FindHooksDir();

    private static string FindHooksDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var hooks = Path.Combine(dir.FullName, "hooks");
            if (File.Exists(Path.Combine(hooks, "overwatch-stop.ps1")))
                return hooks;
        }
        throw new InvalidOperationException("hooks directory not found above the test output");
    }

    public static TheoryData<string> NewHookNames() => new(NewHooks);

    [Theory]
    [MemberData(nameof(NewHookNames))]
    public void NewHook_NeverReadsPromptErrorOrToolInput(string hook)
    {
        var script = File.ReadAllText(Path.Combine(HooksDir, hook));

        Regex.IsMatch(script, @"\.(prompt|error|tool_input)\b").Should().BeFalse();
        script.Split('\n').Where(l => l.Contains("ConvertTo-Json") || l.Contains("Add-Content"))
            .Should().NotContain(l => l.Contains("$hookInput") || l.Contains("$stdinJson") || l.Contains("ReadToEnd"),
                "the raw hook input must never be serialized or written out");
    }

    [Fact]
    public void AllHooks_WriteOnlyToTheJsonlFile()
    {
        var hooks = Directory.GetFiles(HooksDir, "overwatch-*.ps1");

        hooks.Should().HaveCount(7);
        hooks.Should().NotContain(h => File.ReadAllText(h).Contains("SqliteConnection"),
            "pwsh cannot load the SQLite library, so that path never worked");
    }

    [Fact]
    public void ToolFailureHook_RunInPwsh_WritesOnlyToolNameAndTime()
    {
        var line = RunHookAndReadLine("overwatch-tool-failure.ps1", new
        {
            session_id = "claude-1",
            hook_event_name = "PostToolUseFailure",
            tool_name = "Bash",
            tool_input = new { command = Sentinel },
            tool_use_id = "toolu_1",
            error = Sentinel,
            is_interrupt = false
        });
        if (line is null) return;

        line.Should().NotContain(Sentinel);
        using var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("eventType").GetString().Should().Be("ToolFailed");
        doc.RootElement.GetProperty("sessionId").GetString().Should().Be("ow-1");
        using var payload = JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetString()!);
        payload.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("toolName", "at");
        payload.RootElement.GetProperty("toolName").GetString().Should().Be("Bash");
    }

    [Fact]
    public void PromptSubmitHook_RunInPwsh_WritesActiveWithoutThePrompt()
    {
        var line = RunHookAndReadLine("overwatch-prompt-submit.ps1",
            new { session_id = "claude-1", hook_event_name = "UserPromptSubmit", prompt = Sentinel });
        if (line is null) return;

        line.Should().NotContain(Sentinel);
        using var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("eventType").GetString().Should().Be("ActivityChanged");
        using var payload = JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetString()!);
        payload.RootElement.GetProperty("state").GetString().Should().Be("active");
    }

    [Theory]
    [InlineData("permission_prompt", "waiting")]
    [InlineData("agent_needs_input", "waiting")]
    [InlineData("idle_prompt", "idle")]
    [InlineData("auth_success", null)]
    public void NotificationHook_RunInPwsh_MapsNotificationType(string notificationType, string? expectedState)
    {
        var home = CreateHome();
        var result = RunHook("overwatch-notification.ps1", home,
            new { session_id = "claude-1", hook_event_name = "Notification", notification_type = notificationType, message = Sentinel });
        if (result is null) return;

        var lines = ReadLines(home);
        if (expectedState is null)
        {
            lines.Should().BeEmpty();
            return;
        }
        using var doc = JsonDocument.Parse(lines.Single());
        using var payload = JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetString()!);
        payload.RootElement.GetProperty("state").GetString().Should().Be(expectedState);
    }

    private static string? RunHookAndReadLine(string hook, object input)
    {
        var home = CreateHome();
        return RunHook(hook, home, input) is null ? null : ReadLines(home).Single();
    }

    private static string CreateHome()
    {
        var home = Path.Combine(Path.GetTempPath(), $"ow-hook-{Guid.NewGuid():N}");
        var sessions = Path.Combine(home, ".overwatch", "sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(Path.Combine(sessions, "claude-1.json"), "{\"ourSessionId\":\"ow-1\",\"repoPath\":null}");
        return home;
    }

    private static string[] ReadLines(string home)
    {
        var path = Path.Combine(home, ".overwatch", "queue-fallback.jsonl");
        return File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToArray() : [];
    }

    /// <summary>Runs the hook the way Claude Code does, with hook JSON on stdin. Null when pwsh is
    /// not installed. Asserts the async-hook contract: exit 0 and nothing on stdout.</summary>
    private static string? RunHook(string hook, string home, object input)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(HooksDir, hook) })
            psi.ArgumentList.Add(arg);
        psi.Environment["USERPROFILE"] = home;

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception)
        {
            return null;
        }

        using (process)
        {
            process.StandardInput.Write(JsonSerializer.Serialize(input));
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(30_000).Should().BeTrue();

            process.ExitCode.Should().Be(0);
            stdout.Should().BeEmpty("async hooks' stdout is parsed by Claude Code");
            return stdout;
        }
    }
}
