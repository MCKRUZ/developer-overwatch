import type { SubagentUsage } from "../../sessionInsightTypes";
import { formatCost } from "../../utils/tokenFormat";
import { formatPercent } from "../../utils/usageFormat";
import { MUTED } from "./SessionSection";

const TH = "text-left px-4 py-3 font-medium";
const TD = "px-4 py-3 text-gray-500 dark:text-gray-400";

/** The session's usage by subagent, with the main thread as its own row; rows sum to the session. */
export default function SubagentSplitTable({ rows }: { rows: readonly SubagentUsage[] }) {
  if (rows.length === 0) return <p className={MUTED}>No usage recorded.</p>;

  const sessionCost = rows.reduce((sum, r) => sum + r.estimatedCostUsd, 0);
  return (
    <table className="w-full text-sm">
      <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
        <tr>
          <th className={TH}>Name</th>
          <th className={TH}>Tokens</th>
          <th className={TH}>Est. cost</th>
          <th className={TH}>Share of session cost</th>
        </tr>
      </thead>
      <tbody className="divide-y dark:divide-gray-700">
        {rows.map((r) => (
          <tr key={r.subagentName ?? ""} className="hover:bg-gray-50 dark:hover:bg-gray-700">
            <td className="px-4 py-3">{r.subagentName ?? "main thread"}</td>
            <td className={TD}>{r.totalTokens.toLocaleString()}</td>
            <td className={TD}>{formatCost(r.estimatedCostUsd)}</td>
            <td className={TD}>{sessionCost > 0 ? formatPercent(r.estimatedCostUsd / sessionCost) : "—"}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
