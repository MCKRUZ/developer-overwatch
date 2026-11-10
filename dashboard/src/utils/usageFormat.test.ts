import { describe, it, expect } from "vitest";
import { formatPercent, formatPerMinuteUsd, formatMinutes, formatSpan, runningStateLabel } from "./usageFormat";
import type { RunningSession } from "../types";

function running(overrides: Partial<RunningSession>): Pick<RunningSession, "activityState" | "idleForSeconds"> {
  return { ...overrides };
}

describe("usageFormat", () => {
  it("should show the cache hit rate as a whole percent", () => {
    expect(formatPercent(0.874)).toBe("87%");
  });

  it("should show 0% for a real zero hit rate rather than treating it as missing", () => {
    expect(formatPercent(0)).toBe("0%");
  });

  it("should show a dash when there was no input, because a hit rate of nothing is not 0%", () => {
    expect(formatPercent(undefined)).toBe("—");
  });

  it("should keep cents for normal burn so $0.21/min reads like the spec example", () => {
    expect(formatPerMinuteUsd(0.2149)).toBe("$0.21");
  });

  it("should show sub-cent burn with more digits instead of rounding a live session to $0.00", () => {
    expect(formatPerMinuteUsd(0.00342)).toBe("$0.0034");
  });

  it("should show zero burn as $0.00", () => {
    expect(formatPerMinuteUsd(0)).toBe("$0.00");
  });

  it("should format minutes under an hour as Nm and longer spans as Nh Nm", () => {
    expect(formatMinutes(52)).toBe("52m");
    expect(formatMinutes(134)).toBe("2h 14m");
  });

  it("should give a session's duration from its first and last turn", () => {
    expect(formatSpan("2026-11-04T09:00:00Z", "2026-11-04T11:14:30Z")).toBe("2h 14m");
  });

  it.each([
    [{ activityState: "Active" as const }, "active"],
    [{ activityState: "Waiting" as const }, "waiting"],
    [{ activityState: "Idle" as const, idleForSeconds: 3125 }, "idle 52m"],
    [{ activityState: "Idle" as const }, "idle"],
    [{}, "unknown"],
  ])("should label running state %o as %s", (session, label) => {
    expect(runningStateLabel(running(session))).toBe(label);
  });
});
