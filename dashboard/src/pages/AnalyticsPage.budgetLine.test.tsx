import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ReactNode } from "react";
import { render, screen } from "../test/render";
import AnalyticsPage from "./AnalyticsPage";
import { api } from "../api";
import { buildTeamAnalytics, buildDailyCostUsage } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    pricing: { get: vi.fn().mockResolvedValue({ asOf: "2026-11-09" }) },
    analytics: { team: vi.fn() },
    sessions: { list: vi.fn() },
  },
}));

// jsdom gives charts no size, so recharts draws nothing. Stubs keep the page's chart props visible:
// the reference line renders its label text, so the test sees exactly what the page asked recharts to draw.
vi.mock("recharts", () => {
  const Pass = ({ children }: { children?: ReactNode }) => <div>{children}</div>;
  const Nothing = () => null;
  return {
    ResponsiveContainer: Pass,
    BarChart: Pass,
    Bar: Nothing,
    XAxis: Nothing,
    YAxis: Nothing,
    Tooltip: Nothing,
    CartesianGrid: Nothing,
    Legend: Nothing,
    ReferenceLine: ({ y, label, ifOverflow }: { y: number; label: { value: string }; ifOverflow?: string }) => (
      <span>{`${label.value} at ${y} (${ifOverflow ?? "discard"})`}</span>
    ),
  };
});

const mockApi = vi.mocked(api);

function mockTeam(dailyCostThresholdUsd: number | null) {
  mockApi.analytics.team.mockResolvedValue(
    buildTeamAnalytics({
      dailyCostThresholdUsd,
      developers: [],
      dailyCostTrend: [buildDailyCostUsage({ date: "2026-05-27", estimatedCostUsd: 10 })],
    }),
  );
  mockApi.sessions.list.mockResolvedValue([]);
}

describe("AnalyticsPage team daily budget line", () => {
  beforeEach(() => vi.clearAllMocks());

  // Spend of $10 against a $50 budget: recharts discards a line outside the axis unless the axis is
  // extended to reach it, which is the normal case for a team under budget.
  it("should draw the team daily budget as a labelled line that stays visible when spend is below it", async () => {
    mockTeam(50);
    render(<AnalyticsPage />);

    expect(await screen.findByText("Team daily budget at 50 (extendDomain)")).toBeInTheDocument();
    expect(screen.getByText("Daily Cost Trend")).toBeInTheDocument();
  });

  it("should hide the budget line and the warning when the team has no daily budget", async () => {
    mockTeam(null);
    render(<AnalyticsPage />);

    expect(await screen.findByText("Daily Cost Trend")).toBeInTheDocument();
    expect(screen.queryByText(/Team daily budget/)).not.toBeInTheDocument();
    expect(screen.queryByText("Daily Cost Trend ⚠")).not.toBeInTheDocument();
  });
});
