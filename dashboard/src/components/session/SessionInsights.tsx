import { useCallback } from "react";
import { api } from "../../api";
import { useCoalescedFetch, type CoalescedFetch } from "../../hooks/useCoalescedFetch";
import { useSessionLiveRefresh } from "../../hooks/useLiveRefresh";
import ContextPanel from "./ContextPanel";
import ContextBreakoutView from "./ContextBreakoutView";
import WatchList from "./WatchList";
import PromptCostTable from "./PromptCostTable";
import SubagentSplitTable from "./SubagentSplitTable";
import SessionSection from "./SessionSection";

/** Never show a previous session's figures while the next session's are loading. */
function current<T>(fetch: CoalescedFetch<T>): T | null {
  return fetch.isStale ? null : fetch.data;
}

/**
 * The session-level sections above the timeline. The context, prompt and subagent reads run
 * independently; the three context sections share one read and show its state each. All three
 * reload when this session's usage changes, so an open session's fill and costs keep moving.
 */
export default function SessionInsights({ sessionId }: { sessionId: string }) {
  const context = useCoalescedFetch(sessionId, () => api.sessions.context(sessionId));
  const prompts = useCoalescedFetch(sessionId, () => api.sessions.prompts(sessionId));
  const subagents = useCoalescedFetch(sessionId, () => api.sessions.subagentBreakdown(sessionId));
  const contextData = current(context);
  const { refresh: refreshContext } = context;
  const { refresh: refreshPrompts } = prompts;
  const { refresh: refreshSubagents } = subagents;
  const refreshAll = useCallback(() => {
    refreshContext();
    refreshPrompts();
    refreshSubagents();
  }, [refreshContext, refreshPrompts, refreshSubagents]);
  useSessionLiveRefresh(sessionId, refreshAll);

  return (
    <div className="space-y-4 mb-6">
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <SessionSection title="Context" noun="context" data={contextData} error={context.error}>
          {(data) => <ContextPanel context={data} />}
        </SessionSection>
        <SessionSection title="Context Window Breakout" noun="context" data={contextData} error={context.error}>
          {(data) => <ContextBreakoutView breakout={data.breakout} />}
        </SessionSection>
        <SessionSection title="Watch List" noun="context" data={contextData} error={context.error}>
          {(data) => <WatchList context={data} />}
        </SessionSection>
      </div>
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <SessionSection title="Cost per prompt" noun="prompts" data={current(prompts)} error={prompts.error}>
          {(data) => <PromptCostTable prompts={data} />}
        </SessionSection>
        <SessionSection title="Subagent split" noun="subagent split" data={current(subagents)} error={subagents.error}>
          {(data) => <SubagentSplitTable rows={data} />}
        </SessionSection>
      </div>
    </div>
  );
}
