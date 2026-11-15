namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Entities;

/// <summary>Builds the stored snapshot from a validated <see cref="ContextSnapshotDto"/>.</summary>
internal static class ContextSnapshotMapper
{
    /// <param name="takenAtUtc">The snapshot's time, already in UTC and clamped to now by the caller.</param>
    public static SessionContextSnapshot ToEntity(string sessionId, ContextSnapshotDto dto, DateTime takenAtUtc) =>
        SessionContextSnapshot.Create(
            sessionId,
            takenAtUtc,
            dto.ClaudeCodeVersion!,
            dto.Model!,
            dto.MeasuredContextTokens,
            dto.Categories.Select(c => new SessionContextCategory(c.Category, c.Server, c.Tokens)),
            dto.Tools.Select(t => new SessionContextToolGroup(t.Group!, t.IsMcp, t.ToolCount, t.TokensPerRequest, t.Used)),
            dto.Compactions.Select(c => new SessionContextCompaction(ToUtc(c.At), c.Trigger!, c.PreTokens, c.PostTokens)));

    private static DateTime ToUtc(DateTime t) => t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t;
}
