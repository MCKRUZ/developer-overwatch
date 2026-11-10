import { describe, it, expect } from "vitest";
import { SPLIT_DIMENSIONS, USAGE_COLUMNS, usageCsvTable, usageJsonExport, usagePdfTable, usagePdfTitle } from "./usageColumns";
import { buildUsageBucket, buildUsageSplitRow, buildUsageSplits, buildUsageSummary } from "../test/usageFactories";

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

  const modelRow = buildUsageSplitRow({
    key: "claude-opus-4-1",
    label: "Opus 4.1",
    inputTokens: 10,
    outputTokens: 20,
    cacheReadInputTokens: 30,
    cacheCreationInputTokens: 40,
    totalTokens: 100,
    estimatedCostUsd: 2.5,
    cacheHitRate: 0.5,
    turnCount: 3,
    costShare: 0.25,
    firstAt: "2026-11-04T09:00:00Z",
    lastAt: "2026-11-04T10:00:00Z",
  });
  const mainThreadRow = buildUsageSplitRow({ key: undefined, label: "Main thread", cacheHitRate: undefined });
  const fullSummary = buildUsageSummary({
    granularity: "day",
    pricesAsOf: "2026-11-01",
    developer: "alice@x.io",
    project: "overwatch",
    buckets: [bucket],
    splits: buildUsageSplits({ model: [modelRow], subagent: [mainThreadRow] }),
  });

  it("should export CSV as one rectangular table, buckets then every split, with a Section column so a spreadsheet can filter it", () => {
    const table = usageCsvTable(fullSummary);

    expect(table.headers).toEqual([
      "Section",
      "Key",
      "Name",
      ...USAGE_COLUMNS.slice(1).map((c) => c.header),
      "Share of cost",
      "First at",
      "Last at",
      "Prices as of",
      "Developer filter",
      "Project filter",
    ]);
    expect(table.rows).toEqual([
      ["By day", "2026-11-04", "2026-11-04", "1204318", "84112", "9840220", "1120400", "12249050", "18.403912", "0.874", "212", "", "", "", "2026-11-01", "alice@x.io", "overwatch"],
      ["Model", "claude-opus-4-1", "Opus 4.1", "10", "20", "30", "40", "100", "2.5", "0.5", "3", "0.25", "2026-11-04T09:00:00Z", "2026-11-04T10:00:00Z", "2026-11-01", "alice@x.io", "overwatch"],
      ["Subagent", "", "Main thread", "1000", "200", "4000", "300", "5500", "1.25", "", "10", "1", "2026-11-04T09:00:00Z", "2026-11-04T10:30:00Z", "2026-11-01", "alice@x.io", "overwatch"],
    ]);
  });

  it("should leave the CSV filter cells empty when the summary was not filtered", () => {
    const table = usageCsvTable(buildUsageSummary({ buckets: [bucket] }));
    expect(table.rows[0]!.slice(-2)).toEqual(["", ""]);
  });

  it("should leave the CSV hit-rate cell empty when there was no input, not write the screen's dash", () => {
    const table = usageCsvTable(buildUsageSummary({ buckets: [buildUsageBucket({ cacheHitRate: undefined })] }));
    expect(table.rows[0]![table.headers.indexOf("Cache hit rate")]).toBe("");
  });

  it("should export every split in JSON, keyed by dimension, as raw values", () => {
    const json = usageJsonExport(fullSummary);

    expect(Object.keys(json.splits)).toEqual(SPLIT_DIMENSIONS.map((d) => d.key));
    expect(json.splits.model).toEqual([
      {
        key: "claude-opus-4-1",
        label: "Opus 4.1",
        inputTokens: 10,
        outputTokens: 20,
        cacheReadInputTokens: 30,
        cacheCreationInputTokens: 40,
        totalTokens: 100,
        estimatedCostUsd: 2.5,
        cacheHitRate: 0.5,
        turnCount: 3,
        costShare: 0.25,
        firstAt: "2026-11-04T09:00:00Z",
        lastAt: "2026-11-04T10:00:00Z",
      },
    ]);
    expect(json.splits.subagent[0]).toMatchObject({ key: null, cacheHitRate: null });
  });

  it("should put the server's filter echo on top of the JSON, and null when unfiltered, so the file says what it covers", () => {
    expect(usageJsonExport(fullSummary)).toMatchObject({ developer: "alice@x.io", project: "overwatch" });
    expect(usageJsonExport(buildUsageSummary())).toMatchObject({ developer: null, project: null });
  });

  it("should title the PDF with the period, the filters and the price date", () => {
    expect(usagePdfTitle(fullSummary)).toBe(
      "Developer Overwatch - Usage 2026-11-04 to 2026-11-10, developer alice@x.io, project overwatch (API-equivalent estimate, prices as of 2026-11-01)",
    );
    expect(usagePdfTitle(buildUsageSummary({ pricesAsOf: "2026-11-01" }))).toBe(
      "Developer Overwatch - Usage 2026-11-04 to 2026-11-10 (API-equivalent estimate, prices as of 2026-11-01)",
    );
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
