/**
 * Response shapes for the session-level reads: `GET /api/sessions/{id}/context`, `/prompts` and
 * `/subagent-breakdown`. The server omits null members (WhenWritingNull), so every nullable
 * field may arrive missing as well as null: read them with `== null`.
 */

export type ContextCategoryName =
  | "systemPrompt"
  | "builtInTools"
  | "mcpTools"
  | "mcpInstructions"
  | "customAgents"
  | "memoryFiles"
  | "skills"
  | "conversation";

export interface ContextCategory {
  category: ContextCategoryName;
  /** The MCP server name; only set for `mcpTools`. */
  server?: string | null;
  tokens: number;
}

export interface ContextCompaction {
  at: string;
  trigger: string;
  preTokens: number;
  postTokens: number;
}

/** The estimate from the latest Claude Code request shape, scaled to the measured context. */
export interface ContextBreakout {
  takenAt: string;
  claudeCodeVersion: string;
  model: string;
  measuredContextTokens: number;
  windowTokens: number;
  /** Measured / window, unclamped; null when the window size is unknown. */
  fill?: number | null;
  /** Largest first, with `conversation` always last. */
  categories: ContextCategory[];
  /** Newest first. */
  compactions: ContextCompaction[];
}

/** A tool group whose definitions were loaded on every request but never called. */
export interface UnusedToolGroup {
  group: string;
  isMcp: boolean;
  toolCount: number;
  tokensPerRequest: number;
}

export interface SessionContext {
  /** The date the context window sizes were taken from, as YYYY-MM-DD. */
  windowsAsOf: string;
  /** The current four are all null when no main-thread turn has measured context yet. */
  currentModel?: string | null;
  currentContextTokens?: number | null;
  currentWindowTokens?: number | null;
  currentFill?: number | null;
  peakContextTokens?: number | null;
  peakModel?: string | null;
  peakWindowTokens?: number | null;
  peakFill?: number | null;
  toolErrorCount: number;
  lastToolErrorName?: string | null;
  lastToolErrorAt?: string | null;
  /** Null when the agent's Claude Code version gave no request shape to estimate from. */
  breakout?: ContextBreakout | null;
  /** Largest per-request cost first; null when there is no snapshot. */
  unusedTools?: UnusedToolGroup[] | null;
}

/** One prompt and everything it caused up to the next prompt. Index 0 holds turns before the first prompt. */
export interface PromptCost {
  index: number;
  startedAt: string;
  /** Collapsed and cut to 80 characters; null when the prompt has no captured text. */
  label?: string | null;
  turnCount: number;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
  subagentCostUsd: number;
}

export interface SubagentUsage {
  /** Null for the main thread. */
  subagentName?: string | null;
  turnCount: number;
  inputTokens: number;
  outputTokens: number;
  cacheCreationInputTokens: number;
  cacheReadInputTokens: number;
  totalTokens: number;
  estimatedCostUsd: number;
}
