import type { DailyTokenCost, SessionListItem } from "../types";

interface TokenUsage {
  inputTokens?: number;
  outputTokens?: number;
  cacheCreationInputTokens?: number;
  cacheReadInputTokens?: number;
}

/** All tokens the model processed. Cache reads and writes are included: in Claude Code they are
 * usually most of the volume, so a total without them badly understates real usage. */
export function totalTokens(usage: TokenUsage): number {
  return (
    (usage.inputTokens ?? 0) +
    (usage.outputTokens ?? 0) +
    (usage.cacheCreationInputTokens ?? 0) +
    (usage.cacheReadInputTokens ?? 0)
  );
}

export function formatTotalTokens(usage: TokenUsage): string {
  return totalTokens(usage).toLocaleString();
}

/** A session's token/cost contribution for one specific calendar day ("yyyy-MM-dd"), not its
 * lifetime total — what matters for day-by-day cost control. Falls back to zero if the session
 * had no usage recorded for that day (e.g. it was only just started). */
export function dailyUsageFor(session: SessionListItem, day: string): DailyTokenCost {
  return (
    session.dailyUsage[day] ?? {
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
      estimatedCostUsd: 0,
    }
  );
}

export function formatCost(estimatedCostUsd?: number): string {
  return estimatedCostUsd != null ? `$${estimatedCostUsd.toFixed(2)}` : "---";
}

export function tokenBreakdownTooltip(usage: {
  totalInputTokens?: number;
  totalOutputTokens?: number;
  totalCacheReadInputTokens?: number;
  totalCacheCreationInputTokens?: number;
}): string {
  return [
    usage.totalInputTokens != null ? `${usage.totalInputTokens} in` : null,
    usage.totalOutputTokens != null ? `${usage.totalOutputTokens} out` : null,
    usage.totalCacheReadInputTokens ? `${usage.totalCacheReadInputTokens} cache read` : null,
    usage.totalCacheCreationInputTokens ? `${usage.totalCacheCreationInputTokens} cache write` : null,
  ]
    .filter(Boolean)
    .join(" · ");
}
