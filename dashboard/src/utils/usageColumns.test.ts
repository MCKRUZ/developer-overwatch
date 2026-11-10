import { describe, it, expect } from "vitest";
import { USAGE_COLUMNS, usageCsvTable, usageJsonExport, usagePdfTable } from "./usageColumns";
import { buildUsageBucket, buildUsageSummary } from "../test/usageFactories";

describe("usageColumns", () => {
  const bucket = buildUsageBucket({
    start: "2026-11-04",
    inputTokens: 1204318,
    outputTokens: 84112,
    cacheReadInputTokens: 9840220,
    cacheCreationInputTokens: 1120400,
    totalTokens: 12249050,
    estimatedCostUsd: 18.403912,
    cacheHitRate: 0.874,
    turnCount: 212,
  });

  it("should define one column per figure the screen shows, in screen order", () => {
    expect(USAGE_COLUMNS.map((c) => c.key)).toEqual([
      "start",
      "inputTokens",
      "outputTokens",
      "cacheReadInputTokens",
      "cacheCreationInputTokens",
      "totalTokens",
      "estimatedCostUsd",
      "cacheHitRate",
      "turnCount",
    ]);
  });

  it("should export CSV with the screen's columns plus the price date, as raw numbers a spreadsheet can sum", () => {
    const table = usageCsvTable([bucket], "2026-11-01");

    expect(table.headers).toEqual([...USAGE_COLUMNS.map((c) => c.header), "Prices as of"]);
    expect(table.rows).toEqual([
      ["2026-11-04", "1204318", "84112", "9840220", "1120400", "12249050", "18.403912", "0.874", "212", "2026-11-01"],
    ]);
  });

  it("should leave the CSV hit-rate cell empty when there was no input, not write the screen's dash", () => {
    const table = usageCsvTable([buildUsageBucket({ cacheHitRate: undefined })], "2026-11-01");
    expect(table.rows[0]![7]).toBe("");
  });

  it("should export JSON with the same fields as raw values and the period and price date on top", () => {
    const summary = buildUsageSummary({ buckets: [bucket, buildUsageBucket({ start: "2026-11-05", cacheHitRate: undefined })] });

    const json = usageJsonExport(summary);

    expect(json).toMatchObject({
      from: summary.from,
      to: summary.to,
      granularity: summary.granularity,
      timeZone: summary.timeZone,
      pricesAsOf: summary.pricesAsOf,
    });
    expect(json.rows[0]).toEqual({
      start: "2026-11-04",
      inputTokens: 1204318,
      outputTokens: 84112,
      cacheReadInputTokens: 9840220,
      cacheCreationInputTokens: 1120400,
      totalTokens: 12249050,
      estimatedCostUsd: 18.403912,
      cacheHitRate: 0.874,
      turnCount: 212,
    });
    expect(json.rows[1]!.cacheHitRate).toBeNull();
  });

  it("should print the PDF with the screen's formatted figures", () => {
    const table = usagePdfTable([bucket]);

    expect(table.headers).toEqual(USAGE_COLUMNS.map((c) => c.header));
    expect(table.rows[0]).toEqual([
      "2026-11-04",
      (1204318).toLocaleString(),
      (84112).toLocaleString(),
      (9840220).toLocaleString(),
      (1120400).toLocaleString(),
      (12249050).toLocaleString(),
      "$18.40",
      "87%",
      "212",
    ]);
  });
});
