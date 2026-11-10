import { useCallback, useEffect } from "react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import { useCoalescedFetch } from "./useCoalescedFetch";
import { useLiveEvents, useTrailingThrottle } from "./useLiveRefresh";

/**
 * Request budget per Usage tab (rate limit is per IP, so tabs add up):
 * - the running list refetches on live events, at most once per `RUNNING_REFRESH_THROTTLE_MS`;
 * - the summary refetches on a params change, a hub (re)connect, and every
 *   `SUMMARY_REFRESH_INTERVAL_MS`. Events never trigger it: one turn moves the period totals
 *   by cents, and the interval also lets the burn tile decay once activity stops.
 * Under a constant event stream that is 10 + 2 = 12 requests a minute (15 in the first minute,
 * which also carries the mount load and the first-connect resync). UsagePage.budget.test pins it.
 */
export const RUNNING_REFRESH_THROTTLE_MS = 6_000;
export const SUMMARY_REFRESH_INTERVAL_MS = 30_000;

/**
 * Loads the usage summary for `params` and the running sessions, and keeps both current from
 * the hub within the budget above. A failed running list leaves the summary on screen; a
 * response for superseded params never overwrites a newer one, and a live tick never discards
 * a response in flight.
 */
export function useUsageData(params: UsageSummaryParams) {
  // Callers build a fresh params object each render; key on its content so only a real change refetches.
  const paramsKey = JSON.stringify(params);
  const summary = useCoalescedFetch(paramsKey, () => api.usage.summary(params));
  const running = useCoalescedFetch(paramsKey, () => api.usage.running());

  const { refresh: refreshSummary } = summary;
  const requestRunning = useTrailingThrottle(running.refresh, RUNNING_REFRESH_THROTTLE_MS);
  const resync = useCallback(() => {
    refreshSummary();
    requestRunning();
  }, [refreshSummary, requestRunning]);
  useLiveEvents({ onChange: requestRunning, onResync: resync });

  useEffect(() => {
    const timer = setInterval(refreshSummary, SUMMARY_REFRESH_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [refreshSummary]);

  return {
    summary: summary.data,
    running: running.data ?? [],
    error: summary.error,
    runningError: running.error,
  };
}
