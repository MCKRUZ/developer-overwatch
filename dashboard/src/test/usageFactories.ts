import type { RunningSession, UsageBucket, UsageSplitRow, UsageSplits, UsageSummary, UsageTotals } from "../types";

export function buildUsageTotals(overrides?: Partial<UsageTotals>): UsageTotals {
  return {
    inputTokens: 1000,
    outputTokens: 200,
    cacheCreationInputTokens: 300,
    cacheReadInputTokens: 4000,
    totalTokens: 5500,
    estimatedCostUsd: 1.25,
    cacheHitRate: 0.75,
    turnCount: 10,
    ...overrides,
  };
}

export function buildUsageBucket(overrides?: Partial<UsageBucket>): UsageBucket {
  return { ...buildUsageTotals(), start: "2026-11-04", ...overrides };
}

export function buildUsageSplitRow(overrides?: Partial<UsageSplitRow>): UsageSplitRow {
  return {
    ...buildUsageTotals(),
    key: "claude-sonnet-4-5",
    label: "claude-sonnet-4-5",
    costShare: 1,
    firstAt: "2026-11-04T09:00:00Z",
    lastAt: "2026-11-04T10:30:00Z",
    ...overrides,
  };
}

export function buildUsageSplits(overrides?: Partial<UsageSplits>): UsageSplits {
  return {
    model: [],
    project: [],
    session: [],
    subagent: [],
    branch: [],
    developer: [],
    machine: [],
    agent: [],
    ...overrides,
  };
}

export function buildUsageSummary(overrides?: Partial<UsageSummary>): UsageSummary {
  return {
    from: "2026-11-04",
    to: "2026-11-10",
    granularity: "day",
    timeZone: "UTC",
    pricesAsOf: "2026-11-01",
    totals: buildUsageTotals(),
    buckets: [buildUsageBucket()],
    splits: buildUsageSplits(),
    burn: { tokensPerMinute: 4200, costPerMinute: 0.21, lookbackMinutes: 15 },
    ...overrides,
  };
}

export function buildRunningSession(overrides?: Partial<RunningSession>): RunningSession {
  return {
    id: "7bb533f1-0000-0000-0000-000000000000",
    developerEmail: "dev@example.com",
    project: "overwatch",
    machineName: "AVD-01",
    agent: "claude-code",
    activityState: "Active",
    lastActivityAt: "2026-11-10T10:12:04Z",
    inputTokens: 1000,
    outputTokens: 200,
    cacheCreationInputTokens: 300,
    cacheReadInputTokens: 4000,
    estimatedCostUsd: 9.3,
    currentContextTokens: 148000,
    burnTokensPerMinute: 4200,
    burnCostPerMinute: 0.21,
    ...overrides,
  };
}
