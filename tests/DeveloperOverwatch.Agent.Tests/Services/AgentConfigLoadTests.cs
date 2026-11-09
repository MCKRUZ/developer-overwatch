using DeveloperOverwatch.Agent.Models;
using FluentAssertions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class AgentConfigLoadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-config-{Guid.NewGuid():N}");
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public AgentConfigLoadTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    // The hooks always write to ~/.overwatch, so a config that moved the agent elsewhere would make it
    // ignore every hook event.
    [Fact]
    public void Load_OverwatchDirKey_IsIgnored()
    {
        File.WriteAllText(ConfigPath, """{"mode":"Pilot","overwatchDir":"C:/somewhere-else"}""");

        AgentConfig.Load(ConfigPath).OverwatchDir.Should().Be(AgentConfig.DefaultOverwatchDir);
    }

    // A machine nobody enrolled has no config file; its usage must stay on the machine.
    [Fact]
    public void Load_NoConfigFile_IsLocal() =>
        AgentConfig.Load(ConfigPath).Mode.Should().Be(AgentMode.Local);

    // Every install that predates modes has a config file without `mode`; those are the pilot
    // and must keep shipping exactly as before.
    [Fact]
    public void Load_FileWithoutMode_IsPilotAndKeepsSettings()
    {
        File.WriteAllText(ConfigPath, """{"apiUrl":"https://overwatch.example.com","batchSize":7}""");

        var config = AgentConfig.Load(ConfigPath);

        config.Mode.Should().Be(AgentMode.Pilot);
        config.ApiUrl.Should().Be("https://overwatch.example.com");
        config.BatchSize.Should().Be(7);
    }

    [Theory]
    [InlineData("""{"mode":"local","apiUrl":"https://overwatch.example.com"}""", AgentMode.Local)]
    [InlineData("""{"Mode":"LOCAL"}""", AgentMode.Local)]
    [InlineData("""{"mode":"pilot"}""", AgentMode.Pilot)]
    public void Load_ExplicitMode_Wins(string json, AgentMode expected)
    {
        File.WriteAllText(ConfigPath, json);

        AgentConfig.Load(ConfigPath).Mode.Should().Be(expected);
    }

    // When the agent cannot tell what the machine was enrolled as, it fails closed: nothing leaves.
    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"mode":"lokal"}""")]
    public void Load_UnreadableConfig_IsLocal(string json)
    {
        File.WriteAllText(ConfigPath, json);

        AgentConfig.Load(ConfigPath).Mode.Should().Be(AgentMode.Local);
    }

    // The running agent must tell "unreadable right now" apart from "Local", or a half-saved file stops it.
    [Fact]
    public void TryLoad_UnparseableConfig_IsNull()
    {
        File.WriteAllText(ConfigPath, "{not json");

        AgentConfig.TryLoad(ConfigPath).Should().BeNull();
    }

    [Fact]
    public void TryLoad_NoConfigFile_IsLocal() =>
        AgentConfig.TryLoad(ConfigPath)!.Mode.Should().Be(AgentMode.Local);

    // The server takes at most 500 items per list, and a batch of 0 would never drain the queue.
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(10000, 500)]
    [InlineData(7, 7)]
    public void Load_BatchSize_IsClampedTo1Through500(int configured, int expected)
    {
        File.WriteAllText(ConfigPath, $$"""{"mode":"Pilot","batchSize":{{configured}}}""");

        AgentConfig.Load(ConfigPath).BatchSize.Should().Be(expected);
    }
}
