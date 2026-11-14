import type { BudgetLevel, BudgetStatus } from "../../types";
import { formatCost } from "../../utils/tokenFormat";
import {
  budgetWho,
  forecastText,
  formatUsedPercent,
  paceText,
  periodUnit,
  remainingText,
} from "../../utils/budgetFormat";
import LevelBadge from "./LevelBadge";

const ROW_CLASS: Record<BudgetLevel, string> = {
  Ok: "",
  Amber: "bg-amber-50 dark:bg-amber-950",
  Red: "bg-red-50 dark:bg-red-950",
};

const HEADERS = ["Who", "Period", "Budget", "Spent", "Remaining", "Used", "Pace", "Forecast", "Status"];

function BudgetRow({ status, now }: { status: BudgetStatus; now: Date }) {
  const { budget } = status;
  return (
    <tr className={ROW_CLASS[status.level]}>
      <td className="px-4 py-3">{budgetWho(budget)}</td>
      <td className="px-4 py-3">{budget.period}</td>
      <td className="px-4 py-3">{formatCost(budget.amountUsd)}</td>
      <td className="px-4 py-3">{formatCost(status.spentUsd)}</td>
      <td className="px-4 py-3">{remainingText(status.remainingUsd)}</td>
      <td className="px-4 py-3">{formatUsedPercent(status.usedFraction)}</td>
      <td className="px-4 py-3 text-gray-600 dark:text-gray-300">{paceText(status.pace, periodUnit(budget.period))}</td>
      <td className="px-4 py-3 text-gray-600 dark:text-gray-300">{forecastText(status.forecast, now)}</td>
      <td className="px-4 py-3">
        <LevelBadge level={status.level} />
      </td>
    </tr>
  );
}

export default function BudgetStatusTable({ statuses }: { statuses: readonly BudgetStatus[] }) {
  // Only decides whether a run-out reads as a time today or a later date; a refetch re-renders it.
  const now = new Date();
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
          {statuses.map((s) => (
            <BudgetRow key={s.budget.id} status={s} now={now} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
