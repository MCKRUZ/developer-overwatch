namespace DeveloperOverwatch.Application.Commands.IngestSessionEvents;

using DeveloperOverwatch.Domain.Entities;

/// <summary>Builds the stored snapshot from a validated <see cref="ContextSnapshotDto"/>.</summary>
internal static class ContextSnapshotMapper
{
    /// <summary>Every time is stored in UTC and clamped to <paramref name="now"/>, like every other time the Agent
    /// sends: a fast client clock must not pin a snapshot or a compaction in the future.</summary>
    public static SessionContextSnapshot ToEntity(string sessionId, ContextSnapshotDto dto, DateTime now) =>
        SessionContextSnapshot.Create(
            sessionId,
            AtMost(dto.TakenAt, now),
            dto.ClaudeCodeVersion!,
            dto.Model!,
            dto.MeasuredContextTokens,
            dto.Categories.Select(c => new SessionContextCategory(c.Category, c.Server, c.Tokens)),
            dto.Tools.Select(t => new SessionContextToolGroup(t.Group!, t.IsMcp, t.ToolCount, t.TokensPerRequest, t.Used)),
            dto.Compactions.Select(c =>
                new SessionContextCompaction(AtMost(c.At, now), c.Trigger!, c.PreTokens, c.PostTokens)));

    private static DateTime AtMost(DateTime at, DateTime now)
    {
        var utc = at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : at;
        return utc > now ? now : utc;
    }
}
