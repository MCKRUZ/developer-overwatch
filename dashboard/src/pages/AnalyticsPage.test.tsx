import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import AnalyticsPage, { deriveSessionsByDayAndUser } from "./AnalyticsPage";
import { api } from "../api";
import {
  buildTeamAnalytics,
  buildDeveloperMetrics,
  buildSessionListItem,
  buildDailyCostUsage,
} from "../test/factories";

vi.mock("../api", () => ({
  api: {
    analytics: {
      team: vi.fn(),
    },
    sessions: {
      list: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

function mockDefaults(
  teamOverrides?: Parameters<typeof buildTeamAnalytics>[0],
  sessions?: ReturnType<typeof buildSessionListItem>[],
) {
  mockApi.analytics.team.mockResolvedValue(buildTeamAnalytics(teamOverrides));
  mockApi.sessions.list.mockResolvedValue(sessions ?? []);
}

describe("AnalyticsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.analytics.team.mockReturnValue(new Promise(() => {}));
    mockApi.sessions.list.mockReturnValue(new Promise(() => {}));
    render(<AnalyticsPage />);
    expect(screen.getByText("Loading analytics...")).toBeInTheDocument();
  });

  it("shows error state on fetch failure", async () => {
    mockApi.analytics.team.mockRejectedValue(new Error("Service unavailable"));
    mockApi.sessions.list.mockResolvedValue([]);
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Service unavailable")).toBeInTheDocument();
    });
  });

  it("renders metric cards with correct values", async () => {
    mockDefaults({
      sessionsThisWeek: 15,
      pendingPullRequests: 12,
      violationsThisWeek: 3,
      totalSessions: 100,
      totalPullRequests: 50,
      totalViolations: 25,
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("Sessions This Week")).toBeInTheDocument();
    });
    expect(screen.getByText("15")).toBeInTheDocument();
    expect(screen.getByText("Pending PRs")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("Violations This Week")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getByText("Total Sessions")).toBeInTheDocument();
    expect(screen.getByText("100")).toBeInTheDocument();
    expect(screen.getByText("Total Pull Requests")).toBeInTheDocument();
    expect(screen.getByText("Total Violations")).toBeInTheDocument();
  });

  it("calculates PR approval rate correctly", async () => {
    mockDefaults({
      totalPullRequests: 50,
      approvedPullRequests: 30,
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("60%")).toBeInTheDocument();
    });
    expect(screen.getByText("PR Approval Rate")).toBeInTheDocument();
  });

  it("shows 0% approval rate when no PRs", async () => {
    mockDefaults({
      totalPullRequests: 0,
      approvedPullRequests: 0,
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("0%")).toBeInTheDocument();
    });
  });

  it("renders developer breakdown table", async () => {
    mockDefaults({
      developers: [
        buildDeveloperMetrics({ email: "alice@test.com", sessionCount: 10, commitCount: 20, violationCount: 0 }),
        buildDeveloperMetrics({ email: "bob@test.com", sessionCount: 5, commitCount: 8, violationCount: 3 }),
      ],
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    });
    expect(screen.getByText("bob@test.com")).toBeInTheDocument();
    expect(screen.getByText("Developer Breakdown")).toBeInTheDocument();
  });

  it("highlights violation count in red when > 0", async () => {
    mockDefaults({
      developers: [
        buildDeveloperMetrics({ email: "dev@test.com", violationCount: 5 }),
      ],
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("dev@test.com")).toBeInTheDocument();
    });
    const violationCell = screen.getByText("5");
    expect(violationCell).toHaveClass("text-red-600", "font-medium");
  });

  it("does not highlight zero violations", async () => {
    mockDefaults({
      developers: [
        buildDeveloperMetrics({ email: "clean@test.com", violationCount: 0 }),
      ],
    });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("clean@test.com")).toBeInTheDocument();
    });
    const zeroCell = screen.getByText("0");
    expect(zeroCell).not.toHaveClass("text-red-600");
  });

  it("shows empty developer data message", async () => {
    mockDefaults({ developers: [] });
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("No developer data.")).toBeInTheDocument();
    });
  });

  describe("charts", () => {
    it("renders chart titles", async () => {
      mockDefaults(
        {
          developers: [
            buildDeveloperMetrics({ email: "a@test.com", sessionCount: 5 }),
          ],
          dailyCostTrend: [buildDailyCostUsage({ date: "2026-05-27", estimatedCostUsd: 1.23 })],
        },
        [
          buildSessionListItem({
            id: "s1",
            startedAt: "2026-05-27T08:00:00Z",
          }),
        ],
      );
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Sessions Per Day by Developer")).toBeInTheDocument();
      });
      expect(screen.getByText("Daily Cost Trend")).toBeInTheDocument();
      expect(screen.getByText("Developer Activity")).toBeInTheDocument();
    });

    it("shows a warning marker in the title when a day exceeds the cost threshold", async () => {
      mockDefaults(
        {
          dailyCostThresholdUsd: 5,
          dailyCostTrend: [buildDailyCostUsage({ date: "2026-05-27", estimatedCostUsd: 10 })],
        },
        [],
      );
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Daily Cost Trend ⚠")).toBeInTheDocument();
      });
    });

    it("shows empty chart messages when no session/cost data", async () => {
      mockDefaults({ developers: [], dailyCostTrend: [] }, []);
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("No session data available")).toBeInTheDocument();
      });
      expect(screen.getByText("No cost data available")).toBeInTheDocument();
      expect(screen.getByText("No developer data available")).toBeInTheDocument();
    });

    it("shows error when sessions fetch fails", async () => {
      mockApi.analytics.team.mockResolvedValue(buildTeamAnalytics());
      mockApi.sessions.list.mockRejectedValue(new Error("Session fetch failed"));
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Error: Session fetch failed")).toBeInTheDocument();
      });
    });

    it("refetches team analytics and sessions scoped to the selected developer", async () => {
      const user = userEvent.setup();
      mockDefaults({}, [
        buildSessionListItem({ id: "s1", developerEmail: "alice@test.com" }),
        buildSessionListItem({ id: "s2", developerEmail: "bob@test.com" }),
      ]);
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByLabelText("Developer")).toBeInTheDocument();
      });

      await user.selectOptions(screen.getByLabelText("Developer"), "alice@test.com");

      await waitFor(() => {
        expect(mockApi.analytics.team).toHaveBeenLastCalledWith(undefined, undefined, "alice@test.com");
      });
      expect(mockApi.sessions.list).toHaveBeenLastCalledWith({ developer: "alice@test.com" });
    });
  });
});

