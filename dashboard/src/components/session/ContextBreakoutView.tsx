import type { ContextBreakout } from "../../sessionInsightTypes";
import { categoryLabel, formatCompactTokens, formatFill } from "../../utils/contextFormat";
import MeterBar from "./MeterBar";
import { NotAvailable } from "./SessionSection";

function headerText(breakout: ContextBreakout): string {
  const measured = `${formatCompactTokens(breakout.measuredContextTokens)} / ${formatCompactTokens(breakout.windowTokens)}`;
  return breakout.fill == null ? measured : `${measured} · ${formatFill(breakout.fill)}`;
}

/**
 * What fills the context window, by category. Rows keep the server's order (largest first,
 * conversation last); each bar is the category's share of the categories' total.
 */
export default function ContextBreakoutView({ breakout }: { breakout: ContextBreakout | null | undefined }) {
  if (breakout == null) return <NotAvailable />;

  const total = breakout.categories.reduce((sum, c) => sum + c.tokens, 0);
  return (
    <div className="space-y-2">
      <p className="text-sm font-medium" title={breakout.model}>
        {headerText(breakout)}
      </p>
      <ul className="space-y-1.5">
        {breakout.categories.map((c) => {
          const label = categoryLabel(c);
          return (
            <li key={`${c.category}-${c.server ?? ""}`} className="text-sm">
              <div className="flex justify-between gap-2">
                <span>{label}</span>
                <span className="text-gray-500 dark:text-gray-400">{formatCompactTokens(c.tokens)}</span>
              </div>
              <MeterBar fraction={total > 0 ? c.tokens / total : 0} label={`${label} share`} colorClass="bg-blue-500" />
            </li>
          );
        })}
      </ul>
      <p className="text-xs text-gray-500 dark:text-gray-400">
        {`Estimated from Claude Code ${breakout.claudeCodeVersion} request at ${new Date(breakout.takenAt).toLocaleString()}`}
      </p>
    </div>
  );
}
