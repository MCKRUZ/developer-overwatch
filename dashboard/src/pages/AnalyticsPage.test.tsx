import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "../test/render";
import AnalyticsPage from "./AnalyticsPage";
import { api } from "../api";
import {
  buildTeamAnalytics,
  buildDeveloperMetrics,
  buildSession,
  buildToolExecution,
  buildGovernanceViolation,
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
  sessions?: ReturnType<typeof buildSession>[],
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
    it("renders all four chart titles", async () => {
      mockDefaults(
        {
          developers: [
            buildDeveloperMetrics({ email: "a@test.com", sessionCount: 5 }),
          ],
        },
        [
          buildSession({
            id: "s1",
            startedAt: "2026-05-27T08:00:00Z",
            toolExecutions: [buildToolExecution({ toolName: "Read" })],
            governanceViolations: [
              buildGovernanceViolation({ severity: "Warning" }),
            ],
          }),
        ],
      );
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Sessions Over Time")).toBeInTheDocument();
      });
      expect(screen.getByText("Tool Usage Distribution")).toBeInTheDocument();
      expect(screen.getByText("Developer Activity")).toBeInTheDocument();
      expect(screen.getByText("Governance Violations by Severity")).toBeInTheDocument();
    });

    it("shows empty chart messages when no session data", async () => {
      mockDefaults({ developers: [] }, []);
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("No session data available")).toBeInTheDocument();
      });
      expect(screen.getByText("No tool usage data available")).toBeInTheDocument();
      expect(screen.getByText("No developer data available")).toBeInTheDocument();
      expect(screen.getByText("No violation data available")).toBeInTheDocument();
    });

    it("renders charts with session data", async () => {
      mockDefaults(
        {
          developers: [
            buildDeveloperMetrics({ email: "dev@test.com", sessionCount: 3, commitCount: 7 }),
          ],
        },
        [
          buildSession({
            id: "s1",
            startedAt: "2026-05-26T10:00:00Z",
            toolExecutions: [
              buildToolExecution({ toolName: "Read" }),
              buildToolExecution({ toolName: "Write" }),
              buildToolExecution({ toolName: "Read" }),
            ],
            governanceViolations: [
              buildGovernanceViolation({ severity: "Critical" }),
              buildGovernanceViolation({ severity: "Warning" }),
            ],
          }),
          buildSession({
            id: "s2",
            startedAt: "2026-05-27T10:00:00Z",
            toolExecutions: [buildToolExecution({ toolName: "Bash" })],
            governanceViolations: [
              buildGovernanceViolation({ severity: "Error" }),
            ],
          }),
        ],
      );
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Sessions Over Time")).toBeInTheDocument();
      });
      // Chart titles should be present (Recharts renders SVG internally)
      expect(screen.getByText("Tool Usage Distribution")).toBeInTheDocument();
      expect(screen.getByText("Governance Violations by Severity")).toBeInTheDocument();
    });

    it("shows error when sessions fetch fails", async () => {
      mockApi.analytics.team.mockResolvedValue(buildTeamAnalytics());
      mockApi.sessions.list.mockRejectedValue(new Error("Session fetch failed"));
      render(<AnalyticsPage />);
      await waitFor(() => {
        expect(screen.getByText("Error: Session fetch failed")).toBeInTheDocument();
      });
    });
  });
});
