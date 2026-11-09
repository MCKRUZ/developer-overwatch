using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>The mode the machine is enrolled as right now, read fresh so a running agent sees a change.</summary>
public interface IAgentModeSource
{
    /// <summary>Null when the config cannot be read or parsed right now (locked, half-saved), which says
    /// nothing about the mode. A missing config is a definite answer: Local.</summary>
    AgentMode? CurrentMode();
}

/// <summary>Reads <c>config.json</c> with <see cref="AgentConfig.TryLoad"/>.</summary>
public sealed class ConfigFileModeSource(string configPath) : IAgentModeSource
{
    public AgentMode? CurrentMode() => AgentConfig.TryLoad(configPath)?.Mode;
}
