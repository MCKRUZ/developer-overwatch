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

    private const string SharedHelper = "overwatch-common.ps1";

    private const string Sentinel = "SENTINEL-PRIVATE";

    public static TheoryData<string> NewHookNames() => new(NewHooks);

    [Theory]
    [MemberData(nameof(NewHookNames))]
    public void NewHook_NeverReadsPromptErrorOrToolInput(string hook)
    {
        var script = File.ReadAllText(Path.Combine(Pwsh.HooksDir, hook));

        Regex.IsMatch(script, @"\.(prompt|error|tool_input)\b").Should().BeFalse();
        script.Split('\n')
            .Where(l => l.Contains("ConvertTo-Json") || l.Contains("Add-Content") || l.Contains("Add-OverwatchLine"))
            .Should().NotContain(l => l.Contains("$hookInput") || l.Contains("$stdinJson") || l.Contains("ReadToEnd"),
                "the raw hook input must never be serialized or written out");
    }

    [Fact]
    public void AllHooks_WriteOnlyToTheJsonlFile_ThroughTheSharedWriter()
    {
        var hooks = Directory.GetFiles(Pwsh.HooksDir, "overwatch-*.ps1")
            .Where(h => Path.GetFileName(h) != SharedHelper)
            .ToList();

        hooks.Should().HaveCount(7);
        hooks.Should().NotContain(h => File.ReadAllText(h).Contains("SqliteConnection"),
            "pwsh cannot load the SQLite library, so that path never worked");
        hooks.Should().NotContain(h => File.ReadAllText(h).Contains("Add-Content"),
            "Add-Content fails outright when another hook holds the file, and the event is lost");
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
    public void ToolFailureHook_SeveralRunningAtOnce_EveryLineLands()
    {
        // Async hooks run in parallel. An append that loses the race for the file must retry, not drop its event.
        const int count = 4;
        var home = CreateHome();
        var processes = new List<Process>();
        for (var i = 0; i < count; i++)
        {
            if (Pwsh.Start("overwatch-tool-failure.ps1", home) is not { } process) return;
            processes.Add(process);
        }

        // Each one is started and waiting on stdin, so feeding them back to back makes their appends overlap.
        Thread.Sleep(1500);
        var input = JsonSerializer.Serialize(new { session_id = "claude-1", hook_event_name = "PostToolUseFailure", tool_name = "Bash" });
        foreach (var process in processes)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        foreach (var process in processes)
            Pwsh.Finish(process, stdin: null).ExitCode.Should().Be(0);

        var lines = ReadLines(home);
        lines.Should().HaveCount(count);
        lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("id").GetString())
            .Should().OnlyHaveUniqueItems();
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

    public static TheoryData<string> AllHookNames() => new(
        "overwatch-session-start.ps1", "overwatch-post-tool-use.ps1", "overwatch-tool-failure.ps1",
        "overwatch-prompt-submit.ps1", "overwatch-notification.ps1", "overwatch-stop.ps1", "overwatch-session-end.ps1");

    [Theory]
    [MemberData(nameof(AllHookNames))]
    public void Hook_SessionIdThatLeavesTheSessionsFolder_WritesNothing(string hook)
    {
        // Planted where sessions\..\x.json resolves: without the check, session-start overwrites it and
        // the other hooks read it as a mapping and queue an event.
        var home = CreateHome();
        var planted = Path.Combine(home, ".overwatch", "x.json");
        const string plantedContent = "{\"ourSessionId\":\"planted\",\"repoPath\":null}";
        File.WriteAllText(planted, plantedContent);

        var result = RunHook(hook, home, new
        {
            session_id = "../x",
            hook_event_name = "Test",
            tool_name = "Bash",
            notification_type = "permission_prompt",
            transcript_path = Path.Combine(home, "missing.jsonl")
        });
        if (result is null) return;

        File.ReadAllText(planted).Should().Be(plantedContent);
        ReadLines(home).Should().BeEmpty();
    }

    [Fact]
    public void SessionStartHook_DatesItsEventBeforeWritingTheMapping()
    {
        // Other hooks queue events as soon as the mapping exists, so the start must be dated first.
        var script = File.ReadAllText(Path.Combine(Pwsh.HooksDir, "overwatch-session-start.ps1"));

        var dated = script.IndexOf("$now = ", StringComparison.Ordinal);
        var mapped = script.IndexOf("Set-Content $mappingPath", StringComparison.Ordinal);
        dated.Should().BePositive();
        mapped.Should().BeGreaterThan(dated);
        script.Should().Contain("startedAt = $now");
    }

    // The agent keeps a pre-enrolment session local across a compact or clear start, so it needs the source.
    // Only a missing source reads as an old hook; any other source the agent cannot use arrives as "unknown".
    [Theory]
    [InlineData("compact", "compact")]
    [InlineData("RESUME", "resume")]
    [InlineData("new_source", "new_source")]
    [InlineData("not-a-word", "unknown")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg", "unknown")]
    [InlineData("", "unknown")]
    [InlineData(42, "unknown")]
    [InlineData(null, null)]
    public void SessionStartHook_RunInPwsh_ForwardsOnlyAKnownSource(object? source, string? expected)
    {
        var home = CreateHome();
        var line = RunHook("overwatch-session-start.ps1", home,
            new { session_id = "claude-1", source, transcript_path = Path.Combine(home, "missing.jsonl") }) is null
            ? null
            : ReadLines(home).Single();
        if (line is null) return;

        using var doc = JsonDocument.Parse(line);
        using var payload = JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetString()!);
        var sent = payload.RootElement.TryGetProperty("source", out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
        sent.Should().Be(expected);
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
        if (Pwsh.Start(hook, home) is not { } process)
            return null;

        var result = Pwsh.Finish(process, JsonSerializer.Serialize(input));
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().BeEmpty("async hooks' stdout is parsed by Claude Code");
        return result.Stdout;
    }
}
