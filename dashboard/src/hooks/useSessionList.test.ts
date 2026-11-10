import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { api } from "../api";
import type { SessionListFilters } from "../api";
import type { SessionListItem } from "../types";
import { buildSessionListItem } from "../test/factories";
import { useSessionList } from "./useSessionList";

vi.mock("../api", () => ({ api: { sessions: { list: vi.fn() } } }));

const live = vi.hoisted(() => ({ refetch: null as (() => void) | null }));

vi.mock("./useLiveRefresh", () => ({
  useLiveRefresh: (refetch: () => void) => {
    live.refetch = refetch;
  },
}));

const mockApi = vi.mocked(api, true);

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((res) => {
    resolve = res;
  });
  return { promise, resolve };
}

describe("useSessionList", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should show the in-flight response when live ticks land before it, so a slow server under a steady event stream still updates the list", async () => {
    const first = deferred<SessionListItem[]>();
    mockApi.sessions.list.mockReturnValueOnce(first.promise).mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useSessionList({}));

    act(() => {
      live.refetch?.();
      live.refetch?.();
    });
    await act(async () => first.resolve([buildSessionListItem({ id: "first" })]));

    expect(result.current.sessions[0]?.id).toBe("first");
    expect(result.current.loading).toBe(false);
    // Both ticks fold into one follow-up request once the first settles, not one request each.
    expect(mockApi.sessions.list).toHaveBeenCalledTimes(2);
  });

  it("should drop a late response for filters the user has already changed, so old rows never show under new filters", async () => {
    const old = deferred<SessionListItem[]>();
    mockApi.sessions.list.mockReturnValueOnce(old.promise).mockResolvedValue([buildSessionListItem({ id: "new" })]);
    const { result, rerender } = renderHook((filters: SessionListFilters) => useSessionList(filters), {
      initialProps: {},
    });

    rerender({ developer: "bob@x.io" });
    await waitFor(() => expect(result.current.sessions[0]?.id).toBe("new"));
    await act(async () => old.resolve([buildSessionListItem({ id: "old" })]));

    expect(result.current.sessions[0]?.id).toBe("new");
  });
});
