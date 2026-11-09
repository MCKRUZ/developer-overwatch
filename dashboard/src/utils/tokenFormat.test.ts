import { describe, it, expect } from "vitest";
import { formatTotalTokens, dailyUsageFor } from "./tokenFormat";
import { buildSessionListItem } from "../test/factories";

describe("formatTotalTokens", () => {
  it("should count cache reads and cache writes, which are most of real Claude Code usage", () => {
    expect(
      formatTotalTokens({
        inputTokens: 3,
        outputTokens: 50,
        cacheCreationInputTokens: 400,
        cacheReadInputTokens: 9000,
      }),
    ).toBe((9453).toLocaleString());
  });

  it("should treat missing token kinds as zero", () => {
    expect(formatTotalTokens({ outputTokens: 7 })).toBe("7");
    expect(formatTotalTokens({})).toBe("0");
  });
});

describe("dailyUsageFor", () => {
  it("should fall back to an all-zero day including cache tokens", () => {
    const session = buildSessionListItem({ dailyUsage: {} });

    expect(dailyUsageFor(session, "2026-09-20")).toEqual({
      inputTokens: 0,
      outputTokens: 0,
      cacheCreationInputTokens: 0,
      cacheReadInputTokens: 0,
      estimatedCostUsd: 0,
    });
  });
});