describe("deriveSessionsByDayAndUser", () => {
  it("buckets session counts per day per developer", () => {
    const sessions = [
      buildSessionListItem({ id: "s1", developerEmail: "a@test.com", startedAt: "2026-05-27T08:00:00Z" }),
      buildSessionListItem({ id: "s2", developerEmail: "a@test.com", startedAt: "2026-05-27T09:00:00Z" }),
      buildSessionListItem({ id: "s3", developerEmail: "b@test.com", startedAt: "2026-05-27T10:00:00Z" }),
      buildSessionListItem({ id: "s4", developerEmail: "b@test.com", startedAt: "2026-05-28T10:00:00Z" }),
    ];
    const { data, developers } = deriveSessionsByDayAndUser(sessions);

    expect(developers).toEqual(["a@test.com", "b@test.com"]);
    expect(data).toEqual([
      { date: "2026-05-27", total: 3, "a@test.com": 2, "b@test.com": 1 },
      { date: "2026-05-28", total: 1, "a@test.com": 0, "b@test.com": 1 },
    ]);
  });

  it("folds developers beyond the 8-slot palette into an Other bucket", () => {
    const sessions = Array.from({ length: 9 }, (_, i) =>
      buildSessionListItem({
        id: `s${i}`,
        developerEmail: `dev${i}@test.com`,
        startedAt: "2026-05-27T08:00:00Z",
      }),
    );
    const { data, developers } = deriveSessionsByDayAndUser(sessions);

    expect(developers).toHaveLength(9);
    expect(developers.at(-1)).toBe("Other");
    expect(data[0].total).toBe(9);
    expect(data[0]["Other"]).toBe(1);
  });
});
