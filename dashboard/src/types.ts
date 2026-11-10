export interface SessionListItem {
  id: string;
  developerEmail: string;
  developerName?: string;
  repoPath?: string;
  title?: string;
  status: "Active" | "Completed" | "Failed" | "Stale";
  startedAt: string;
  endedAt?: string;
  createdAt: string;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheCreationInputTokens: number;
  totalCacheReadInputTokens: number;
  estimatedCostUsd: number;
  violationCount: number;
  dailyUsage: Record<string, DailyTokenCost>;
  agent?: string;
  machineName?: string;
  projectKey?: string;
  peakContextTokens?: number;
  activityState?: ActivityState;
  activityStateAt?: string;
  toolErrorCount?: number;
  lastToolErrorName?: string;
  lastToolErrorAt?: string;
}

export type ActivityState = "Active" | "Waiting" | "Idle";

export interface DailyTokenCost {
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  estimatedCostUsd: number;
}

export interface SessionDetail {
  id: string;
  developerEmail: string;
  developerName?: string;
  repoPath?: string;
  title?: string;
  status: "Active" | "Completed" | "Failed" | "Stale";
  startedAt: string;
  endedAt?: string;
  summaryJson?: string;
  createdAt: string;
  totalInputTokens: number;
  totalOutputTokens: number;
  totalCacheCreationInputTokens: number;
  totalCacheReadInputTokens: number;
  estimatedCostUsd: number;
  conversationTurnCount: number;
  toolExecutionCount: number;
  fileChangeCount: number;
  governanceViolations: GovernanceViolation[];
  architectAnnotations: ArchitectAnnotation[];
  agent?: string;
  machineName?: string;
  projectKey?: string;
  peakContextTokens?: number;
  activityState?: ActivityState;
  activityStateAt?: string;
  toolErrorCount?: number;
  lastToolErrorName?: string;
  lastToolErrorAt?: string;
}

export interface ConversationTurn {
  id: string;
  sessionId: string;
  turnIndex: number;
  role: string;
  content?: string;
  tokenCount?: number;
  inputTokens?: number;
  outputTokens?: number;
  cacheCreationInputTokens?: number;
  cacheReadInputTokens?: number;
  model?: string;
  estimatedCostUsd?: number;
  containsRedactedSecret?: boolean;
  timestamp: string;
  isDecision: boolean;
}

export interface ToolExecution {
  id: string;
  sessionId: string;
  turnId?: string;
  toolName: string;
  inputParams?: string;
  outputSummary?: string;
  durationMs?: number;
  timestamp: string;
}

export interface FileChange {
  id: string;
  sessionId: string;
  toolExecutionId?: string;
  filePath: string;
  beforeHash?: string;
  afterHash?: string;
  diffText?: string;
  changeType?: "Created" | "Modified" | "Deleted";
}

export interface SessionTimelinePage {
  conversationTurns: ConversationTurn[];
  toolExecutions: ToolExecution[];
  fileChanges: FileChange[];
  nextCursor?: string;
  hasMore: boolean;
}

export interface DailyUsage {
  date: string;
  turnCount: number;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  estimatedCostUsd: number;
}

export interface ModelUsage {
  model: string;
  turnCount: number;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  estimatedCostUsd: number;
}

export interface GovernanceViolation {
  id: string;
  sessionId: string;
  ruleId: string;
  severity: "Info" | "Warning" | "Error" | "Critical";
  message: string;
  contextJson?: string;
  conversationTurnId?: string;
  toolExecutionId?: string;
  resolved: boolean;
  resolvedBy?: string;
  resolutionReason?: string;
  resolvedAt?: string;
  createdAt: string;
}

export interface ArchitectAnnotation {
  id: string;
  sessionId: string;
  turnId?: string;
  toolExecutionId?: string;
  content: string;
  createdBy: string;
  createdAt: string;
}

export interface PullRequestReview {
  id: string;
  prNumber: number;
  repo: string;
  status: "Pending" | "Approved" | "NeedsChanges";
  reviewedBy?: string;
  reviewedAt?: string;
  notes?: string;
}

export interface PullRequestDetail {
  id: string;
  prNumber: number;
  repo: string;
  status: string;
  reviewedBy?: string;
  reviewedAt?: string;
  notes?: string;
  linkedSessions: LinkedSession[];
}

export interface LinkedSession {
  sessionId: string;
  commitSha: string;
  confidence: number;
  linkMethod: string;
  developerEmail?: string;
  sessionStartedAt?: string;
  sessionEndedAt?: string;
}

export interface DailyCostUsage {
  date: string;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  estimatedCostUsd: number;
}

export interface TeamAnalytics {
  totalSessions: number;
  sessionsThisWeek: number;
  totalPullRequests: number;
  approvedPullRequests: number;
  pendingPullRequests: number;
  needsChangesPullRequests: number;
  totalViolations: number;
  violationsThisWeek: number;
  developers: DeveloperMetrics[];
  dailyCostTrend: DailyCostUsage[];
  dailyCostThresholdUsd: number;
}

export interface DeveloperMetrics {
  email: string;
  sessionCount: number;
  violationCount: number;
  commitCount: number;
}

export interface DeveloperAnalytics {
  email: string;
  totalSessions: number;
  sessionsThisWeek: number;
  totalViolations: number;
  criticalViolations: number;
  errorViolations: number;
  warningViolations: number;
  infoViolations: number;
  totalCommits: number;
  recentRepos: string[];
}

export interface PricingInfo {
  asOf: string;
}

export type UsageGranularity = "day" | "week" | "month";

/** Sums for one slice of usage. `cacheHitRate` (0..1) is omitted when there was no input. */
export interface UsageTotals {
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  cacheHitRate?: number;
  turnCount: number;
}

/** One day, ISO week (its Monday) or month (its 1st), as a local YYYY-MM-DD date. */
export interface UsageBucket extends UsageTotals {
  start: string;
}

/** `key` is omitted for the main thread (subagent split) and for an unknown branch or machine. */
export interface UsageSplitRow extends UsageTotals {
  key?: string;
  label: string;
  costShare: number;
  firstAt: string;
  lastAt: string;
}

export interface UsageSplits {
  model: UsageSplitRow[];
  project: UsageSplitRow[];
  session: UsageSplitRow[];
  subagent: UsageSplitRow[];
  branch: UsageSplitRow[];
  developer: UsageSplitRow[];
  machine: UsageSplitRow[];
  agent: UsageSplitRow[];
}

/** Spend over the last `lookbackMinutes`, all four token kinds, per minute. */
export interface UsageBurn {
  tokensPerMinute: number;
  costPerMinute: number;
  lookbackMinutes: number;
}

export interface UsageSummary {
  from: string;
  to: string;
  granularity: UsageGranularity;
  timeZone: string;
  pricesAsOf: string;
  /** The developer and project filters the server applied, echoed back; omitted when not set. */
  developer?: string;
  project?: string;
  totals: UsageTotals;
  buckets: UsageBucket[];
  splits: UsageSplits;
  burn: UsageBurn;
}

export interface RunningSession {
  id: string;
  developerEmail: string;
  developerName?: string;
  project?: string;
  machineName?: string;
  agent: string;
  activityState?: ActivityState;
  idleForSeconds?: number;
  lastActivityAt: string;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  estimatedCostUsd: number;
  currentContextTokens?: number;
  burnTokensPerMinute: number;
  burnCostPerMinute: number;
}
