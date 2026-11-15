import type {
  ContextBreakout,
  PromptCost,
  SessionContext,
  SubagentUsage,
  UnusedToolGroup,
} from "../sessionInsightTypes";

/** A session with nothing measured yet: what an old agent or a fresh session returns. */
export function buildEmptySessionContext(overrides?: Partial<SessionContext>): SessionContext {
  return { windowsAsOf: "2026-11-01", toolErrorCount: 0, ...overrides };
}

export function buildContextBreakout(overrides?: Partial<ContextBreakout>): ContextBreakout {
  return {
    takenAt: "2026-11-13T14:30:00Z",
    claudeCodeVersion: "2.1.40",
    model: "claude-opus-5-5",
    measuredContextTokens: 170_000,
    windowTokens: 1_000_000,
    fill: 0.17,
    categories: [
      { category: "systemPrompt", server: null, tokens: 40_000 },
      { category: "mcpTools", server: "github", tokens: 30_000 },
      { category: "builtInTools", server: null, tokens: 20_000 },
      { category: "conversation", server: null, tokens: 80_000 },
    ],
    compactions: [],
    ...overrides,
  };
}

export function buildUnusedToolGroup(overrides?: Partial<UnusedToolGroup>): UnusedToolGroup {
  return { group: "github", isMcp: true, toolCount: 12, tokensPerRequest: 5_000, ...overrides };
}

export function buildSessionContext(overrides?: Partial<SessionContext>): SessionContext {
  return {
    windowsAsOf: "2026-11-01",
    currentModel: "claude-sonnet-4-5",
    currentContextTokens: 148_000,
    currentWindowTokens: 200_000,
    currentFill: 0.74,
    peakContextTokens: 190_000,
    peakModel: "claude-sonnet-4-5",
    peakWindowTokens: 200_000,
    peakFill: 0.95,
    toolErrorCount: 2,
    lastToolErrorName: "Bash",
    lastToolErrorAt: "2026-11-13T14:00:00Z",
    breakout: buildContextBreakout(),
    unusedTools: [buildUnusedToolGroup()],
    ...overrides,
  };
}

export function buildPromptCost(overrides?: Partial<PromptCost>): PromptCost {
  return {
    index: 1,
    startedAt: "2026-11-13T14:00:00Z",
    label: "Fix the login bug",
    turnCount: 6,
    inputTokens: 1_000,
    outputTokens: 500,
    cacheCreationInputTokens: 2_000,
    cacheReadInputTokens: 6_500,
    totalTokens: 10_000,
    estimatedCostUsd: 0.5,
    subagentCostUsd: 0,
    ...overrides,
  };
}

export function buildSubagentUsage(overrides?: Partial<SubagentUsage>): SubagentUsage {
  return {
    subagentName: null,
    turnCount: 10,
    inputTokens: 1_000,
    outputTokens: 500,
    cacheCreationInputTokens: 2_000,
    cacheReadInputTokens: 6_500,
    totalTokens: 10_000,
    estimatedCostUsd: 3,
    ...overrides,
  };
}
