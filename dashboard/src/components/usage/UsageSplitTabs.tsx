import { useState } from "react";
import type { UsageSplitRow, UsageSplits } from "../../types";
import { formatCost } from "../../utils/tokenFormat";
import { formatPercent, formatSpan } from "../../utils/usageFormat";

const TABS: readonly { key: keyof UsageSplits; label: string }[] = [
  { key: "model", label: "Model" },
  { key: "project", label: "Project" },
  { key: "session", label: "Session" },
  { key: "subagent", label: "Subagent" },
  { key: "branch", label: "Branch" },
  { key: "developer", label: "Developer" },
  { key: "machine", label: "Machine" },
  { key: "agent", label: "Agent" },
];

function SplitTable({ rows, showDuration }: { rows: readonly UsageSplitRow[]; showDuration: boolean }) {
  if (rows.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-gray-400 p-4">No usage in this period.</p>;
  }

  return (
    <table className="w-full text-sm">
      <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
        <tr>
          <th className="text-left px-4 py-3 font-medium">Name</th>
          {showDuration && <th className="text-left px-4 py-3 font-medium">Duration</th>}
          <th className="text-left px-4 py-3 font-medium">Tokens</th>
          <th className="text-left px-4 py-3 font-medium">Est. cost</th>
          <th className="text-left px-4 py-3 font-medium">Share of cost</th>
        </tr>
      </thead>
      <tbody className="divide-y dark:divide-gray-700">
        {rows.map((row, index) => (
          // `key` is omitted for the main thread and unknown values, so it cannot be the React key alone.
          <tr key={row.key ?? `${row.label}-${index}`} className="hover:bg-gray-50 dark:hover:bg-gray-700">
            <td className="px-4 py-3" title={row.key}>
              {row.label}
            </td>
            {showDuration && (
              <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatSpan(row.firstAt, row.lastAt)}</td>
            )}
            <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{row.totalTokens.toLocaleString()}</td>
            <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatCost(row.estimatedCostUsd)}</td>
            <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatPercent(row.costShare)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default function UsageSplitTabs({ splits }: { splits: UsageSplits }) {
  const [active, setActive] = useState<keyof UsageSplits>("model");

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-x-auto">
      <div role="tablist" className="flex flex-wrap gap-1 border-b dark:border-gray-700 px-2 pt-2">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-selected={active === tab.key}
            onClick={() => setActive(tab.key)}
            className={`px-3 py-1.5 rounded-t text-sm font-medium ${
              active === tab.key
                ? "bg-gray-100 dark:bg-gray-700 text-gray-900 dark:text-white"
                : "text-gray-500 dark:text-gray-400 hover:text-gray-900 dark:hover:text-white"
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div role="tabpanel">
        <SplitTable rows={splits[active]} showDuration={active === "session"} />
      </div>
    </div>
  );
}
