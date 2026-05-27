import { useState } from "react";
import type { Session, ConversationTurn, ToolExecution, FileChange } from "../types";
import TimelineEvent from "./TimelineEvent";

interface TimelineEntry {
  timestamp: string;
  type: "turn" | "tool" | "file";
  turn?: ConversationTurn;
  tool?: ToolExecution;
  file?: FileChange;
}

function buildTimeline(session: Session): TimelineEntry[] {
  const entries: TimelineEntry[] = [];

  for (const turn of session.conversationTurns) {
    entries.push({ timestamp: turn.timestamp, type: "turn", turn });
  }

  for (const tool of session.toolExecutions) {
    entries.push({ timestamp: tool.timestamp, type: "tool", tool });
  }

  // FileChange has no timestamp. Use the parent tool execution's timestamp
  // if linked, otherwise fall back to session start time.
  const toolTimestampMap = new Map<string, string>();
  for (const tool of session.toolExecutions) {
    toolTimestampMap.set(tool.id, tool.timestamp);
  }

  for (const file of session.fileChanges) {
    const ts = file.toolExecutionId
      ? toolTimestampMap.get(file.toolExecutionId) ?? session.startedAt
      : session.startedAt;
    entries.push({ timestamp: ts, type: "file", file });
  }

  entries.sort(
    (a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime(),
  );

  return entries;
}

function TurnContent({ turn }: { turn: ConversationTurn }) {
  const [expanded, setExpanded] = useState(false);
  const content = turn.content ?? "";
  const truncateAt = 200;
  const needsTruncate = content.length > truncateAt;
  const displayContent =
    needsTruncate && !expanded ? content.slice(0, truncateAt) + "..." : content;

  return (
    <div className="text-sm">
      <div className="flex items-center gap-2 mb-1">
        <span title="Conversation turn">&#128172;</span>
        <span className="font-medium capitalize">{turn.role}</span>
        {turn.tokenCount != null && (
          <span className="text-xs text-gray-400">{turn.tokenCount} tokens</span>
        )}
      </div>
      {content && (
        <pre className="whitespace-pre-wrap text-gray-700 max-h-60 overflow-y-auto">
          {displayContent}
        </pre>
      )}
      {needsTruncate && (
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="text-xs text-blue-600 hover:underline mt-1"
        >
          {expanded ? "Show less" : "Show more"}
        </button>
      )}
    </div>
  );
}

function ToolContent({ tool }: { tool: ToolExecution }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="text-sm">
      <div className="flex items-center gap-2 mb-1">
        <span title="Tool execution">&#128295;</span>
        <span className="font-medium">{tool.toolName}</span>
        {tool.durationMs != null && (
          <span className="text-xs text-gray-400">{tool.durationMs}ms</span>
        )}
      </div>
      {expanded && (
        <div className="mt-2 space-y-2">
          {tool.inputParams && (
            <div>
              <div className="text-xs font-medium text-gray-500">Input</div>
              <pre className="text-xs bg-gray-100 p-2 rounded overflow-x-auto max-h-40 overflow-y-auto">
                {tool.inputParams}
              </pre>
            </div>
          )}
          {tool.outputSummary && (
            <div>
              <div className="text-xs font-medium text-gray-500">Output</div>
              <pre className="text-xs bg-gray-100 p-2 rounded overflow-x-auto max-h-40 overflow-y-auto">
                {tool.outputSummary}
              </pre>
            </div>
          )}
        </div>
      )}
      {(tool.inputParams || tool.outputSummary) && (
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="text-xs text-purple-600 hover:underline mt-1"
        >
          {expanded ? "Hide details" : "Show details"}
        </button>
      )}
    </div>
  );
}

function FileContent({ file }: { file: FileChange }) {
  const [diffExpanded, setDiffExpanded] = useState(false);
  const changeTypeStyles: Record<string, string> = {
    Created: "bg-green-100 text-green-700",
    Modified: "bg-blue-100 text-blue-700",
    Deleted: "bg-red-100 text-red-700",
  };

  return (
    <div className="text-sm">
      <div className="flex items-center gap-2">
        <span title="File change">&#128196;</span>
        <span className="font-mono text-xs">{file.filePath}</span>
        {file.changeType && (
          <span
            className={`text-xs px-1.5 py-0.5 rounded ${changeTypeStyles[file.changeType] ?? "bg-gray-100 text-gray-700"}`}
          >
            {file.changeType}
          </span>
        )}
      </div>
      {file.diffText && (
        <>
          <button
            type="button"
            onClick={() => setDiffExpanded(!diffExpanded)}
            className="text-xs text-green-600 hover:underline mt-1"
          >
            {diffExpanded ? "Hide diff" : "Show diff"}
          </button>
          {diffExpanded && (
            <pre className="mt-2 text-xs bg-gray-900 text-gray-100 p-3 rounded overflow-x-auto max-h-60 overflow-y-auto">
              {file.diffText}
            </pre>
          )}
        </>
      )}
    </div>
  );
}

export default function SessionTimeline({ session }: { session: Session }) {
  const entries = buildTimeline(session);

  if (entries.length === 0) {
    return (
      <div className="bg-white rounded-lg border p-4">
        <h3 className="font-semibold mb-3">Session Timeline</h3>
        <p className="text-gray-500 text-sm">No events recorded for this session.</p>
      </div>
    );
  }

  return (
    <div className="bg-white rounded-lg border p-4">
      <h3 className="font-semibold mb-4">Session Timeline</h3>
      {/* Timeline line */}
      <div className="relative border-l-2 border-gray-200 ml-1.5">
        {entries.map((entry, idx) => {
          const key =
            entry.turn?.id ?? entry.tool?.id ?? entry.file?.id ?? `evt-${idx}`;

          if (entry.type === "turn" && entry.turn) {
            return (
              <TimelineEvent
                key={key}
                type="turn"
                timestamp={entry.timestamp}
                isDecision={entry.turn.isDecision}
              >
                <TurnContent turn={entry.turn} />
              </TimelineEvent>
            );
          }

          if (entry.type === "tool" && entry.tool) {
            return (
              <TimelineEvent key={key} type="tool" timestamp={entry.timestamp}>
                <ToolContent tool={entry.tool} />
              </TimelineEvent>
            );
          }

          if (entry.type === "file" && entry.file) {
            return (
              <TimelineEvent key={key} type="file" timestamp={entry.timestamp}>
                <FileContent file={entry.file} />
              </TimelineEvent>
            );
          }

          return null;
        })}
      </div>
    </div>
  );
}
