import type { UsageBucket, UsageGranularity, UsageSummary } from "../types";
import { formatCost } from "./tokenFormat";
import { formatPercent } from "./usageFormat";

type RawValue = string | number | null;

/** One figure of a usage bucket: `raw` feeds CSV and JSON, `display` feeds the screen and PDF. */
export interface UsageColumn {
  key: keyof UsageBucket;
  header: string;
  raw: (bucket: UsageBucket) => RawValue;
  display: (bucket: UsageBucket) => string;
}

type TokenKey = "inputTokens" | "outputTokens" | "cacheReadInputTokens" | "cacheCreationInputTokens" | "totalTokens";

function tokenColumn(key: TokenKey, header: string): UsageColumn {
  return { key, header, raw: (b) => b[key], display: (b) => b[key].toLocaleString() };
}

/** The single definition of the bucket table: the screen, CSV, JSON and PDF all read it (rule 7). */
export const USAGE_COLUMNS: readonly UsageColumn[] = [
  { key: "start", header: "Period", raw: (b) => b.start, display: (b) => b.start },
  tokenColumn("inputTokens", "Input tokens"),
  tokenColumn("outputTokens", "Output tokens"),
  tokenColumn("cacheReadInputTokens", "Cache read tokens"),
  tokenColumn("cacheCreationInputTokens", "Cache write tokens"),
  tokenColumn("totalTokens", "Total tokens"),
  {
    key: "estimatedCostUsd",
    header: "Est. cost (USD)",
    raw: (b) => b.estimatedCostUsd,
    display: (b) => formatCost(b.estimatedCostUsd),
  },
  {
    key: "cacheHitRate",
    header: "Cache hit rate",
    raw: (b) => b.cacheHitRate ?? null,
    display: (b) => formatPercent(b.cacheHitRate),
  },
  { key: "turnCount", header: "Turns", raw: (b) => b.turnCount, display: (b) => b.turnCount.toLocaleString() },
];

export interface ExportTable {
  headers: string[];
  rows: string[][];
}

/** Raw numbers, with the price date on every row so a pasted range still says which prices it used. */
export function usageCsvTable(buckets: readonly UsageBucket[], pricesAsOf: string): ExportTable {
  return {
    headers: [...USAGE_COLUMNS.map((c) => c.header), "Prices as of"],
    rows: buckets.map((b) => [...USAGE_COLUMNS.map((c) => String(c.raw(b) ?? "")), pricesAsOf]),
  };
}

export function usagePdfTable(buckets: readonly UsageBucket[]): ExportTable {
  return {
    headers: USAGE_COLUMNS.map((c) => c.header),
    rows: buckets.map((b) => USAGE_COLUMNS.map((c) => c.display(b))),
  };
}

export interface UsageJsonExport {
  from: string;
  to: string;
  granularity: UsageGranularity;
  timeZone: string;
  pricesAsOf: string;
  rows: Record<string, RawValue>[];
}

export function usageJsonExport(summary: UsageSummary): UsageJsonExport {
  return {
    from: summary.from,
    to: summary.to,
    granularity: summary.granularity,
    timeZone: summary.timeZone,
    pricesAsOf: summary.pricesAsOf,
    rows: summary.buckets.map((b) => Object.fromEntries(USAGE_COLUMNS.map((c) => [c.key, c.raw(b)]))),
  };
}
