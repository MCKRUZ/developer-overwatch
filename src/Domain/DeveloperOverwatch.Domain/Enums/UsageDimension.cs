namespace DeveloperOverwatch.Domain.Enums;

/// <summary>What the usage meter splits a period's turns by.</summary>
public enum UsageDimension
{
    /// <summary>The turn's model.</summary>
    Model,

    /// <summary>The session's project key, or its repository path when the Agent sent no key.</summary>
    Project,

    /// <summary>The session the turn belongs to. Subagent turns are stored under their parent session.</summary>
    Session,

    /// <summary>The subagent that made the turn; no key means the main thread.</summary>
    Subagent,

    /// <summary>The git branch the turn was made on.</summary>
    Branch,

    /// <summary>The session's developer email, across all of their machines.</summary>
    Developer,

    /// <summary>The machine the session ran on.</summary>
    Machine,

    /// <summary>The coding agent that ran the session.</summary>
    Agent,
}
