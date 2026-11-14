import type { BudgetLevel } from "../../types";
import { levelLabel } from "../../utils/budgetFormat";

const BADGE_CLASS: Record<BudgetLevel, string> = {
  Ok: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200",
  Amber: "bg-amber-100 text-amber-900 dark:bg-amber-900 dark:text-amber-100",
  Red: "bg-red-100 text-red-800 dark:bg-red-900 dark:text-red-100",
};

/** The level in words, so amber and red never rely on colour alone. */
export default function LevelBadge({ level }: { level: BudgetLevel }) {
  return (
    <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${BADGE_CLASS[level]}`}>
      {levelLabel(level)}
    </span>
  );
}
