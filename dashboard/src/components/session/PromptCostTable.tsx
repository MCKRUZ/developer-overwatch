import type { PromptCost } from "../../sessionInsightTypes";
import { formatCost } from "../../utils/tokenFormat";
import { formatPercent } from "../../utils/usageFormat";
import { MUTED } from "./SessionSection";

const MARKED = 3;
const TH = "text-left px-4 py-3 font-medium";
const TD = "px-4 py-3 text-gray-500 dark:text-gray-400";

/**
 * Row position to cost rank (1-based) for the three most expensive prompts. Free prompts are never
 * marked and ties go to the earlier prompt. With three prompts or fewer every one would be a top three,
 * so nothing is marked.
 */
function topCostRanks(prompts: readonly PromptCost[]): ReadonlyMap<number, number> {
  if (prompts.length <= MARKED) return new Map();
  const ranked = prompts
    .map((p, position) => ({ position, cost: p.estimatedCostUsd }))
    .filter((p) => p.cost > 0)
    .sort((a, b) => b.cost - a.cost || a.position - b.position)
    .slice(0, MARKED);
  return new Map(ranked.map((p, i) => [p.position, i + 1]));
}

function PromptLabel({ prompt }: { prompt: PromptCost }) {
  if (prompt.index === 0) return <span className="italic">(before the first prompt)</span>;
  if (prompt.label == null) return <span className="italic">(no text captured)</span>;
  return <span title={prompt.label}>{prompt.label}</span>;
}

function subagentShare(prompt: PromptCost): string {
  return prompt.estimatedCostUsd > 0 ? formatPercent(prompt.subagentCostUsd / prompt.estimatedCostUsd) : "—";
}

function PromptRow({ prompt, rank }: { prompt: PromptCost; rank: number | undefined }) {
  return (
    <tr className="hover:bg-gray-50 dark:hover:bg-gray-700">
      <td className={TD}>{prompt.index === 0 ? "—" : prompt.index}</td>
      <td className="px-4 py-3">
        <PromptLabel prompt={prompt} />
      </td>
      <td className={TD}>{prompt.totalTokens.toLocaleString()}</td>
      <td className={TD}>
        {formatCost(prompt.estimatedCostUsd)}
        {rank !== undefined && (
          <span className="ml-2 inline-flex px-1.5 py-0.5 rounded text-xs font-medium bg-amber-100 text-amber-900 dark:bg-amber-900 dark:text-amber-100">
            {`#${rank} cost`}
          </span>
        )}
      </td>
      <td className={TD}>{subagentShare(prompt)}</td>
    </tr>
  );
}

/** What each prompt cost, including everything it set off up to the next prompt, in prompt order. */
export default function PromptCostTable({ prompts }: { prompts: readonly PromptCost[] }) {
  if (prompts.length === 0) return <p className={MUTED}>No prompts recorded.</p>;

  const ranks = topCostRanks(prompts);
  return (
    <div className="max-h-96 overflow-auto">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
          <tr>
            <th className={TH}>#</th>
            <th className={TH}>Prompt</th>
            <th className={TH}>Tokens</th>
            <th className={TH}>Est. cost</th>
            <th className={TH}>Subagent share</th>
          </tr>
        </thead>
        <tbody className="divide-y dark:divide-gray-700">
          {prompts.map((p, position) => (
            <PromptRow key={p.index} prompt={p} rank={ranks.get(position)} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
