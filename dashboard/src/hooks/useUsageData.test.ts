import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import type { RunningSession, UsageSummary } from "../types";
import { buildRunningSession, buildUsageSummary } from "../test/usageFactories";
import { RUNNING_REFRESH_THROTTLE_MS, SUMMARY_REFRESH_INTERVAL_MS, useUsageData } from "./useUsageData";

vi.mock("../api", () => ({
  api: { usage: { summary: vi.fn(), running: vi.fn() } },
}));

const live = vi.hoisted(() => ({
  onChange: null as (() => void) | null,
  onResync: null as (() => void) | null,
}));

vi.mock("./useLiveRefresh", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./useLiveRefresh")>()),
  useLiveEvents: (handlers: { onChange: () => void; onResync: () => void }) => {
    live.onChange = handlers.onChange;
    live.onResync = handlers.onResync;
  },
}));

const mockApi = vi.mocked(api, true);

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (e: Error) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

const dayParams: UsageSummaryParams = { from: "2026-11-04", to: "2026-11-10", granularity: "day" };

describe("useUsageData", () => {
  beforeEach(() => vi.clearAllMocks());

  it("should drop late running-list and error results from a superseded request, so old data cannot replace new", async () => {
    const oldSummary = deferred<UsageSummary>();
    const oldRunning = deferred<RunningSession[]>();
    mockApi.usage.summary.mockReturnValueOnce(oldSummary.promise).mockResolvedValue(buildUsageSummary());
    mockApi.usage.running
      .mockReturnValueOnce(oldRunning.promise)
      .mockResolvedValue([buildRunningSession({ id: "new" })]);
    const { result, rerender } = renderHook((params: UsageSummaryParams) => useUsageData(params), {
      initialProps: dayParams,
    });

    rerender({ ...dayParams, granularity: "week" });
    await waitFor(() => expect(result.current.running[0]?.id).toBe("new"));

    await act(async () => {
      oldSummary.reject(new Error("old summary failed"));
      oldRunning.resolve([buildRunningSession({ id: "old" })]);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.running[0]?.id).toBe("new");
  });

  it("should drop a superseded running-list failure", async () => {
    const oldRunning = deferred<RunningSession[]>();
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockReturnValueOnce(oldRunning.promise).mockResolvedValue([]);
    const { result, rerender } = renderHook((params: UsageSummaryParams) => useUsageData(params), {
      initialProps: dayParams,
    });

    rerender({ ...dayParams, granularity: "month" });
    await waitFor(() => expect(mockApi.usage.running).toHaveBeenCalledTimes(2));
    await act(async () => oldRunning.reject(new Error("old running failed")));

    expect(result.current.runningError).toBeNull();
  });

  it("should show the in-flight response when live ticks land before it, so a slow server under a steady event stream still updates the screen", async () => {
    const first = deferred<UsageSummary>();
    mockApi.usage.summary.mockReturnValueOnce(first.promise).mockReturnValue(new Promise(() => {}));
    mockApi.usage.running.mockResolvedValue([]);
    const { result } = renderHook(() => useUsageData(dayParams));

    act(() => {
      live.onResync?.();
      live.onResync?.();
    });
    await act(async () => first.resolve(buildUsageSummary({ pricesAsOf: "first" })));

    expect(result.current.summary?.pricesAsOf).toBe("first");
    // Both ticks fold into one follow-up request once the first settles, not one request each.
    expect(mockApi.usage.summary).toHaveBeenCalledTimes(2);
  });

  describe("live budget", () => {
    beforeEach(() => {
      vi.useFakeTimers();
      mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
      mockApi.usage.running.mockResolvedValue([]);
    });
    afterEach(() => vi.useRealTimers());

    const advance = (ms: number) =>
      act(async () => {
        await vi.advanceTimersByTimeAsync(ms);
      });

    it("should refetch only the running list on live events, throttled, because one turn barely moves the period totals", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      act(() => {
        for (let i = 0; i < 20; i++) live.onChange?.();
      });
      await advance(RUNNING_REFRESH_THROTTLE_MS);

      expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
      expect(mockApi.usage.summary).toHaveBeenCalledTimes(1);
    });

    it("should refetch the summary on its interval even with no events, so the burn tile decays when activity stops", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      await advance(SUMMARY_REFRESH_INTERVAL_MS);

      expect(mockApi.usage.summary).toHaveBeenCalledTimes(2);
    });

    it("should refetch the summary at once and the running list within the throttle on a hub reconnect, so missed events are recovered", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      act(() => live.onResync?.());
      await advance(0);
      expect(mockApi.usage.summary).toHaveBeenCalledTimes(2);
      await advance(RUNNING_REFRESH_THROTTLE_MS);

      expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
    });
  });
});
