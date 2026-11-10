import { useCallback, useEffect, useRef } from "react";
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
 * - with no events, the running list also refetches once it is `RUNNING_STALE_AFTER_MS` old, so
 *   idle times, the server's stale cutoff and burn rates keep moving. Under a stream it never fires.
 * Under a constant event stream that is 10 + 2 = 12 requests a minute (15 in the first minute,
 * which also carries the mount load and the first-connect resync). UsagePage.budget.test pins it.
 */
export const RUNNING_REFRESH_THROTTLE_MS = 6_000;
export const SUMMARY_REFRESH_INTERVAL_MS = 30_000;
export const RUNNING_STALE_AFTER_MS = 60_000;

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
  const staleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const running = useCoalescedFetch(paramsKey, () => {
    armStaleTimer();
    return api.usage.running();
  });
  const { refresh: refreshRunning } = running;

  // Re-armed by every running fetch, so it only fires after a full quiet minute. `refresh` is stable.
  function armStaleTimer() {
    if (staleTimerRef.current !== null) clearTimeout(staleTimerRef.current);
    staleTimerRef.current = setTimeout(refreshRunning, RUNNING_STALE_AFTER_MS);
  }
  useEffect(
    () => () => {
      if (staleTimerRef.current !== null) clearTimeout(staleTimerRef.current);
    },
    [],
  );

  const { refresh: refreshSummary } = summary;
  const requestRunning = useTrailingThrottle(refreshRunning, RUNNING_REFRESH_THROTTLE_MS);
  const resync = useCallback(() => {
    refreshSummary();
    requestRunning();
  }, [refreshSummary, requestRunning]);
  useLiveEvents({ onChange: requestRunning, onResync: resync });

  useEffect(() => {
    const timer = setInterval(refreshSummary, SUMMARY_REFRESH_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [refreshSummary]);

  // Data loaded for other params is dropped, so an old period's numbers never show under the new
  // period's label, not while the new one loads and not after it fails.
  return {
    summary: summary.isStale ? null : summary.data,
    running: running.isStale ? null : running.data,
    error: summary.error,
    runningError: running.error,
  };
}
