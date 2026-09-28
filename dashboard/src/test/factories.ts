import type {
  SessionListItem,
  SessionDetail,
  SessionTimelinePage,
  DailyUsage,
  DailyCostUsage,
  ModelUsage,
  ConversationTurn,
  ToolExecution,
  FileChange,
  GovernanceViolation,
  ArchitectAnnotation,
  PullRequestReview,
  PullRequestDetail,
  LinkedSession,
  TeamAnalytics,
  DeveloperAnalytics,
  DeveloperMetrics,
} from "../types";

export function buildSessionListItem(overrides?: Partial<SessionListItem>): SessionListItem {
  return {
    id: "sess-001",
    developerEmail: "dev@example.com",
    repoPath: "org/repo",
    status: "Completed",
    startedAt: "2026-05-27T08:00:00Z",
    endedAt: "2026-05-27T09:30:00Z",
    createdAt: "2026-05-27T08:00:00Z",
    totalInputTokens: 0,
    totalOutputTokens: 0,
    totalCacheCreationInputTokens: 0,
    totalCacheReadInputTokens: 0,
    estimatedCostUsd: 0,
    violationCount: 0,
    dailyUsage: {},
    ...overrides,
  };
}

export function buildSessionDetail(overrides?: Partial<SessionDetail>): SessionDetail {
  return {
    id: "sess-001",
    developerEmail: "dev@example.com",
    repoPath: "org/repo",
    status: "Completed",
    startedAt: "2026-05-27T08:00:00Z",
    endedAt: "2026-05-27T09:30:00Z",
    createdAt: "2026-05-27T08:00:00Z",
    totalInputTokens: 0,
    totalOutputTokens: 0,
    totalCacheCreationInputTokens: 0,
    totalCacheReadInputTokens: 0,
    estimatedCostUsd: 0,
    conversationTurnCount: 0,
    toolExecutionCount: 0,
    fileChangeCount: 0,
    governanceViolations: [],
    architectAnnotations: [],
    ...overrides,
  };
}

export function buildConversationTurn(overrides?: Partial<ConversationTurn>): ConversationTurn {
  return {
    id: "turn-001",
    sessionId: "sess-001",
    turnIndex: 0,
    role: "user",
    content: "Hello, please help me.",
    tokenCount: 42,
    timestamp: "2026-05-27T08:01:00Z",
    isDecision: false,
    ...overrides,
  };
}

export function buildToolExecution(overrides?: Partial<ToolExecution>): ToolExecution {
  return {
    id: "tool-001",
    sessionId: "sess-001",
    toolName: "Read",
    inputParams: '{"path":"src/main.ts"}',
    outputSummary: "File contents returned",
    durationMs: 150,
    timestamp: "2026-05-27T08:02:00Z",
    ...overrides,
  };
}

export function buildFileChange(overrides?: Partial<FileChange>): FileChange {
  return {
    id: "fc-001",
    sessionId: "sess-001",
    filePath: "src/utils.ts",
    changeType: "Modified",
    diffText: "@@ -1,3 +1,4 @@\n+import { foo } from './foo';",
    ...overrides,
  };
}

export function buildSessionTimelinePage(overrides?: Partial<SessionTimelinePage>): SessionTimelinePage {
  return {
    conversationTurns: [],
    toolExecutions: [],
    fileChanges: [],
    hasMore: false,
    ...overrides,
  };
}

export function buildDailyUsage(overrides?: Partial<DailyUsage>): DailyUsage {
  return {
    date: "2026-05-27",
    turnCount: 1,
    inputTokens: 100,
    outputTokens: 50,
    cacheCreationInputTokens: 0,
    cacheReadInputTokens: 0,
    estimatedCostUsd: 0.01,
    ...overrides,
  };
}

export function buildDailyCostUsage(overrides?: Partial<DailyCostUsage>): DailyCostUsage {
  return {
    date: "2026-05-27",
    inputTokens: 100,
    outputTokens: 50,
    cacheCreationInputTokens: 0,
    cacheReadInputTokens: 0,
    estimatedCostUsd: 0.01,
    ...overrides,
  };
}

export function buildModelUsage(overrides?: Partial<ModelUsage>): ModelUsage {
  return {
    model: "claude-sonnet-5",
    turnCount: 1,
    inputTokens: 100,
    outputTokens: 50,
    cacheCreationInputTokens: 0,
    cacheReadInputTokens: 0,
    estimatedCostUsd: 0.01,
    ...overrides,
  };
}

export function buildGovernanceViolation(overrides?: Partial<GovernanceViolation>): GovernanceViolation {
  return {
    id: "viol-001",
    sessionId: "sess-001",
    ruleId: "no-console-log",
    severity: "Warning",
    message: "console.log found in production code",
    resolved: false,
    createdAt: "2026-05-27T09:00:00Z",
    ...overrides,
  };
}

export function buildArchitectAnnotation(overrides?: Partial<ArchitectAnnotation>): ArchitectAnnotation {
  return {
    id: "ann-001",
    sessionId: "sess-001",
    content: "Good use of Result pattern here.",
    createdBy: "lead@example.com",
    createdAt: "2026-05-27T09:15:00Z",
    ...overrides,
  };
}

export function buildPullRequestReview(overrides?: Partial<PullRequestReview>): PullRequestReview {
  return {
    id: "pr-001",
    prNumber: 42,
    repo: "org/repo",
    status: "Pending",
    ...overrides,
  };
}

export function buildLinkedSession(overrides?: Partial<LinkedSession>): LinkedSession {
  return {
    sessionId: "sess-001",
    commitSha: "abc1234",
    confidence: 0.85,
    linkMethod: "file-change-matching",
    ...overrides,
  };
}

export function buildPullRequestDetail(overrides?: Partial<PullRequestDetail>): PullRequestDetail {
  return {
    id: "pr-001",
    prNumber: 42,
    repo: "org/repo",
    status: "Pending",
    linkedSessions: [],
    ...overrides,
  };
}

export function buildTeamAnalytics(overrides?: Partial<TeamAnalytics>): TeamAnalytics {
  return {
    totalSessions: 100,
    sessionsThisWeek: 15,
    totalPullRequests: 50,
    approvedPullRequests: 30,
    pendingPullRequests: 12,
    needsChangesPullRequests: 8,
    totalViolations: 25,
    violationsThisWeek: 3,
    developers: [],
    dailyCostTrend: [],
    dailyCostThresholdUsd: 50,
    ...overrides,
  };
}

export function buildDeveloperMetrics(overrides?: Partial<DeveloperMetrics>): DeveloperMetrics {
  return {
    email: "dev@example.com",
    sessionCount: 10,
    violationCount: 2,
    commitCount: 15,
    ...overrides,
  };
}

export function buildDeveloperAnalytics(overrides?: Partial<DeveloperAnalytics>): DeveloperAnalytics {
  return {
    email: "dev@example.com",
    totalSessions: 10,
    sessionsThisWeek: 3,
    totalViolations: 5,
    criticalViolations: 0,
    errorViolations: 1,
    warningViolations: 3,
    infoViolations: 1,
    totalCommits: 15,
    recentRepos: ["org/repo"],
    ...overrides,
  };
}
