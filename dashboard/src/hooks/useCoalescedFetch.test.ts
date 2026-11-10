import { describe, it, expect, vi } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { useCoalescedFetch } from "./useCoalescedFetch";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("useCoalescedFetch", () => {
  it("should fold every refresh made during a load into one follow-up, so an event storm costs one request", async () => {
    const first = deferred<string>();
    const load = vi.fn().mockReturnValueOnce(first.promise).mockResolvedValue("second");
    const { result } = renderHook(() => useCoalescedFetch("k", load));

    act(() => {
      for (let i = 0; i < 5; i++) result.current.refresh();
    });
    await act(async () => first.resolve("first"));

    await waitFor(() => expect(result.current.data).toBe("second"));
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("should load right away on a refresh while idle", async () => {
    const load = vi.fn().mockResolvedValueOnce("a").mockResolvedValueOnce("b");
    const { result } = renderHook(() => useCoalescedFetch("k", load));
    await waitFor(() => expect(result.current.data).toBe("a"));

    act(() => result.current.refresh());

    await waitFor(() => expect(result.current.data).toBe("b"));
    expect(result.current.loading).toBe(false);
  });

  it("should keep the last data beside a refresh error, because the data still belongs to the same key", async () => {
    const load = vi.fn().mockResolvedValueOnce("a").mockRejectedValueOnce(new Error("503: busy"));
    const { result } = renderHook(() => useCoalescedFetch("k", load));
    await waitFor(() => expect(result.current.data).toBe("a"));

    act(() => result.current.refresh());

    await waitFor(() => expect(result.current.error).toBe("503: busy"));
    expect(result.current.data).toBe("a");
  });

  it("should report a non-Error rejection as text rather than crash", async () => {
    const load = vi.fn().mockRejectedValue("offline");
    const { result } = renderHook(() => useCoalescedFetch("k", load));

    await waitFor(() => expect(result.current.error).toBe("offline"));
  });

  it("should mark loading on a key change and drop the superseded key's late result", async () => {
    const old = deferred<string>();
    const load = vi.fn().mockReturnValueOnce(old.promise).mockResolvedValue("new");
    const { result, rerender } = renderHook(({ k }) => useCoalescedFetch(k, load), { initialProps: { k: "a" } });

    rerender({ k: "b" });
    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.data).toBe("new"));
    await act(async () => old.resolve("old"));

    expect(result.current.data).toBe("new");
  });

  it("should not run a dirty follow-up for a key that was superseded mid-load", async () => {
    const old = deferred<string>();
    const load = vi.fn().mockReturnValueOnce(old.promise).mockResolvedValue("new");
    const { result, rerender } = renderHook(({ k }) => useCoalescedFetch(k, load), { initialProps: { k: "a" } });

    act(() => result.current.refresh());
    rerender({ k: "b" });
    await act(async () => old.resolve("old"));

    await waitFor(() => expect(result.current.data).toBe("new"));
    expect(load).toHaveBeenCalledTimes(2);
  });

  it("should do nothing after unmount, so a late timer cannot fire a request for a closed page", async () => {
    const pending = deferred<string>();
    const load = vi.fn().mockReturnValue(pending.promise);
    const { result, unmount } = renderHook(() => useCoalescedFetch("k", load));
    const { refresh } = result.current;

    unmount();
    refresh();
    await act(async () => pending.resolve("late"));

    expect(load).toHaveBeenCalledTimes(1);
  });
});
