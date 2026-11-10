import { renderHook, act } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";

const { hub } = vi.hoisted(() => {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const connection = {
    on: (event: string, handler: (...args: unknown[]) => void) => handlers.set(event, handler),
    off: (event: string) => handlers.delete(event),
  };
  const emit = (event: string, ...args: unknown[]) => handlers.get(event)?.(...args);
  return { hub: { connection, emit, handlers, connectedEpoch: 1 } };
});

vi.mock("./useSignalR", () => ({
  useSignalR: () => ({
    connection: hub.connection as unknown as HubConnection,
    state: HubConnectionState.Connected,
    connectedEpoch: hub.connectedEpoch,
  }),
}));

import { useLiveRefresh, useTrailingThrottle, LIVE_REFRESH_INTERVAL_MS } from "./useLiveRefresh";

describe("useTrailingThrottle", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("should coalesce a burst into one call, so a busy session does not flood the API", () => {
    const fn = vi.fn();
    const { result } = renderHook(() => useTrailingThrottle(fn, 2000));

    act(() => {
      for (let i = 0; i < 10; i++) result.current();
    });
    expect(fn).not.toHaveBeenCalled();

    act(() => vi.advanceTimersByTime(2000));
    expect(fn).toHaveBeenCalledTimes(1);
  });

  it("should run at most once per interval while events keep arriving", () => {
    const fn = vi.fn();
    const { result } = renderHook(() => useTrailingThrottle(fn, 2000));

    for (let ms = 0; ms < 6000; ms += 500) {
      act(() => {
        result.current();
        vi.advanceTimersByTime(500);
      });
    }

    expect(fn).toHaveBeenCalledTimes(3);
  });

  it("should call the latest callback, so a filter change made mid-window is what gets refetched", () => {
    const first = vi.fn();
    const second = vi.fn();
    const { result, rerender } = renderHook(({ fn }) => useTrailingThrottle(fn, 2000), {
      initialProps: { fn: first },
    });

    act(() => result.current());
    rerender({ fn: second });
    act(() => vi.advanceTimersByTime(2000));

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
  });

  it("should keep the trigger stable across renders, so event subscriptions are not torn down", () => {
    const { result, rerender } = renderHook(({ fn }) => useTrailingThrottle(fn, 2000), {
      initialProps: { fn: vi.fn() },
    });
    const trigger = result.current;

    rerender({ fn: vi.fn() });

    expect(result.current).toBe(trigger);
  });

  it("should drop a pending call on unmount, so no refetch fires into an unmounted page", () => {
    const fn = vi.fn();
    const { result, unmount } = renderHook(() => useTrailingThrottle(fn, 2000));

    act(() => result.current());
    unmount();
    vi.advanceTimersByTime(2000);

    expect(fn).not.toHaveBeenCalled();
  });
});

describe("useLiveRefresh", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    hub.connectedEpoch = 0;
  });
  afterEach(() => vi.useRealTimers());

  it.each(["SessionStarted", "SessionEnded", "UsageUpdated"])(
    "should refetch after %s, because each one changes what the list shows",
    (event) => {
      const refetch = vi.fn();
      renderHook(() => useLiveRefresh(refetch));

      act(() => hub.emit(event, { sessionId: "s1", at: "t" }));
      act(() => vi.advanceTimersByTime(LIVE_REFRESH_INTERVAL_MS));

      expect(refetch).toHaveBeenCalledTimes(1);
    },
  );

  it("should refetch once for a mixed burst of events inside one window", () => {
    const refetch = vi.fn();
    renderHook(() => useLiveRefresh(refetch));

    act(() => {
      hub.emit("SessionStarted", "s1", "d", null);
      hub.emit("UsageUpdated", { sessionId: "s1", at: "t" });
      hub.emit("SessionEnded", "s1");
    });
    act(() => vi.advanceTimersByTime(LIVE_REFRESH_INTERVAL_MS));

    expect(refetch).toHaveBeenCalledTimes(1);
  });

  it("should not refetch for violation events, which the list does not depend on", () => {
    const refetch = vi.fn();
    renderHook(() => useLiveRefresh(refetch));

    act(() => hub.emit("ViolationResolved", "v1"));
    act(() => vi.advanceTimersByTime(LIVE_REFRESH_INTERVAL_MS));

    expect(refetch).not.toHaveBeenCalled();
  });

  it("should refetch at once on every new hub connect, because events sent while disconnected were lost", () => {
    hub.connectedEpoch = 1;
    const refetch = vi.fn();
    const { rerender } = renderHook(() => useLiveRefresh(refetch));
    expect(refetch).toHaveBeenCalledTimes(1);

    hub.connectedEpoch = 2;
    rerender();

    expect(refetch).toHaveBeenCalledTimes(2);
  });

  it("should not refetch before the hub has ever connected, since the mount load is already current", () => {
    const refetch = vi.fn();
    renderHook(() => useLiveRefresh(refetch));

    expect(refetch).not.toHaveBeenCalled();
  });
});
