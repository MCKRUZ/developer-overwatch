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
}

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
