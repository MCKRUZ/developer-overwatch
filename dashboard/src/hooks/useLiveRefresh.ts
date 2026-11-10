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

export interface LiveEventHandlers {
  /** A session started or ended, or a session's usage changed. Unthrottled. */
  onChange: () => void;
  /** The hub (re)connected, so anything sent while it was down was missed. */
  onResync: () => void;
}

/**
 * Subscribes to the hub events that change usage and session data, over this page's one hub
 * connection. Handlers may change identity on every render without resubscribing.
 */
export function useLiveEvents({ onChange, onResync }: LiveEventHandlers): void {
  const onChangeRef = useRef(onChange);
  const onResyncRef = useRef(onResync);
  const { connection, state, connectedEpoch } = useSignalR();

  useEffect(() => {
    onChangeRef.current = onChange;
    onResyncRef.current = onResync;
  }, [onChange, onResync]);

  useEffect(() => {
    if (connectedEpoch > 0) onResyncRef.current();
  }, [connectedEpoch]);

  const handleChange = useCallback(() => onChangeRef.current(), []);
  useGlobalUpdates({
    connection,
    connectionState: state,
    onSessionStarted: handleChange,
    onSessionEnded: handleChange,
    onUsageUpdated: handleChange,
  });
}

/**
 * Calls `refetch` (throttled) whenever the hub reports a session start, end, or new usage, and
 * at once on every new hub connect, since events sent while disconnected were lost.
 */
export function useLiveRefresh(refetch: () => void): void {
  const requestRefresh = useTrailingThrottle(refetch, LIVE_REFRESH_INTERVAL_MS);
  useLiveEvents({ onChange: requestRefresh, onResync: refetch });
}
