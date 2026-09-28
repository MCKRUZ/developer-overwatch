import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../api";
import type { ConversationTurn, ToolExecution, FileChange } from "../types";

interface TimelineEntry {
  timestamp: string;
  type: "turn" | "tool" | "file";
  turn?: ConversationTurn;
  tool?: ToolExecution;
  file?: FileChange;
}

function buildTimeline(
  turns: ConversationTurn[],
  tools: ToolExecution[],
  files: FileChange[],
  toolTimestamps: Map<string, string>,
): TimelineEntry[] {
  const entries: TimelineEntry[] = [];

  for (const turn of turns) {
    entries.push({ timestamp: turn.timestamp, type: "turn", turn });
  }

  for (const tool of tools) {
    entries.push({ timestamp: tool.timestamp, type: "tool", tool });
  }

  for (const file of files) {
    const ts = (file.toolExecutionId && toolTimestamps.get(file.toolExecutionId)) || new Date().toISOString();
    entries.push({ timestamp: ts, type: "file", file });
  }

  entries.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());

  return entries;
}

// Consecutive tool/file entries between two conversation turns are the "noise" the user has to
// wade through to follow the actual conversation — folding them into one collapsible group per
// run is what actually fixes readability, rather than any per-row styling tweak.
type TimelineGroup =
  | { kind: "turn"; key: string; turn: ConversationTurn }
  | { kind: "tools"; key: string; tools: ToolExecution[]; filesByTool: Map<string, FileChange[]>; orphanFiles: FileChange[] };

function buildGroups(entries: TimelineEntry[]): TimelineGroup[] {
  const groups: TimelineGroup[] = [];
  let pendingTools: ToolExecution[] = [];
  let pendingFiles: FileChange[] = [];

  function flush() {
    if (pendingTools.length === 0 && pendingFiles.length === 0) return;
    const filesByTool = new Map<string, FileChange[]>();
    const orphanFiles: FileChange[] = [];
    for (const file of pendingFiles) {
      if (file.toolExecutionId) {
        const list = filesByTool.get(file.toolExecutionId) ?? [];
        list.push(file);
        filesByTool.set(file.toolExecutionId, list);
      } else {
        orphanFiles.push(file);
      }
    }
    groups.push({
      kind: "tools",
      key: `tools-${pendingTools[0]?.id ?? pendingFiles[0]?.id ?? groups.length}`,
      tools: pendingTools,
      filesByTool,
      orphanFiles,
    });
    pendingTools = [];
    pendingFiles = [];
  }

  for (const entry of entries) {
    if (entry.type === "turn" && entry.turn) {
      flush();
      groups.push({ kind: "turn", key: entry.turn.id, turn: entry.turn });
    } else if (entry.type === "tool" && entry.tool) {
      pendingTools.push(entry.tool);
    } else if (entry.type === "file" && entry.file) {
      pendingFiles.push(entry.file);
    }
  }
  flush();

  return groups;
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString();
}

