import type { PlanValue } from "../../types";
import { formatCost } from "../../utils/tokenFormat";
import { formatMultiple } from "../../utils/budgetFormat";

const HEADERS = ["Developer", "Plan", "Price / month", "API value month to date", "Multiple"];

export default function PlanValueTable({ values }: { values: readonly PlanValue[] }) {
  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
          <tr>
            {HEADERS.map((h) => (
              <th key={h} className="text-left px-4 py-3 font-medium">
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y dark:divide-gray-700">
          {values.map((v) => (
            <tr key={v.developerEmail}>
              <td className="px-4 py-3">{v.developerEmail}</td>
              <td className="px-4 py-3">{v.planName}</td>
              <td className="px-4 py-3">{formatCost(v.monthlyPriceUsd)}</td>
              <td className="px-4 py-3">{formatCost(v.apiValueUsd)}</td>
              <td className="px-4 py-3 font-medium">{formatMultiple(v.valueMultiple)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
