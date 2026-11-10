import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { api } from "../api";
import type { UsageSummaryParams } from "../api";
import type { RunningSession, UsageSummary } from "../types";
import { buildRunningSession, buildUsageSummary } from "../test/usageFactories";
import { useUsageData } from "./useUsageData";

vi.mock("../api", () => ({
  api: { usage: { summary: vi.fn(), running: vi.fn() } },
}));

const live = vi.hoisted(() => ({ refetch: null as (() => void) | null }));

vi.mock("./useLiveRefresh", () => ({
  useLiveRefresh: (refetch: () => void) => {
    live.refetch = refetch;
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
      live.refetch?.();
      live.refetch?.();
    });
    await act(async () => first.resolve(buildUsageSummary({ pricesAsOf: "first" })));

    expect(result.current.summary?.pricesAsOf).toBe("first");
    // Both ticks fold into one follow-up request once the first settles, not one request each.
    expect(mockApi.usage.summary).toHaveBeenCalledTimes(2);
    expect(mockApi.usage.running).toHaveBeenCalledTimes(2);
  });
});
