import { useState } from "react";
import type { SessionContext, UnusedToolGroup } from "../../sessionInsightTypes";
import { formatCompactTokens } from "../../utils/contextFormat";
import { MUTED, NotAvailable } from "./SessionSection";

const VISIBLE_UNUSED = 10;

const TAG_CLASS = "inline-flex px-1.5 py-0.5 rounded text-xs font-medium";
const MCP_TAG = `${TAG_CLASS} bg-purple-100 text-purple-800 dark:bg-purple-900 dark:text-purple-100`;
const BUILT_IN_TAG = `${TAG_CLASS} bg-gray-100 text-gray-700 dark:bg-gray-700 dark:text-gray-200`;

function UnusedRow({ tool }: { tool: UnusedToolGroup }) {
  return (
    <li className="flex items-center gap-2 text-sm" title={`${tool.toolCount} ${tool.toolCount === 1 ? "tool" : "tools"}`}>
      <span className={tool.isMcp ? MCP_TAG : BUILT_IN_TAG}>{tool.isMcp ? "MCP" : "built-in"}</span>
      <span>{`${tool.group}: unused · ${formatCompactTokens(tool.tokensPerRequest)}/request`}</span>
    </li>
  );
}

/** The server sorts by per-request cost, largest first, so the first ten are the costliest. */
function UnusedTools({ tools }: { tools: readonly UnusedToolGroup[] }) {
  const [expanded, setExpanded] = useState(false);
  if (tools.length === 0) return <p className={MUTED}>No unused tools</p>;

  const hidden = expanded ? 0 : Math.max(0, tools.length - VISIBLE_UNUSED);
  const visible = hidden > 0 ? tools.slice(0, VISIBLE_UNUSED) : tools;
  return (
    <>
      <ul className="space-y-1">
        {visible.map((tool) => (
          <UnusedRow key={`${tool.isMcp ? "mcp" : "builtin"}-${tool.group}`} tool={tool} />
        ))}
      </ul>
      {hidden > 0 && (
        <button
          type="button"
          onClick={() => setExpanded(true)}
          className="text-sm text-blue-600 dark:text-blue-400 hover:underline"
        >
          {`and ${hidden} more`}
        </button>
      )}
    </>
  );
}

function toolErrorText(context: SessionContext): string {
  const count = context.toolErrorCount;
  if (count === 0) return "No tool errors";
  const counted = `${count} tool ${count === 1 ? "error" : "errors"}`;
  return context.lastToolErrorName == null ? counted : `${counted} · last: ${context.lastToolErrorName} failed`;
}

/** Tool definitions paid for on every request but never called, and the session's tool failures. */
export default function WatchList({ context }: { context: SessionContext }) {
  const lastErrorAt = context.lastToolErrorAt == null ? undefined : new Date(context.lastToolErrorAt).toLocaleString();
  return (
    <div className="space-y-2">
      {context.unusedTools == null ? <NotAvailable /> : <UnusedTools tools={context.unusedTools} />}
      <p className="text-sm" title={lastErrorAt}>
        {toolErrorText(context)}
      </p>
    </div>
  );
}
