namespace DeveloperOverwatch.Application.Queries.GetSessionContext;

using DeveloperOverwatch.Domain.Common;
using MediatR;

/// <summary>How full a session's context is, what fills it, and what it carries without using.</summary>
public sealed record GetSessionContextQuery(string SessionId) : IRequest<Result<SessionContextView>>;

/// <param name="WindowsAsOf">The date the context window sizes were taken from.</param>
/// <param name="CurrentModel">The current four are null when no main-thread turn has measured its context.</param>
/// <param name="PeakModel">The model of the peak turn, or the current model for sessions stored before it was recorded.</param>
/// <param name="Breakout">Null when the Agent's Claude Code version gave no request shape to estimate from.</param>
/// <param name="UnusedTools">Costliest first; null when there is no snapshot.</param>
public sealed record SessionContextView(
    DateOnly WindowsAsOf,
    string? CurrentModel,
    long? CurrentContextTokens,
    long? CurrentWindowTokens,
    double? CurrentFill,
    long? PeakContextTokens,
    string? PeakModel,
    long? PeakWindowTokens,
    double? PeakFill,
    int ToolErrorCount,
    string? LastToolErrorName,
    DateTime? LastToolErrorAt,
    ContextBreakoutView? Breakout,
    IReadOnlyList<UnusedToolGroupView>? UnusedTools);

/// <param name="Categories">Largest first, with "conversation" always last.</param>
/// <param name="Compactions">Newest first.</param>
public sealed record ContextBreakoutView(
    DateTime TakenAt,
    string ClaudeCodeVersion,
    string Model,
    long MeasuredContextTokens,
    long WindowTokens,
    double? Fill,
    IReadOnlyList<ContextCategoryView> Categories,
    IReadOnlyList<ContextCompactionView> Compactions);

/// <param name="Category">The category's camelCase name, as the dashboard and the Agent spell it.</param>
/// <param name="Server">The MCP server; set only for "mcpTools".</param>
public sealed record ContextCategoryView(string Category, string? Server, long Tokens);

public sealed record ContextCompactionView(DateTime At, string Trigger, long PreTokens, long PostTokens);

public sealed record UnusedToolGroupView(string Group, bool IsMcp, int ToolCount, long TokensPerRequest);
