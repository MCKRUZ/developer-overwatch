import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act } from "@testing-library/react";
import { render, screen, waitFor, userEvent } from "../test/render";
import UsagePage from "./UsagePage";
import { api } from "../api";
import { exportCsv } from "../utils/exportCsv";
import { exportJson } from "../utils/exportJson";
import { exportPdf } from "../utils/exportPdf";
import { buildRunningSession, buildUsageSplitRow, buildUsageSplits, buildUsageSummary } from "../test/usageFactories";

vi.mock("../api", () => ({
  api: {
    pricing: { get: vi.fn() },
    usage: { summary: vi.fn(), running: vi.fn() },
  },
}));

vi.mock("../utils/exportCsv", () => ({ exportCsv: vi.fn() }));
vi.mock("../utils/exportJson", () => ({ exportJson: vi.fn() }));
vi.mock("../utils/exportPdf", () => ({ exportPdf: vi.fn() }));

const live = vi.hoisted(() => ({
  onChange: null as (() => void) | null,
  onResync: null as (() => void) | null,
}));

// Throttling is pinned in useLiveRefresh.test and UsagePage.budget.test; here events act at once.
vi.mock("../hooks/useLiveRefresh", () => ({
  useTrailingThrottle: (fn: () => void) => fn,
  useLiveEvents: (handlers: { onChange: () => void; onResync: () => void }) => {
    live.onChange = handlers.onChange;
    live.onResync = handlers.onResync;
  },
}));

const mockApi = vi.mocked(api, true);

const summary = buildUsageSummary({
  pricesAsOf: "2026-11-01",
  splits: buildUsageSplits({
    developer: [buildUsageSplitRow({ key: "alice@x.io", label: "alice@x.io" })],
    project: [buildUsageSplitRow({ key: "overwatch", label: "overwatch" })],
  }),
});

