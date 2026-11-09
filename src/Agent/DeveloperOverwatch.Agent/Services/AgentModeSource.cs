using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>The mode the machine is enrolled as right now, read fresh so a running agent sees a change.</summary>
public interface IAgentModeSource
{
    AgentMode CurrentMode();
}

/// <summary>Reads <c>config.json</c> with <see cref="AgentConfig.Load"/>, which fails closed to Local
/// when the file is missing or unreadable.</summary>
public sealed class ConfigFileModeSource(string configPath) : IAgentModeSource
{
    public AgentMode CurrentMode() => AgentConfig.Load(configPath).Mode;
}
