import { api } from "../api";
import type { SessionListFilters } from "../api";
import { useCoalescedFetch } from "./useCoalescedFetch";
import { useLiveRefresh } from "./useLiveRefresh";

/**
 * Loads the session list for `filters` and keeps it current from the hub's live events.
 * A filter change shows the loading state; a live refetch swaps the rows in place.
 */
export function useSessionList(filters: SessionListFilters) {
  // Callers build a fresh filters object each render; key on its content so only a real change refetches.
  const list = useCoalescedFetch(JSON.stringify(filters), () => api.sessions.list(filters));
  useLiveRefresh(list.refresh);

  return { sessions: list.data ?? [], loading: list.loading, error: list.error };
}
