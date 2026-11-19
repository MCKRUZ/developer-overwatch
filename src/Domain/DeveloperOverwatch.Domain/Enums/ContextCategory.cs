namespace DeveloperOverwatch.Domain.Enums;

/// <summary>What a share of a session's context window is spent on, as the Agent measures it from the
/// transcript. The Agent sends these names in camelCase.</summary>
public enum ContextCategory
{
    SystemPrompt,
    BuiltInTools,

    /// <summary>One MCP server's tool definitions; the row's server names it.</summary>
    McpTools,
    McpInstructions,
    CustomAgents,
    MemoryFiles,
    Skills,

    /// <summary>Everything else in the measured context: the messages themselves.</summary>
    Conversation,
}
