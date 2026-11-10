import { useCallback } from "react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import { useCoalescedFetch } from "./useCoalescedFetch";
import { useLiveRefresh } from "./useLiveRefresh";

/**
 * Loads the usage summary for `params` and the running sessions, and refetches both on the hub's
 * live events. A failed running list leaves the summary on screen; a response for superseded
 * params never overwrites a newer one, and a live tick never discards a response in flight.
 */
export function useUsageData(params: UsageSummaryParams) {
  // Callers build a fresh params object each render; key on its content so only a real change refetches.
  const paramsKey = JSON.stringify(params);
  const summary = useCoalescedFetch(paramsKey, () => api.usage.summary(params));
  const running = useCoalescedFetch(paramsKey, () => api.usage.running());

  const { refresh: refreshSummary } = summary;
  const { refresh: refreshRunning } = running;
  const refetch = useCallback(() => {
    refreshSummary();
    refreshRunning();
  }, [refreshSummary, refreshRunning]);
  useLiveRefresh(refetch);

  return {
    summary: summary.data,
    running: running.data ?? [],
    error: summary.error,
    runningError: running.error,
  };
}
