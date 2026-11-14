import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import type { RunningSession, UsageSummary } from "../types";
import { buildRunningSession, buildUsageSummary } from "../test/usageFactories";
import { buildBudgetStatus, buildDeveloperWindow, buildPlanValue } from "../test/meterFactories";
import { RUNNING_REFRESH_THROTTLE_MS, RUNNING_STALE_AFTER_MS, SUMMARY_REFRESH_INTERVAL_MS, useUsageData } from "./useUsageData";
import { METER_REFRESH_THROTTLE_MS, METER_STALE_AFTER_MS } from "./useMeterData";

vi.mock("../api", () => ({
  api: {
    usage: { summary: vi.fn(), running: vi.fn(), windows: vi.fn(), planValue: vi.fn() },
    budgets: { status: vi.fn() },
  },
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

function meterCalls(): number[] {
  return [
    mockApi.budgets.status.mock.calls.length,
    mockApi.usage.windows.mock.calls.length,
    mockApi.usage.planValue.mock.calls.length,
  ];
}

describe("useUsageData", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApi.budgets.status.mockResolvedValue([buildBudgetStatus()]);
    mockApi.usage.windows.mockResolvedValue([buildDeveloperWindow()]);
    mockApi.usage.planValue.mockResolvedValue([buildPlanValue()]);
  });

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

    rerender({ ...dayParams, developer: "bob@x.io" });
    await waitFor(() => expect(result.current.running?.[0]?.id).toBe("new"));

    await act(async () => {
      oldSummary.reject(new Error("old summary failed"));
      oldRunning.resolve([buildRunningSession({ id: "old" })]);
    });

    expect(result.current.error).toBeNull();
    expect(result.current.running?.[0]?.id).toBe("new");
  });

  it("should drop a superseded running-list failure", async () => {
    const oldRunning = deferred<RunningSession[]>();
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockReturnValueOnce(oldRunning.promise).mockResolvedValue([]);
    const { result, rerender } = renderHook((params: UsageSummaryParams) => useUsageData(params), {
      initialProps: dayParams,
    });

    rerender({ ...dayParams, project: "overwatch" });
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

    it("should refetch the running list once it is a minute old with no events, so idle times, the stale cutoff and burn rates keep moving", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      await advance(RUNNING_STALE_AFTER_MS - 1);
      expect(mockApi.usage.running).toHaveBeenCalledTimes(1);
      await advance(1);
      expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
      await advance(RUNNING_STALE_AFTER_MS);
      expect(mockApi.usage.running).toHaveBeenCalledTimes(3);
    });

    it("should measure staleness from the last running fetch, so an event-driven refresh postpones the idle one", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(30_000);

      act(() => live.onChange?.());
      await advance(RUNNING_REFRESH_THROTTLE_MS);
      expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
      await advance(RUNNING_STALE_AFTER_MS - 1);

      expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
    });

    it("should stop the idle refresh on unmount", async () => {
      const { unmount } = renderHook(() => useUsageData(dayParams));
      await advance(0);

      unmount();
      await advance(RUNNING_STALE_AFTER_MS * 2);

      expect(mockApi.usage.running).toHaveBeenCalledTimes(1);
    });

    it("should refetch budgets, windows and plan value on live events at most once per meter throttle, because a turn moves spend but a refetch per turn would flood the API", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);
      expect(meterCalls()).toEqual([1, 1, 1]);

      act(() => {
        for (let i = 0; i < 20; i++) live.onChange?.();
      });
      await advance(METER_REFRESH_THROTTLE_MS - 1);
      expect(meterCalls()).toEqual([1, 1, 1]);
      await advance(1);

      expect(meterCalls()).toEqual([2, 2, 2]);
    });

    it("should refetch the meter once it is stale with no events, so pace and forecast keep up with the clock", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      await advance(METER_STALE_AFTER_MS - 1);
      expect(meterCalls()).toEqual([1, 1, 1]);
      await advance(1);
      expect(meterCalls()).toEqual([2, 2, 2]);
    });

    it("should postpone the idle meter refresh after an event-driven one", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      act(() => live.onChange?.());
      await advance(METER_REFRESH_THROTTLE_MS);
      await advance(METER_STALE_AFTER_MS - 1);

      expect(meterCalls()).toEqual([2, 2, 2]);
    });

    it("should stop the idle meter refresh on unmount", async () => {
      const { unmount } = renderHook(() => useUsageData(dayParams));
      await advance(0);

      unmount();
      await advance(METER_STALE_AFTER_MS * 2);

      expect(meterCalls()).toEqual([1, 1, 1]);
    });

    it("should refetch the meter at once on a hub reconnect, so spend missed while disconnected shows", async () => {
      renderHook(() => useUsageData(dayParams));
      await advance(0);

      act(() => live.onResync?.());
      await advance(0);

      expect(meterCalls()).toEqual([2, 2, 2]);
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

  it("should expose budgets, windows and plan value, each with its own error so one failure leaves the others on screen", async () => {
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockResolvedValue([]);
    mockApi.usage.windows.mockRejectedValue(new Error("503: busy"));
    const { result } = renderHook(() => useUsageData(dayParams));

    await waitFor(() => expect(result.current.meter.windowsError).toBe("503: busy"));
    expect(result.current.meter.budgets).toEqual([buildBudgetStatus()]);
    expect(result.current.meter.planValues).toEqual([buildPlanValue()]);
    expect(result.current.meter.windows).toBeNull();
    expect(result.current.meter.budgetsError).toBeNull();
    expect(result.current.meter.planValuesError).toBeNull();
  });

  it("should not reload the meter when the page filters change, since budgets, windows and plan value ignore them", async () => {
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockResolvedValue([]);
    const { rerender } = renderHook((params: UsageSummaryParams) => useUsageData(params), {
      initialProps: dayParams,
    });
    await waitFor(() => expect(mockApi.budgets.status).toHaveBeenCalledTimes(1));

    rerender({ ...dayParams, granularity: "week", developer: "alice@x.io" });
    await waitFor(() => expect(mockApi.usage.summary).toHaveBeenCalledTimes(2));

    expect(meterCalls()).toEqual([1, 1, 1]);
  });

  it("should load the running list with only the developer and project filters, and not reload it for a date change it does not depend on", async () => {
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockResolvedValue([]);
    const { rerender } = renderHook((params: UsageSummaryParams) => useUsageData(params), {
      initialProps: { ...dayParams, developer: "alice@x.io", project: "overwatch" },
    });
    await waitFor(() => expect(mockApi.usage.running).toHaveBeenCalledTimes(1));

    rerender({ ...dayParams, granularity: "week", developer: "alice@x.io", project: "overwatch" });
    await waitFor(() => expect(mockApi.usage.summary).toHaveBeenCalledTimes(2));

    expect(mockApi.usage.running).toHaveBeenCalledTimes(1);
    expect(mockApi.usage.running).toHaveBeenCalledWith({ developer: "alice@x.io", project: "overwatch" });
  });
});
