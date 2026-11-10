import { useCallback, useEffect, useRef, useState } from "react";

export interface CoalescedFetch<T> {
  data: T | null;
  error: string | null;
  /** True from a key change until the first load for that key settles; a refresh never sets it. */
  loading: boolean;
  refresh: () => void;
}

interface FetchState<T> {
  data: T | null;
  error: string | null;
  loading: boolean;
}

function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/**
 * Runs `load` when `key` changes and again on `refresh()`.
 *
 * A key change supersedes an in-flight load, so its late result is dropped. A refresh never
 * supersedes: while a load is in flight it only marks the data dirty, and one more load runs
 * once the current one settles. A steady stream of refreshes against a slow server therefore
 * still shows every response, and any number of refreshes during one load cost one request.
 */
export function useCoalescedFetch<T>(key: string, load: () => Promise<T>): CoalescedFetch<T> {
  const [state, setState] = useState<FetchState<T>>({ data: null, error: null, loading: true });
  const loadRef = useRef(load);
  const generationRef = useRef(0);
  const activeRef = useRef(false);
  const inFlightRef = useRef(false);
  const dirtyRef = useRef(false);

  useEffect(() => {
    loadRef.current = load;
  });

  const run = useCallback((generation: number) => {
    const isCurrent = () => activeRef.current && generation === generationRef.current;
    inFlightRef.current = true;
    dirtyRef.current = false;
    loadRef
      .current()
      .then(
        (data) => {
          if (isCurrent()) setState({ data, error: null, loading: false });
        },
        (e: unknown) => {
          if (isCurrent()) setState((prev) => ({ ...prev, error: messageOf(e), loading: false }));
        },
      )
      .finally(() => {
        if (!isCurrent()) return;
        inFlightRef.current = false;
        if (dirtyRef.current) run(generation);
      });
  }, []);

  useEffect(() => {
    const generation = ++generationRef.current;
    activeRef.current = true;
    setState((prev) => (prev.loading ? prev : { ...prev, loading: true }));
    run(generation);
    return () => {
      activeRef.current = false;
      inFlightRef.current = false;
      dirtyRef.current = false;
    };
  }, [key, run]);

  const refresh = useCallback(() => {
    if (!activeRef.current) return;
    if (inFlightRef.current) {
      dirtyRef.current = true;
      return;
    }
    run(generationRef.current);
  }, [run]);

  return { ...state, refresh };
}
