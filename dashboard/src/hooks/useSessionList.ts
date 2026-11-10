import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "../api";
import type { SessionListFilters } from "../api";
import type { SessionListItem } from "../types";
import { useLiveRefresh } from "./useLiveRefresh";

/**
 * Loads the session list for `filters` and keeps it current from the hub's live events.
 * A filter change shows the loading state; a live refetch swaps the rows in place.
 */
export function useSessionList(filters: SessionListFilters) {
  const [sessions, setSessions] = useState<SessionListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshTick, setRefreshTick] = useState(0);
  const lastFilterKeyRef = useRef<string | null>(null);

  const refetch = useCallback(() => setRefreshTick((tick) => tick + 1), []);
  useLiveRefresh(refetch);

  // Callers build a fresh filters object each render; key on its content so only a real change refetches.
  const filterKey = JSON.stringify(filters);
  const stableFilters = useMemo(() => filters, [filterKey]);

  useEffect(() => {
    let superseded = false;
    if (lastFilterKeyRef.current !== filterKey) setLoading(true);
    lastFilterKeyRef.current = filterKey;

    api.sessions
      .list(stableFilters)
      .then((rows) => {
        if (superseded) return;
        setSessions(rows);
        setError(null);
      })
      .catch((e: Error) => {
        if (!superseded) setError(e.message);
      })
      .finally(() => {
        if (!superseded) setLoading(false);
      });

    return () => {
      superseded = true;
    };
  }, [filterKey, stableFilters, refreshTick]);

  return { sessions, loading, error };
}
