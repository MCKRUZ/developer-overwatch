import type { UsageBucket, UsageGranularity, UsageSplitRow, UsageSplits, UsageSummary, UsageTotals } from "../types";
import { formatCost } from "./tokenFormat";
import { formatPercent } from "./usageFormat";

type RawValue = string | number | null;

/** One figure of a usage row: `raw` feeds CSV and JSON, `display` feeds the screen and PDF. */
export interface UsageColumn<R extends UsageTotals = UsageBucket> {
  key: keyof R;
  header: string;
  raw: (row: R) => RawValue;
  display: (row: R) => string;
}

type TokenKey = "inputTokens" | "outputTokens" | "cacheReadInputTokens" | "cacheCreationInputTokens" | "totalTokens";

function tokenColumn(key: TokenKey, header: string): UsageColumn<UsageTotals> {
  return { key, header, raw: (r) => r[key], display: (r) => r[key].toLocaleString() };
}

/** The figures every usage row carries, whether it is a time bucket or a split row. */
const TOTALS_COLUMNS: readonly UsageColumn<UsageTotals>[] = [
  tokenColumn("inputTokens", "Input tokens"),
  tokenColumn("outputTokens", "Output tokens"),
  tokenColumn("cacheReadInputTokens", "Cache read tokens"),
  tokenColumn("cacheCreationInputTokens", "Cache write tokens"),
  tokenColumn("totalTokens", "Total tokens"),
  {
    key: "estimatedCostUsd",
    header: "Est. cost (USD)",
    raw: (r) => r.estimatedCostUsd,
    display: (r) => formatCost(r.estimatedCostUsd),
  },
  {
    key: "cacheHitRate",
    header: "Cache hit rate",
    raw: (r) => r.cacheHitRate ?? null,
    display: (r) => formatPercent(r.cacheHitRate),
  },
  { key: "turnCount", header: "Turns", raw: (r) => r.turnCount, display: (r) => r.turnCount.toLocaleString() },
];

/** The single definition of the bucket table: the screen, CSV, JSON and PDF all read it (rule 7). */
export const USAGE_COLUMNS: readonly UsageColumn[] = [
  { key: "start", header: "Period", raw: (b) => b.start, display: (b) => b.start },
  ...TOTALS_COLUMNS,
];

/** The split dimensions in tab order, shared by the split tabs and the exports. */
export const SPLIT_DIMENSIONS: readonly { key: keyof UsageSplits; label: string }[] = [
  { key: "model", label: "Model" },
  { key: "project", label: "Project" },
  { key: "session", label: "Session" },
  { key: "subagent", label: "Subagent" },
  { key: "branch", label: "Branch" },
  { key: "developer", label: "Developer" },
  { key: "machine", label: "Machine" },
  { key: "agent", label: "Agent" },
];

export interface ExportTable {
  headers: string[];
  rows: string[][];
}

const cell = (value: RawValue): string => String(value ?? "");

function totalsCells(row: UsageTotals): string[] {
  return TOTALS_COLUMNS.map((c) => cell(c.raw(row)));
}

/**
 * One rectangular table, so it opens as a single sheet: the period's buckets, then every split.
 * The Section column says which slice each row belongs to; each section sums to the period total
 * on its own. The price date and the applied filters sit on every row, so a pasted range still
 * says which prices and which developer and project it covers.
 */
export function usageCsvTable(summary: UsageSummary): ExportTable {
  const context = [summary.pricesAsOf, summary.developer ?? "", summary.project ?? ""];
  const bucketRows = summary.buckets.map((b) => [
    `By ${summary.granularity}`,
    b.start,
    b.start,
    ...totalsCells(b),
    "",
    "",
    "",
    ...context,
  ]);
  const splitRows = SPLIT_DIMENSIONS.flatMap(({ key, label }) =>
    summary.splits[key].map((r) => [
      label,
      r.key ?? "",
      r.label,
      ...totalsCells(r),
      cell(r.costShare),
      r.firstAt,
      r.lastAt,
      ...context,
    ]),
  );
  return {
    headers: [
      "Section",
      "Key",
      "Name",
      ...TOTALS_COLUMNS.map((c) => c.header),
      "Share of cost",
      "First at",
      "Last at",
      "Prices as of",
      "Developer filter",
      "Project filter",
    ],
    rows: [...bucketRows, ...splitRows],
  };
}

export function usagePdfTable(buckets: readonly UsageBucket[]): ExportTable {
  return {
    headers: USAGE_COLUMNS.map((c) => c.header),
    rows: buckets.map((b) => USAGE_COLUMNS.map((c) => c.display(b))),
  };
}

export function usagePdfTitle(summary: UsageSummary): string {
  const filters = [
    summary.developer ? `developer ${summary.developer}` : null,
    summary.project ? `project ${summary.project}` : null,
  ].filter((f): f is string => f !== null);
  const scope = filters.length > 0 ? `, ${filters.join(", ")}` : "";
  return `Developer Overwatch - Usage ${summary.from} to ${summary.to}${scope} (API-equivalent estimate, prices as of ${summary.pricesAsOf})`;
}

type RawRow = Record<string, RawValue>;

function splitJsonRow(row: UsageSplitRow): RawRow {
  return {
    key: row.key ?? null,
    label: row.label,
    ...Object.fromEntries(TOTALS_COLUMNS.map((c) => [c.key, c.raw(row)])),
    costShare: row.costShare,
    firstAt: row.firstAt,
    lastAt: row.lastAt,
  };
}

export interface UsageJsonExport {
  from: string;
  to: string;
  granularity: UsageGranularity;
  timeZone: string;
  pricesAsOf: string;
  /** The filters the server applied; null means all. */
  developer: string | null;
  project: string | null;
  rows: RawRow[];
  splits: Record<keyof UsageSplits, RawRow[]>;
}

export function usageJsonExport(summary: UsageSummary): UsageJsonExport {
  return {
    from: summary.from,
    to: summary.to,
    granularity: summary.granularity,
    timeZone: summary.timeZone,
    pricesAsOf: summary.pricesAsOf,
    developer: summary.developer ?? null,
    project: summary.project ?? null,
    rows: summary.buckets.map((b) => Object.fromEntries(USAGE_COLUMNS.map((c) => [c.key, c.raw(b)]))),
    splits: Object.fromEntries(
      SPLIT_DIMENSIONS.map(({ key }) => [key, summary.splits[key].map(splitJsonRow)]),
    ) as Record<keyof UsageSplits, RawRow[]>,
  };
}
