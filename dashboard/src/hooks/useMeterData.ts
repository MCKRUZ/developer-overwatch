import { useCallback, useEffect, useRef } from "react";
import { api } from "../api";
import type { BudgetStatus, DeveloperWindow, PlanValue } from "../types";
import { useCoalescedFetch } from "./useCoalescedFetch";
import { useTrailingThrottle } from "./useLiveRefresh";

/**
 * Budgets, 5-hour windows and plan value refetch together, 3 requests each time:
 * - on live usage events, at most once per `METER_REFRESH_THROTTLE_MS`;
 * - with no events, once they are `METER_STALE_AFTER_MS` old, so pace, forecast and a period
 *   rollover keep up with the clock. Under an event stream that timer never fires.
 * The window reset countdown ticks locally and never refetches.
 */
export const METER_REFRESH_THROTTLE_MS = 60_000;
export const METER_STALE_AFTER_MS = 300_000;

// None of the three depends on the Usage page's period or filters, so their key never changes.
const METER_KEY = "meter";

export interface MeterData {
  budgets: BudgetStatus[] | null;
  budgetsError: string | null;
  windows: DeveloperWindow[] | null;
  windowsError: string | null;
  planValues: PlanValue[] | null;
  planValuesError: string | null;
}

/**
 * Loads the meter sections. It does not subscribe to the hub itself (each subscription opens its
 * own connection): the caller wires `requestRefresh` to live events and `refresh` to resyncs.
 */
export function useMeterData() {
  const budgets = useCoalescedFetch(METER_KEY, () => api.budgets.status());
  const windows = useCoalescedFetch(METER_KEY, () => api.usage.windows());
  const planValues = useCoalescedFetch(METER_KEY, () => api.usage.planValue());
  const staleTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const refreshRef = useRef<() => void>(() => {});

  // Re-armed by every refresh, so it only fires after a full quiet period.
  const armStaleTimer = useCallback(() => {
    if (staleTimerRef.current !== null) clearTimeout(staleTimerRef.current);
    staleTimerRef.current = setTimeout(() => refreshRef.current(), METER_STALE_AFTER_MS);
  }, []);

  const { refresh: refreshBudgets } = budgets;
  const { refresh: refreshWindows } = windows;
  const { refresh: refreshPlanValues } = planValues;
  const refresh = useCallback(() => {
    refreshBudgets();
    refreshWindows();
    refreshPlanValues();
    armStaleTimer();
  }, [refreshBudgets, refreshWindows, refreshPlanValues, armStaleTimer]);

  useEffect(() => {
    refreshRef.current = refresh;
  }, [refresh]);

  useEffect(() => {
    armStaleTimer();
    // Armed just above, so there is always a timer to clear.
    return () => clearTimeout(staleTimerRef.current!);
  }, [armStaleTimer]);

  const requestRefresh = useTrailingThrottle(refresh, METER_REFRESH_THROTTLE_MS);

  const data: MeterData = {
    budgets: budgets.data,
    budgetsError: budgets.error,
    windows: windows.data,
    windowsError: windows.error,
    planValues: planValues.data,
    planValuesError: planValues.error,
  };
  return { data, refresh, requestRefresh };
}
