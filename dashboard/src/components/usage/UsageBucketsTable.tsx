import type { UsageBucket } from "../../types";
import { USAGE_COLUMNS } from "../../utils/usageColumns";

export default function UsageBucketsTable({ buckets }: { buckets: readonly UsageBucket[] }) {
  if (buckets.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-gray-400">No usage in this period.</p>;
  }

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
          <tr>
            {USAGE_COLUMNS.map((c) => (
              <th key={c.key} className="text-left px-4 py-3 font-medium">
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y dark:divide-gray-700">
          {buckets.map((b) => (
            <tr key={b.start} className="hover:bg-gray-50 dark:hover:bg-gray-700">
              {USAGE_COLUMNS.map((c) => (
                <td key={c.key} className="px-4 py-3 text-gray-600 dark:text-gray-300">
                  {c.display(b)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