function TurnBubble({
  turn,
  highlighted,
  bubbleRef,
}: {
  turn: ConversationTurn;
  highlighted: boolean;
  bubbleRef?: (el: HTMLDivElement | null) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const content = turn.content ?? "";
  const truncateAt = 400;
  const needsTruncate = content.length > truncateAt;
  const displayContent = needsTruncate && !expanded ? content.slice(0, truncateAt) + "..." : content;
  const isUser = turn.role === "user";
  const isSubagent = turn.role === "subagent";

  return (
    <div
      ref={bubbleRef}
      data-testid="timeline-turn"
      className={`rounded-lg p-3 text-sm border-l-4 ${
        isUser
          ? "bg-blue-50 dark:bg-blue-900/20 border-blue-400"
          : isSubagent
            ? "bg-violet-50 dark:bg-violet-900/20 border-violet-400"
            : "bg-gray-50 dark:bg-gray-700/40 border-gray-300 dark:border-gray-600"
      } ${highlighted ? "ring-2 ring-amber-400" : ""} ${turn.isDecision ? "outline outline-1 outline-yellow-400" : ""}`}
    >
      <div className="flex items-center gap-2 mb-1 flex-wrap">
        <span className="font-medium capitalize">{turn.role}</span>
        <span className="text-xs text-gray-400 dark:text-gray-500">{formatTime(turn.timestamp)}</span>
        {turn.isDecision && (
          <span className="text-xs bg-amber-100 text-amber-800 px-1.5 py-0.5 rounded font-medium">Decision</span>
        )}
        {isSubagent && turn.model && (
          <span className="text-xs bg-violet-100 text-violet-800 px-1.5 py-0.5 rounded font-mono">{turn.model}</span>
        )}
        {turn.tokenCount != null && (
          <span
            className="text-xs text-gray-400 dark:text-gray-500"
            title={
              turn.inputTokens != null || turn.outputTokens != null
                ? [
                    turn.inputTokens != null ? `${turn.inputTokens} in` : null,
                    turn.outputTokens != null ? `${turn.outputTokens} out` : null,
                    turn.cacheReadInputTokens ? `${turn.cacheReadInputTokens} cache read` : null,
                    turn.cacheCreationInputTokens ? `${turn.cacheCreationInputTokens} cache write` : null,
                  ]
                    .filter(Boolean)
                    .join(" · ")
                : undefined
            }
          >
            {turn.tokenCount} tokens
          </span>
        )}
        {turn.containsRedactedSecret && (
          <span
            title="Agent redacted a likely secret in this turn"
            className="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-red-600 text-white"
          >
            secret redacted
          </span>
        )}
      </div>
      {content && (
        <pre className="whitespace-pre-wrap text-gray-700 dark:text-gray-300 max-h-60 overflow-y-auto">
          {displayContent}
        </pre>
      )}
      {needsTruncate && (
        <button
          type="button"
          onClick={() => setExpanded(!expanded)}
          className="text-xs text-blue-600 dark:text-blue-400 hover:underline mt-1"
        >
          {expanded ? "Show less" : "Show more"}
        </button>
      )}
    </div>
  );
}

function ToolRow({
  tool,
  files,
  highlighted,
  rowRef,
}: {
  tool: ToolExecution;
  files: FileChange[];
  highlighted: boolean;
  rowRef?: (el: HTMLDivElement | null) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const changeTypeStyles: Record<string, string> = {
    Created: "bg-green-100 text-green-700",
    Modified: "bg-blue-100 text-blue-700",
    Deleted: "bg-red-100 text-red-700",
  };

  return (
    <div
      ref={rowRef}
      className={`text-xs px-2 py-1.5 rounded ${highlighted ? "ring-2 ring-amber-400" : ""}`}
    >
      <div className="flex items-center gap-2 flex-wrap">
        <span title="Tool execution">&#128295;</span>
        <span className="font-medium">{tool.toolName}</span>
        <span className="text-gray-400 dark:text-gray-500">{formatTime(tool.timestamp)}</span>
        {tool.durationMs != null && <span className="text-gray-400 dark:text-gray-500">{tool.durationMs}ms</span>}
        {(tool.inputParams || tool.outputSummary) && (
          <button
            type="button"
            onClick={() => setExpanded(!expanded)}
            className="text-purple-600 dark:text-purple-400 hover:underline"
          >
            {expanded ? "Hide details" : "Show details"}
          </button>
        )}
        {files.map((f) => (
          <span
            key={f.id}
            title={f.filePath}
            className={`px-1.5 py-0.5 rounded font-mono ${changeTypeStyles[f.changeType ?? ""] ?? "bg-gray-100 text-gray-700"}`}
          >
            {f.filePath.split(/[\\/]/).pop()}
          </span>
        ))}
      </div>
      {expanded && (
        <div className="mt-2 space-y-2">
          {tool.inputParams && (
            <div>
              <div className="font-medium text-gray-500 dark:text-gray-400">Input</div>
              <pre className="bg-gray-100 dark:bg-gray-700 p-2 rounded overflow-x-auto max-h-40 overflow-y-auto dark:text-gray-200">
                {tool.inputParams}
              </pre>
            </div>
          )}
          {tool.outputSummary && (
            <div>
              <div className="font-medium text-gray-500 dark:text-gray-400">Output</div>
              <pre className="bg-gray-100 dark:bg-gray-700 p-2 rounded overflow-x-auto max-h-40 overflow-y-auto dark:text-gray-200">
                {tool.outputSummary}
              </pre>
            </div>
          )}
          {files.map((f) =>
            f.diffText ? (
              <div key={f.id}>
                <div className="font-medium text-gray-500 dark:text-gray-400">{f.filePath} diff</div>
                <pre className="bg-gray-900 text-gray-100 p-2 rounded overflow-x-auto max-h-60 overflow-y-auto">
                  {f.diffText}
                </pre>
              </div>
            ) : null,
          )}
        </div>
      )}
    </div>
  );
}

function FileRow({ file }: { file: FileChange }) {
  const [diffExpanded, setDiffExpanded] = useState(false);
  const changeTypeStyles: Record<string, string> = {
    Created: "bg-green-100 text-green-700",
    Modified: "bg-blue-100 text-blue-700",
    Deleted: "bg-red-100 text-red-700",
  };

  return (
    <div className="text-xs px-2 py-1.5 rounded">
      <div className="flex items-center gap-2">
        <span title="File change">&#128196;</span>
        <span className="font-mono">{file.filePath}</span>
        {file.changeType && (
          <span className={`px-1.5 py-0.5 rounded ${changeTypeStyles[file.changeType] ?? "bg-gray-100 text-gray-700"}`}>
            {file.changeType}
          </span>
        )}
        {file.diffText && (
          <button
            type="button"
            onClick={() => setDiffExpanded(!diffExpanded)}
            className="text-green-600 dark:text-green-400 hover:underline"
          >
            {diffExpanded ? "Hide diff" : "Show diff"}
          </button>
        )}
      </div>
      {diffExpanded && file.diffText && (
        <pre className="mt-2 bg-gray-900 text-gray-100 p-2 rounded overflow-x-auto max-h-60 overflow-y-auto">
          {file.diffText}
        </pre>
      )}
    </div>
  );
}

function ToolGroupCard({
  group,
  highlightedToolId,
  defaultExpanded,
  toolRowRef,
}: {
  group: Extract<TimelineGroup, { kind: "tools" }>;
  highlightedToolId?: string;
  defaultExpanded: boolean;
  toolRowRef?: (el: HTMLDivElement | null) => void;
}) {
  const [expanded, setExpanded] = useState(defaultExpanded);
  const toolNames = [...new Set(group.tools.map((t) => t.toolName))];
  const totalDuration = group.tools.reduce((sum, t) => sum + (t.durationMs ?? 0), 0);
  const containsHighlight = group.tools.some((t) => t.id === highlightedToolId);
  const fileCount =
    [...group.filesByTool.values()].reduce((sum, list) => sum + list.length, 0) + group.orphanFiles.length;
  const countLabel =
    group.tools.length > 0
      ? `${group.tools.length} tool call${group.tools.length === 1 ? "" : "s"}`
      : `${fileCount} file change${fileCount === 1 ? "" : "s"}`;

  return (
    <div
      className={`rounded-lg border bg-gray-50/60 dark:bg-gray-800/40 ${
        containsHighlight && !expanded
          ? "ring-2 ring-amber-400 border-transparent"
          : "border-gray-200 dark:border-gray-700"
      }`}
    >
      <button
        type="button"
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center gap-2 px-3 py-2 text-xs text-gray-500 dark:text-gray-400 text-left"
      >
        <span>{group.tools.length > 0 ? "\u{1F527}" : "\u{1F4C4}"}</span>
        <span className="font-medium">{countLabel}</span>
        <span className="truncate">{toolNames.slice(0, 4).join(", ")}{toolNames.length > 4 ? "…" : ""}</span>
        {totalDuration > 0 && <span className="ml-auto shrink-0">{totalDuration}ms total</span>}
        <span className="shrink-0">{expanded ? "▼" : "▶"}</span>
      </button>
      {expanded && (
        <div className="px-2 pb-2 space-y-1 divide-y divide-gray-100 dark:divide-gray-700/50">
          {group.tools.map((tool) => (
            <ToolRow
              key={tool.id}
              tool={tool}
              files={group.filesByTool.get(tool.id) ?? []}
              highlighted={tool.id === highlightedToolId}
              rowRef={tool.id === highlightedToolId ? toolRowRef : undefined}
            />
          ))}
          {group.orphanFiles.length > 0 && (
            <div className="pt-1 space-y-1">
              {group.orphanFiles.map((f) => <FileRow key={f.id} file={f} />)}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export default function SessionTimeline({ sessionId }: { sessionId: string }) {
  const [searchParams] = useSearchParams();
  const anchorTurnId = searchParams.get("turn") ?? undefined;
  const anchorToolId = searchParams.get("tool") ?? undefined;
  const isAnchored = Boolean(anchorTurnId || anchorToolId);

  const [turns, setTurns] = useState<ConversationTurn[]>([]);
  const [tools, setTools] = useState<ToolExecution[]>([]);
  const [files, setFiles] = useState<FileChange[]>([]);
  const [nextCursor, setNextCursor] = useState<string | undefined>(undefined);
  const [hasMore, setHasMore] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const scrollRef = useRef<HTMLDivElement | null>(null);
  const sentinelRef = useRef<HTMLDivElement | null>(null);
  const loadingMoreRef = useRef(false);
  const anchorElRef = useRef<HTMLDivElement | null>(null);
  const scrolledToAnchorRef = useRef(false);

  useEffect(() => {
    setLoading(true);
    scrolledToAnchorRef.current = false;
    const fetcher = isAnchored
      ? api.sessions.timelineAround(sessionId, { turnId: anchorTurnId, toolExecutionId: anchorToolId })
      : api.sessions.timeline(sessionId);

    fetcher
      .then((page) => {
        setTurns(page.conversationTurns);
        setTools(page.toolExecutions);
        setFiles(page.fileChanges);
        setNextCursor(page.nextCursor);
        setHasMore(page.hasMore);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessionId, anchorTurnId, anchorToolId]);

  const loadOlderPage = useCallback(() => {
    if (loadingMoreRef.current || !hasMore || !nextCursor) return;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    api.sessions
      .timeline(sessionId, nextCursor)
      .then((page) => {
        setTurns((prev) => [...page.conversationTurns, ...prev]);
        setTools((prev) => [...page.toolExecutions, ...prev]);
        setFiles((prev) => [...page.fileChanges, ...prev]);
        setNextCursor(page.nextCursor);
        setHasMore(page.hasMore);
      })
      .catch(() => {
        // best-effort — leave hasMore as-is so the user can retry by scrolling again
      })
      .finally(() => {
        loadingMoreRef.current = false;
        setLoadingMore(false);
      });
  }, [sessionId, nextCursor, hasMore]);

  useEffect(() => {
    const sentinel = sentinelRef.current;
    const root = scrollRef.current;
    if (!sentinel || !root || loading) return;

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) loadOlderPage();
      },
      { root, threshold: 0.1 },
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, [loading, loadOlderPage]);

  const toolTimestamps = useMemo(() => new Map(tools.map((t) => [t.id, t.timestamp])), [tools]);
  const entries = useMemo(
    () => buildTimeline(turns, tools, files, toolTimestamps),
    [turns, tools, files, toolTimestamps],
  );
  const groups = useMemo(() => buildGroups(entries), [entries]);

  useEffect(() => {
    if (loading || scrolledToAnchorRef.current || !isAnchored) return;
    if (!anchorElRef.current) return;
    anchorElRef.current.scrollIntoView({ block: "center" });
    scrolledToAnchorRef.current = true;
  }, [loading, groups, isAnchored]);

  if (loading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
        <h3 className="font-semibold mb-3">Session Timeline</h3>
        <p className="text-gray-500 dark:text-gray-400 text-sm">Loading timeline...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
        <h3 className="font-semibold mb-3">Session Timeline</h3>
        <p className="text-red-600 text-sm">Error: {error}</p>
      </div>
    );
  }

  if (entries.length === 0) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
        <h3 className="font-semibold mb-3">Session Timeline</h3>
        <p className="text-gray-500 dark:text-gray-400 text-sm">No events recorded for this session.</p>
      </div>
    );
  }

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
      <div className="flex items-center justify-between mb-4">
        <h3 className="font-semibold">Session Timeline</h3>
        {isAnchored && (
          <span className="text-xs text-amber-600 dark:text-amber-400">Jumped to flagged item</span>
        )}
      </div>
      <div ref={scrollRef} className="max-h-[32rem] overflow-y-auto pr-2 space-y-2">
        {groups.map((group) => {
          if (group.kind === "turn") {
            const highlighted = group.turn.id === anchorTurnId;
            return (
              <TurnBubble
                key={group.key}
                turn={group.turn}
                highlighted={highlighted}
                bubbleRef={highlighted ? (el) => (anchorElRef.current = el) : undefined}
              />
            );
          }
          const containsAnchorTool = group.tools.some((t) => t.id === anchorToolId);
          return (
            <ToolGroupCard
              key={group.key}
              group={group}
              highlightedToolId={anchorToolId}
              defaultExpanded={containsAnchorTool}
              toolRowRef={containsAnchorTool ? (el) => (anchorElRef.current = el) : undefined}
            />
          );
        })}
        {hasMore && (
          <div className="text-center text-xs text-gray-400 dark:text-gray-500 py-2">
            {loadingMore ? "Loading older events..." : "Scroll down for older events"}
          </div>
        )}
        <div ref={sentinelRef} data-testid="timeline-sentinel" />
      </div>
    </div>
  );
}
