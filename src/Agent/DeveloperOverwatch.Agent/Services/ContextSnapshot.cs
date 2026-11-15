namespace DeveloperOverwatch.Agent.Services;

/// <summary>Where a session's context went at its latest request: estimated categories that sum to the measured
/// context, the tool groups loaded on every request, and the compactions so far.</summary>
/// <param name="TakenAt">The latest <c>api-request</c>'s time, UTC.</param>
internal sealed record ContextSnapshot(
    DateTime TakenAt,
    string ClaudeCodeVersion,
    string Model,
    long MeasuredContextTokens,
    IReadOnlyList<ContextCategoryTokens> Categories,
    IReadOnlyList<ContextToolGroup> Tools,
    IReadOnlyList<ContextCompaction> Compactions);

/// <param name="Category">One of <see cref="ContextCategories"/>.</param>
/// <param name="Server">The MCP server, for <see cref="ContextCategories.McpTools"/> only.</param>
internal sealed record ContextCategoryTokens(string Category, string? Server, long Tokens);

/// <param name="Group">An MCP server, or a built-in tool's own name.</param>
/// <param name="Used">Some tool of the group was called, in the session or one of its subagents.</param>
internal sealed record ContextToolGroup(string Group, bool IsMcp, int ToolCount, long TokensPerRequest, bool Used);

internal sealed record ContextCompaction(DateTime At, string Trigger, long PreTokens, long PostTokens);

internal static class ContextCategories
{
    public const string SystemPrompt = "systemPrompt";
    public const string BuiltInTools = "builtInTools";
    public const string McpTools = "mcpTools";
    public const string McpInstructions = "mcpInstructions";
    public const string CustomAgents = "customAgents";
    public const string MemoryFiles = "memoryFiles";
    public const string Skills = "skills";
    public const string Conversation = "conversation";
}
