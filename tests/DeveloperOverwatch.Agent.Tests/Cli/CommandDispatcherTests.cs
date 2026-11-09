using DeveloperOverwatch.Agent.Cli;
using DeveloperOverwatch.Agent.Models;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Cli;

public sealed class CommandDispatcherTests
{
    // Never read: both cases are rejected before any store opens.
    private static readonly AgentConfig Config = new()
    {
        Mode = AgentMode.Local,
        OverwatchDir = Path.Combine(Path.GetTempPath(), $"ow-dispatch-{Guid.NewGuid():N}"),
    };

    [Fact]
    public async Task RunAsync_UnknownVerb_PrintsHelpAndReturnsTwo()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CommandDispatcher.RunAsync(["bogus"], output, error, Config, TimeProvider.System);

        exit.Should().Be(2);
        error.ToString().Should().Contain("Unknown command 'bogus'.")
            .And.Contain("Usage: overwatch-agent [command]")
            .And.Contain("usage");
        output.ToString().Should().BeEmpty();
        Directory.Exists(Config.OverwatchDir).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_UsageWithAnArgument_ReturnsTwo()
    {
        var error = new StringWriter();

        var exit = await CommandDispatcher.RunAsync(["usage", "--all"], new StringWriter(), error, Config, TimeProvider.System);

        exit.Should().Be(2);
        error.ToString().Should().Contain("takes no arguments");
        Directory.Exists(Config.OverwatchDir).Should().BeFalse();
    }
}
