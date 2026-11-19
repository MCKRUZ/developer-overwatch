namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

/// <summary>Tokens one category takes in a <see cref="SessionContextSnapshot"/>.</summary>
public sealed class SessionContextCategory
{
    public SessionContextCategory(ContextCategory category, string? server, long tokens)
    {
        Category = category;
        Server = server;
        Tokens = tokens;
    }

    public ContextCategory Category { get; private set; }

    /// <summary>The MCP server for an <see cref="ContextCategory.McpTools"/> row, else null. The Agent folds
    /// the smallest servers into one row named "(other servers)" to stay within the category cap.</summary>
    public string? Server { get; private set; }

    public long Tokens { get; private set; }
}
