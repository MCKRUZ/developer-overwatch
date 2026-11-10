import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import type { RunningSession, UsageSummary } from "../types";
import { useLiveRefresh } from "./useLiveRefresh";

/**
 * Loads the usage summary for `params` and the running sessions, and refetches both on the hub's
 * live events. A failed running list leaves the summary on screen; a stale response never
 * overwrites a newer one.
 */
export function useUsageData(params: UsageSummaryParams) {
  const [summary, setSummary] = useState<UsageSummary | null>(null);
  const [running, setRunning] = useState<RunningSession[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [runningError, setRunningError] = useState<string | null>(null);
  const [refreshTick, setRefreshTick] = useState(0);

  const refetch = useCallback(() => setRefreshTick((tick) => tick + 1), []);
  useLiveRefresh(refetch);

  // Callers build a fresh params object each render; key on its content so only a real change refetches.
  const paramsKey = JSON.stringify(params);
  const stableParams = useMemo(() => params, [paramsKey]);

  useEffect(() => {
    let superseded = false;

    api.usage
      .summary(stableParams)
      .then((result) => {
        if (superseded) return;
        setSummary(result);
        setError(null);
      })
      .catch((e: Error) => {
        if (!superseded) setError(e.message);
      });

    api.usage
      .running()
      .then((rows) => {
        if (superseded) return;
        setRunning(rows);
        setRunningError(null);
      })
      .catch((e: Error) => {
        if (!superseded) setRunningError(e.message);
      });

    return () => {
      superseded = true;
    };
  }, [stableParams, refreshTick]);

  return { summary, running, error, runningError };
}
