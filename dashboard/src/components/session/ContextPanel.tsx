import type { BudgetLevel } from "../../types";
import type { ContextCompaction, SessionContext } from "../../sessionInsightTypes";
import { contextLevel, formatCompactTokens, formatFill } from "../../utils/contextFormat";
import LevelBadge from "../usage/LevelBadge";
import MeterBar from "./MeterBar";
import { MUTED } from "./SessionSection";

const FILL_CLASS: Record<BudgetLevel, string> = {
  Ok: "bg-green-500",
  Amber: "bg-amber-500",
  Red: "bg-red-500",
};

function CurrentContext({ context }: { context: SessionContext }) {
  const { currentContextTokens: tokens, currentWindowTokens: windowTokens, currentFill: fill } = context;
  if (tokens == null) return <p className="text-sm">Context: not measured yet</p>;
  if (fill == null || windowTokens == null) {
    return <p className="text-sm">{`Context: ${formatCompactTokens(tokens)}`}</p>;
  }

  const level = contextLevel(fill);
  return (
    <>
      <div className="flex items-center gap-2">
        <p className="text-sm font-medium" title={context.currentModel ?? undefined}>
          {`Context: ${formatCompactTokens(tokens)} / ${formatCompactTokens(windowTokens)} (${formatFill(fill)})`}
        </p>
        {level !== "Ok" && <LevelBadge level={level} />}
      </div>
      <MeterBar fraction={fill} label="Context fill" colorClass={FILL_CLASS[level]} />
    </>
  );
}

function PeakContext({ context }: { context: SessionContext }) {
  const { peakContextTokens: tokens, peakFill: fill } = context;
  if (tokens == null) return null;
  const text = fill == null ? `Peak: ${formatCompactTokens(tokens)}` : `Peak: ${formatCompactTokens(tokens)} (${formatFill(fill)})`;
  return (
    <p className={MUTED} title={context.peakModel ?? undefined}>
      {text}
    </p>
  );
}

/** `compactions` is newest first; "last at" is how big the context was when the newest one ran. */
function compactionText(compactions: readonly ContextCompaction[]): string | null {
  const [latest] = compactions;
  if (!latest) return null;
  return `Compacted ${compactions.length}× · last at ${formatCompactTokens(latest.preTokens)} (${latest.trigger})`;
}

/** How full the context window is now and at its peak, against the configured window sizes. */
export default function ContextPanel({ context }: { context: SessionContext }) {
  const compactions = compactionText(context.breakout?.compactions ?? []);
  return (
    <div className="space-y-2">
      <CurrentContext context={context} />
      <PeakContext context={context} />
      {compactions && <p className={MUTED}>{compactions}</p>}
      <p className="text-xs text-gray-500 dark:text-gray-400">{`estimate · windows as of ${context.windowsAsOf}`}</p>
    </div>
  );
}