describe("UsagePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 10, 10, 9, 0));
    mockApi.usage.summary.mockResolvedValue(summary);
    mockApi.usage.running.mockResolvedValue([buildRunningSession({ project: "overwatch" })]);
  });

  afterEach(() => vi.useRealTimers());

  it("should load the last 7 local days by day when it opens", async () => {
    render(<UsagePage />);

    await screen.findByTestId("tile-cost");
    expect(mockApi.usage.summary).toHaveBeenCalledWith({
      from: "2026-11-04",
      to: "2026-11-10",
      granularity: "day",
      developer: undefined,
      project: undefined,
    });
  });

  it("should label dollar figures with the price date the summary was computed with", async () => {
    render(<UsagePage />);

    expect(await screen.findByText("API-equivalent estimate (prices as of 2026-11-01)")).toBeInTheDocument();
    expect(mockApi.pricing.get).not.toHaveBeenCalled();
  });

  it("should show running sessions beside the period totals", async () => {
    render(<UsagePage />);
    expect(await screen.findByText("1 running")).toBeInTheDocument();
  });

  it("should refetch with the chosen developer and project so a lead can narrow the view", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    const user = userEvent.setup();

    await user.selectOptions(screen.getByLabelText("Developer"), "alice@x.io");
    await user.selectOptions(screen.getByLabelText("Project"), "overwatch");

    await waitFor(() =>
      expect(mockApi.usage.summary).toHaveBeenLastCalledWith(
        expect.objectContaining({ developer: "alice@x.io", project: "overwatch" }),
      ),
    );
  });

  it("should refetch the new granularity when the period changes", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");

    await userEvent.setup().selectOptions(screen.getByLabelText("Granularity"), "month");

    await waitFor(() =>
      expect(mockApi.usage.summary).toHaveBeenLastCalledWith(expect.objectContaining({ granularity: "month" })),
    );
  });

  it("should refresh the running list on a live hub event without a manual refresh (rule 1), and leave the summary to its interval", async () => {
    render(<UsagePage />);
    await screen.findByText("1 running");
    mockApi.usage.running.mockResolvedValue([buildRunningSession({ id: "a" }), buildRunningSession({ id: "b" })]);

    act(() => live.onChange?.());

    expect(await screen.findByText("2 running")).toBeInTheDocument();
    expect(mockApi.usage.summary).toHaveBeenCalledTimes(1);
  });

  it("should refetch the summary when the hub reconnects, so totals catch up on events missed while it was down", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    mockApi.usage.summary.mockResolvedValue(
      buildUsageSummary({ totals: { ...summary.totals, estimatedCostUsd: 14.2 } }),
    );

    act(() => live.onResync?.());

    await waitFor(() => expect(screen.getByTestId("tile-cost")).toHaveTextContent("$14.20"));
  });

  it("should export the buckets as CSV, JSON and PDF from the same columns, with the price date", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    const user = userEvent.setup();

    await user.click(screen.getByText("Export CSV"));
    await user.click(screen.getByText("Export JSON"));
    await user.click(screen.getByText("Export PDF"));

    expect(vi.mocked(exportCsv).mock.calls[0]![0]).toBe("usage-2026-11-04-to-2026-11-10");
    expect(vi.mocked(exportCsv).mock.calls[0]![1]).toContain("Prices as of");
    expect(vi.mocked(exportJson).mock.calls[0]![1]).toMatchObject({ pricesAsOf: "2026-11-01" });
    expect(vi.mocked(exportPdf).mock.calls[0]![1]).toContain("prices as of 2026-11-01");
  });

  it("should show the error when the summary cannot be loaded", async () => {
    mockApi.usage.summary.mockRejectedValue(new Error("400: bad range"));
    render(<UsagePage />);
    expect(await screen.findByText("400: bad range")).toBeInTheDocument();
  });

  it("should keep the period totals when only the running list fails", async () => {
    mockApi.usage.running.mockRejectedValue(new Error("503: busy"));
    render(<UsagePage />);
    expect(await screen.findByTestId("tile-cost")).toBeInTheDocument();
    expect(await screen.findByText("Running sessions unavailable: 503: busy")).toBeInTheDocument();
  });

  it("should ignore a stale response that lands after a newer filter, so the screen matches the filters", async () => {
    let resolveOld: (s: typeof summary) => void = () => {};
    mockApi.usage.summary.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveOld = resolve;
      }),
    );
    mockApi.usage.summary.mockResolvedValueOnce(
      buildUsageSummary({ granularity: "month", totals: { ...summary.totals, estimatedCostUsd: 99 } }),
    );
    render(<UsagePage />);

    await userEvent.setup().selectOptions(screen.getByLabelText("Granularity"), "month");
    await waitFor(() => expect(screen.getByTestId("tile-cost")).toHaveTextContent("$99.00"));
    await act(async () => resolveOld(buildUsageSummary({ totals: { ...summary.totals, estimatedCostUsd: 1 } })));

    expect(screen.getByTestId("tile-cost")).toHaveTextContent("$99.00");
  });

  it("should show loading and hide the old period's numbers while a new period loads, so old totals never sit under the new label", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    mockApi.usage.summary.mockReturnValue(new Promise(() => {}));

    await userEvent.setup().selectOptions(screen.getByLabelText("Granularity"), "month");

    expect(await screen.findByText("Loading usage...")).toBeInTheDocument();
    expect(screen.queryByTestId("tile-cost")).not.toBeInTheDocument();
    expect(screen.queryByText("Export CSV")).not.toBeInTheDocument();
  });

  it("should show only the error when the new period fails, never the previous period's numbers", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    mockApi.usage.summary.mockRejectedValue(new Error("500: boom"));

    await userEvent.setup().selectOptions(screen.getByLabelText("Developer"), "alice@x.io");

    expect(await screen.findByText("500: boom")).toBeInTheDocument();
    expect(screen.queryByTestId("tile-cost")).not.toBeInTheDocument();
  });

  it("should keep the numbers beside the error when a refresh of the same period fails, since they still describe that period", async () => {
    render(<UsagePage />);
    await screen.findByTestId("tile-cost");
    mockApi.usage.summary.mockRejectedValue(new Error("503: busy"));

    act(() => live.onResync?.());

    expect(await screen.findByText("503: busy")).toBeInTheDocument();
    expect(screen.getByTestId("tile-cost")).toBeInTheDocument();
  });

  it("should head the bucket table with the granularity the numbers were computed for", async () => {
    mockApi.usage.summary.mockResolvedValue({ ...summary, granularity: "week" });
    render(<UsagePage />);

    expect(await screen.findByText("By week")).toBeInTheDocument();
  });
});
