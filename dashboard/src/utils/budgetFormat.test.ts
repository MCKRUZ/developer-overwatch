import { describe, it, expect } from "vitest";
import {
  budgetWho,
  forecastText,
  formatLocalTime,
  formatMultiple,
  formatUsedPercent,
  levelLabel,
  paceText,
  periodUnit,
  remainingText,
  resetText,
} from "./budgetFormat";

/** A local wall-clock instant as the UTC ISO string the API sends, so tests pass in any machine time zone. */
function localIso(month: number, day: number, hour: number, minute: number): string {
  return new Date(2026, month - 1, day, hour, minute).toISOString();
}

describe("budgetFormat", () => {
  describe("remainingText", () => {
    it.each([
      [5.8, "$5.80 left"],
      [0, "$0.00 left"],
      [-1.2, "over by $1.20"],
    ])("should describe %s remaining as %s, never as a negative amount left", (remaining, expected) => {
      expect(remainingText(remaining)).toBe(expected);
    });
  });

  describe("paceText", () => {
    it.each([
      ["Ahead", "day", "ahead of pace: 42% used at 25% of the day"],
      ["OnPace", "month", "on pace: 42% used at 25% of the month"],
      ["Behind", "window", "behind pace: 42% used at 25% of the window"],
    ] as const)("should word %s pace for a %s", (status, unit, expected) => {
      expect(paceText({ status, usedFraction: 0.42, elapsedFraction: 0.25 }, unit)).toBe(expected);
    });
  });

  describe("forecastText", () => {
    const now = new Date(2026, 10, 13, 9, 0);

    it("should give the local time when the money runs out later today", () => {
      expect(forecastText({ kind: "RunsOutWithinPeriod", runsOutAt: localIso(11, 13, 15, 10) }, now)).toBe(
        "runs out about 15:10",
      );
    });

    it("should give the local date when the money runs out on a later day", () => {
      expect(forecastText({ kind: "RunsOutWithinPeriod", runsOutAt: localIso(11, 24, 8, 0) }, now)).toBe(
        "runs out on 2026-11-24",
      );
    });

    it("should not invent a time when the server sends none", () => {
      expect(forecastText({ kind: "RunsOutWithinPeriod", runsOutAt: null }, now)).toBe("runs out within the period");
    });

    it.each([
      ["LastsPastPeriodEnd", "lasts the period"],
      ["AlreadyExceeded", "already over"],
      ["NoSpendYet", "no spend yet"],
      ["NoRateYet", "not enough activity to forecast yet"],
    ] as const)("should word %s as %s", (kind, expected) => {
      expect(forecastText({ kind, runsOutAt: null }, now)).toBe(expected);
    });
  });

  describe("levelLabel", () => {
    it.each([
      ["Ok", "Within limit"],
      ["Amber", "Nearing limit"],
      ["Red", "Limit reached"],
    ] as const)("should name the %s level in words, so colour is never the only signal", (level, expected) => {
      expect(levelLabel(level)).toBe(expected);
    });
  });

  describe("resetText", () => {
    it("should count down to the reset with its local clock time, labelled an estimate", () => {
      const now = new Date(2026, 10, 13, 13, 13);
      expect(resetText(localIso(11, 13, 15, 0), now)).toBe("resets in about 1h 47m (15:00), estimate");
    });

    it("should round a part minute up, so an open window never reads 0m", () => {
      const now = new Date(2026, 10, 13, 14, 59, 30);
      expect(resetText(localIso(11, 13, 15, 0), now)).toBe("resets in about 1m (15:00), estimate");
    });
  });

  it("should format a local time as zero-padded 24-hour HH:mm", () => {
    expect(formatLocalTime(localIso(11, 13, 7, 5))).toBe("07:05");
  });

  it("should show the plan value multiple to one decimal", () => {
    expect(formatMultiple(5.6666)).toBe("5.7x");
  });

  it("should show a used fraction as a whole percent", () => {
    expect(formatUsedPercent(0.755)).toBe("76%");
  });

  it("should name who a budget covers", () => {
    expect(budgetWho({ scope: "Team", developerEmail: null })).toBe("Team");
    expect(budgetWho({ scope: "Developer", developerEmail: "a@x.io" })).toBe("a@x.io");
  });

  it.each([
    ["Daily", "day"],
    ["Monthly", "month"],
  ] as const)("should call the %s period a %s in pace wording", (period, unit) => {
    expect(periodUnit(period)).toBe(unit);
  });
});
