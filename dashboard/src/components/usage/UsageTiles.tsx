import type { UsageBurn, UsageTotals } from "../../types";
import { formatCost } from "../../utils/tokenFormat";
import { formatPercent, formatPerMinuteUsd } from "../../utils/usageFormat";

interface TileProps {
  testId: string;
  label: string;
  value: string;
  detail?: string;
}

function Tile({ testId, label, value, detail }: TileProps) {
  return (
    <div data-testid={testId} className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
      <p className="text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400">{label}</p>
      <p className="text-xl font-semibold mt-1">{value}</p>
      {detail && <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{detail}</p>}
    </div>
  );
}

interface UsageTilesProps {
  totals: UsageTotals;
  burn: UsageBurn;
}

export default function UsageTiles({ totals, burn }: UsageTilesProps) {
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-7 gap-3">
      <Tile testId="tile-input" label="Input" value={totals.inputTokens.toLocaleString()} />
      <Tile testId="tile-output" label="Output" value={totals.outputTokens.toLocaleString()} />
      <Tile testId="tile-cache-read" label="Cache read" value={totals.cacheReadInputTokens.toLocaleString()} />
      <Tile testId="tile-cache-write" label="Cache write" value={totals.cacheCreationInputTokens.toLocaleString()} />
      <Tile testId="tile-cost" label="Total cost" value={formatCost(totals.estimatedCostUsd)} />
      <Tile testId="tile-hit-rate" label="Cache hit rate" value={formatPercent(totals.cacheHitRate)} />
      <Tile
        testId="tile-burn"
        label="Burn"
        value={`${Math.round(burn.tokensPerMinute).toLocaleString()} tokens/min`}
        detail={`${formatPerMinuteUsd(burn.costPerMinute)}/min · all token kinds, last ${burn.lookbackMinutes} min`}
      />
    </div>
  );
}
