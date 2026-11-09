namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using MediatR;

public sealed record GetSessionQuery(string SessionId) : IRequest<Result<SessionDetail>>;

/// <summary>
/// Session detail for the dashboard's session page — scalar fields + counts, deliberately
/// WITHOUT the ConversationTurns/ToolExecutions/FileChanges collections (those can be hundreds+
/// of rows with large content payloads; fetch them via the paginated
/// GET /api/sessions/{id}/timeline endpoint instead). Governance violations/annotations are
/// small in practice and kept eager.
/// </summary>
public sealed record SessionDetail(
    string Id,
    string DeveloperEmail,
    string? DeveloperName,
    string? RepoPath,
    string? Title,
    SessionStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? SummaryJson,
    DateTime CreatedAt,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalCacheCreationInputTokens,
    int TotalCacheReadInputTokens,
    decimal EstimatedCostUsd,
    int ConversationTurnCount,
    int ToolExecutionCount,
    int FileChangeCount,
    IReadOnlyList<GovernanceViolation> GovernanceViolations,
    IReadOnlyList<ArchitectAnnotation> ArchitectAnnotations);
