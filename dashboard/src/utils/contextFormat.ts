import type { BudgetLevel } from "../types";
import type { ContextCategory, ContextCategoryName } from "../sessionInsightTypes";
import { formatUsedPercent } from "./budgetFormat";

/** The Usage page's level thresholds, applied to how full a context window is. */
const AMBER_FROM = 0.75;
const RED_FROM = 0.9;

const CATEGORY_LABELS: Record<ContextCategoryName, string> = {
  systemPrompt: "System prompt",
  builtInTools: "Built-in tools",
  mcpTools: "MCP tools",
  mcpInstructions: "MCP server instructions",
  customAgents: "Custom agents",
  memoryFiles: "Memory files",
  skills: "Skills",
  conversation: "Conversation",
};

/** "999", "148K", "1.0M". Thousands that round to 1000K read as 1.0M instead. */
export function formatCompactTokens(tokens: number): string {
  if (tokens < 1_000) return String(tokens);
  const thousands = Math.round(tokens / 1_000);
  if (thousands < 1_000) return `${thousands}K`;
  return `${(tokens / 1_000_000).toFixed(1)}M`;
}

/** Floored like budget percents; not capped, because over 100% means the window size is outdated. */
export function formatFill(fill: number): string {
  return formatUsedPercent(fill);
}

export function contextLevel(fill: number): BudgetLevel {
  if (fill >= RED_FROM) return "Red";
  if (fill >= AMBER_FROM) return "Amber";
  return "Ok";
}

/** A friendly name; MCP tools rows carry their server, since there is one row per server. */
export function categoryLabel(category: Pick<ContextCategory, "category" | "server">): string {
  const label = CATEGORY_LABELS[category.category] ?? category.category;
  return category.category === "mcpTools" && category.server != null ? `${label} (${category.server})` : label;
}
