import { Link } from "react-router-dom";
import type { BudgetLevel, DeveloperWindow, Forecast, Pace, UsageWindow } from "../../types";
import { useNow } from "../../hooks/useNow";
import { formatCost } from "../../utils/tokenFormat";
import { forecastText, formatUsedPercent, paceText, resetText } from "../../utils/budgetFormat";
import LevelBadge from "./LevelBadge";

const COUNTDOWN_TICK_MS = 60_000;

const CARD_BORDER: Record<BudgetLevel, string> = {
  Ok: "dark:border-gray-700",
  Amber: "border-amber-400 border-2",
  Red: "border-red-500 border-2",
};

const MUTED = "text-sm text-gray-600 dark:text-gray-300";

type LimitedUsageWindow = UsageWindow & { usedFraction: number; level: BudgetLevel; pace: Pace; forecast: Forecast };

/** The server sends usedFraction, level, pace and forecast together: all set with a limit, all null without. */
function isLimited(window: UsageWindow): window is LimitedUsageWindow {
  return window.usedFraction !== null;
}

function LimitedWindow({ window, limitUsd, now }: { window: LimitedUsageWindow; limitUsd: number | null; now: Date }) {
  return (
    <>
      <div className="flex items-center gap-2">
        <span className="text-xl font-semibold">{`${formatUsedPercent(window.usedFraction)} used`}</span>
        <LevelBadge level={window.level} />
      </div>
      <p className={MUTED}>{`${formatCost(window.spentUsd)} of ${formatCost(limitUsd ?? undefined)}`}</p>
      <p className={MUTED}>{paceText(window.pace, "window")}</p>
      <p className={MUTED}>{forecastText(window.forecast, now)}</p>
    </>
  );
}

function UnlimitedWindow({ window }: { window: UsageWindow }) {
  return (
    <>
      <p className="text-xl font-semibold">{`${formatCost(window.spentUsd)} so far`}</p>
      <Link to="/settings" className="text-sm text-blue-600 dark:text-blue-400 hover:underline">
        Set a plan limit
      </Link>
    </>
  );
}

function WindowCard({ item, now }: { item: DeveloperWindow; now: Date }) {
  // Past its reset the window has closed; the next turn opens a new one.
  const open = item.window && new Date(item.window.resetsAtUtc) > now ? item.window : null;
  const level = open?.level ?? "Ok";
  return (
    <li
      aria-label={`5-hour window for ${item.developerEmail}`}
      className={`bg-white dark:bg-gray-800 rounded-lg border p-4 space-y-1 ${CARD_BORDER[level]}`}
    >
      <p className="font-medium">{item.developerEmail}</p>
      <p className="text-xs text-gray-500 dark:text-gray-400">{item.planName ?? "No plan"}</p>
      {!open ? (
        <p className={MUTED}>No usage in the current window</p>
      ) : (
        <>
          {isLimited(open) ? (
            <LimitedWindow window={open} limitUsd={item.windowLimitUsd} now={now} />
          ) : (
            <UnlimitedWindow window={open} />
          )}
          <p className="text-xs text-gray-500 dark:text-gray-400">{resetText(open.resetsAtUtc, now)}</p>
        </>
      )}
    </li>
  );
}

export default function UsageWindowCards({ windows }: { windows: readonly DeveloperWindow[] }) {
  const now = useNow(COUNTDOWN_TICK_MS);
  return (
    <ul className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
      {windows.map((w) => (
        <WindowCard key={w.developerEmail} item={w} now={now} />
      ))}
    </ul>
  );
}
