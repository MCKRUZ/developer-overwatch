namespace DeveloperOverwatch.Agent.Models;

/// <summary>Whether this machine's usage leaves it.</summary>
public enum AgentMode
{
    /// <summary>Not in the pilot: usage is kept on the machine only, and nothing is sent anywhere.</summary>
    Local,

    /// <summary>In the pilot: events and usage are shipped to the Overwatch server.</summary>
    Pilot
}
