import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "../test/render";
import AnalyticsPage from "./AnalyticsPage";
import { api } from "../api";
import { buildTeamAnalytics, buildDeveloperMetrics } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    analytics: {
      team: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("AnalyticsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.analytics.team.mockReturnValue(new Promise(() => {}));
    render(<AnalyticsPage />);
    expect(screen.getByText("Loading analytics...")).toBeInTheDocument();
  });

  it("shows error state on fetch failure", async () => {
    mockApi.analytics.team.mockRejectedValue(new Error("Service unavailable"));
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Service unavailable")).toBeInTheDocument();
    });
  });

  it("renders metric cards with correct values", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        sessionsThisWeek: 15,
        pendingPullRequests: 12,
        violationsThisWeek: 3,
        totalSessions: 100,
        totalPullRequests: 50,
        totalViolations: 25,
      }),
    );
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
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        totalPullRequests: 50,
        approvedPullRequests: 30,
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("60%")).toBeInTheDocument();
    });
    expect(screen.getByText("PR Approval Rate")).toBeInTheDocument();
  });

  it("shows 0% approval rate when no PRs", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        totalPullRequests: 0,
        approvedPullRequests: 0,
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("0%")).toBeInTheDocument();
    });
  });

  it("renders developer breakdown table", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "alice@test.com", sessionCount: 10, commitCount: 20, violationCount: 0 }),
          buildDeveloperMetrics({ email: "bob@test.com", sessionCount: 5, commitCount: 8, violationCount: 3 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    });
    expect(screen.getByText("bob@test.com")).toBeInTheDocument();
    expect(screen.getByText("Developer Breakdown")).toBeInTheDocument();
  });

  it("highlights violation count in red when > 0", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "dev@test.com", violationCount: 5 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("dev@test.com")).toBeInTheDocument();
    });
    const violationCell = screen.getByText("5");
    expect(violationCell).toHaveClass("text-red-600", "font-medium");
  });

  it("does not highlight zero violations", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({
        developers: [
          buildDeveloperMetrics({ email: "clean@test.com", violationCount: 0 }),
        ],
      }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("clean@test.com")).toBeInTheDocument();
    });
    const zeroCell = screen.getByText("0");
    expect(zeroCell).not.toHaveClass("text-red-600");
  });

  it("shows empty developer data message", async () => {
    mockApi.analytics.team.mockResolvedValue(
      buildTeamAnalytics({ developers: [] }),
    );
    render(<AnalyticsPage />);
    await waitFor(() => {
      expect(screen.getByText("No developer data.")).toBeInTheDocument();
    });
  });
});
