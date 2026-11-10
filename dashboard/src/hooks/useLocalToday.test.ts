import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { LOCAL_DATE_CHECK_MS, useLocalToday } from "./useLocalToday";

describe("useLocalToday", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 10, 23, 58, 30));
  });
  afterEach(() => vi.useRealTimers());

  it("should keep the same value within a day, so derived ranges do not refetch every minute", () => {
    const { result } = renderHook(() => useLocalToday());
    const first = result.current;

    act(() => vi.advanceTimersByTime(LOCAL_DATE_CHECK_MS));

    expect(result.current).toBe(first);
  });

  it("should move to the new local date within a check of midnight", () => {
    const { result } = renderHook(() => useLocalToday());

    act(() => vi.advanceTimersByTime(LOCAL_DATE_CHECK_MS * 2));

    expect(result.current).toEqual(new Date(2026, 10, 11));
  });
});
