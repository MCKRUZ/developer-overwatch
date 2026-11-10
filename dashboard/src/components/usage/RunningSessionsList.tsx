import { Link } from "react-router-dom";
import type { RunningSession } from "../../types";
import { formatCost } from "../../utils/tokenFormat";
import { formatPerMinuteUsd, runningStateLabel } from "../../utils/usageFormat";

const STATE_CLASS: Record<string, string> = {
  active: "bg-green-100 text-green-800 dark:bg-green-900 dark:text-green-200",
  waiting: "bg-yellow-100 text-yellow-800 dark:bg-yellow-900 dark:text-yellow-200",
};
const IDLE_CLASS = "bg-gray-100 text-gray-700 dark:bg-gray-700 dark:text-gray-200";

function sessionTokens(s: RunningSession): number {
  return s.inputTokens + s.outputTokens + s.cacheCreationInputTokens + s.cacheReadInputTokens;
}

function RunningRow({ session }: { session: RunningSession }) {
  const state = runningStateLabel(session);
  return (
    <tr className="hover:bg-gray-50 dark:hover:bg-gray-700">
      <td className="px-4 py-3">
        <Link to={`/sessions/${session.id}`} title={session.id} className="text-blue-600 dark:text-blue-400 hover:underline">
          {session.project ?? session.id.slice(0, 8)}
        </Link>
      </td>
      <td className="px-4 py-3">
        <span className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${STATE_CLASS[state] ?? IDLE_CLASS}`}>
          {state}
        </span>
      </td>
      <td className="px-4 py-3" title={session.developerEmail}>
        {session.developerName ?? session.developerEmail}
      </td>
      <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{session.machineName ?? "unknown"}</td>
      <td className="px-4 py-3 text-gray-500 dark:text-gray-400">
        {`${Math.round(session.burnTokensPerMinute).toLocaleString()} tokens/min (${formatPerMinuteUsd(session.burnCostPerMinute)}/min)`}
      </td>
      <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{sessionTokens(session).toLocaleString()}</td>
      <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatCost(session.estimatedCostUsd)}</td>
      <td className="px-4 py-3 text-gray-500 dark:text-gray-400">
        {session.currentContextTokens != null ? session.currentContextTokens.toLocaleString() : "—"}
      </td>
    </tr>
  );
}

export default function RunningSessionsList({ sessions }: { sessions: readonly RunningSession[] }) {
  if (sessions.length === 0) {
    return <p className="text-sm text-gray-500 dark:text-gray-400">No sessions running.</p>;
  }

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-x-auto">
      <p className="px-4 pt-3 text-sm font-medium">{`${sessions.length} running`}</p>
      <table className="w-full text-sm">
        <thead className="bg-gray-50 dark:bg-gray-700 border-y dark:border-gray-600">
          <tr>
            <th className="text-left px-4 py-3 font-medium">Project</th>
            <th className="text-left px-4 py-3 font-medium">State</th>
            <th className="text-left px-4 py-3 font-medium">Developer</th>
            <th className="text-left px-4 py-3 font-medium">Machine</th>
            <th className="text-left px-4 py-3 font-medium">Burn (15 min)</th>
            <th className="text-left px-4 py-3 font-medium">Tokens so far</th>
            <th className="text-left px-4 py-3 font-medium">Est. cost</th>
            <th className="text-left px-4 py-3 font-medium">Context</th>
          </tr>
        </thead>
        <tbody className="divide-y dark:divide-gray-700">
          {sessions.map((s) => (
            <RunningRow key={s.id} session={s} />
          ))}
        </tbody>
      </table>
    </div>
  );
}
