using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>What a command verb gets: the arguments after the verb, where to write, and the agent's config and clock.</summary>
public sealed record CliContext(
    IReadOnlyList<string> Args,
    TextWriter Output,
    TextWriter Error,
    AgentConfig Config,
    TimeProvider Time);
