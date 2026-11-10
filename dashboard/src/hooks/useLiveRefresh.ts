import { useCallback, useEffect, useRef } from "react";
import { useSignalR } from "./useSignalR";
import { useGlobalUpdates } from "./useGlobalUpdates";

/** Live events can arrive many times a second; refetch at most this often. */
export const LIVE_REFRESH_INTERVAL_MS = 2_000;

/**
 * Returns a stable trigger. The first call in a quiet period schedules `fn` for `intervalMs` later;
 * calls made while that is pending are absorbed by it. `fn` always runs after the newest event it
 * covers, so the last update of a burst is never lost, and it runs at most once per interval.
 */
export function useTrailingThrottle(fn: () => void, intervalMs: number): () => void {
  const fnRef = useRef(fn);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    fnRef.current = fn;
  }, [fn]);

  useEffect(
    () => () => {
      if (timerRef.current !== null) clearTimeout(timerRef.current);
    },
    [],
  );

  return useCallback(() => {
    if (timerRef.current !== null) return;
    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      fnRef.current();
    }, intervalMs);
  }, [intervalMs]);
}

/**
 * Calls `refetch` (throttled) whenever the hub reports a session start, end, or new usage, and
 * at once on every new hub connect, since events sent while disconnected were lost.
 */
export function useLiveRefresh(refetch: () => void): void {
  const requestRefresh = useTrailingThrottle(refetch, LIVE_REFRESH_INTERVAL_MS);
  const { connection, state, connectedEpoch } = useSignalR();
  useResync(connectedEpoch, refetch);

  useGlobalUpdates({
    connection,
    connectionState: state,
    onSessionStarted: requestRefresh,
    onSessionEnded: requestRefresh,
    onUsageUpdated: requestRefresh,
  });
}

/** Runs `resync` each time `connectedEpoch` moves past 0, i.e. on every transition into Connected. */
function useResync(connectedEpoch: number, resync: () => void): void {
  const resyncRef = useRef(resync);

  useEffect(() => {
    resyncRef.current = resync;
  }, [resync]);

  useEffect(() => {
    if (connectedEpoch > 0) resyncRef.current();
  }, [connectedEpoch]);
}
