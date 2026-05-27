import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within, userEvent } from "../test/render";
import PrQueuePage from "./PrQueuePage";
import { api } from "../api";
import { buildPullRequestReview } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    pullRequests: {
      list: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("PrQueuePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.pullRequests.list.mockReturnValue(new Promise(() => {}));
    render(<PrQueuePage />);
    expect(screen.getByText("Loading pull requests...")).toBeInTheDocument();
  });

  it("calls api.pullRequests.list() on mount", async () => {
    mockApi.pullRequests.list.mockResolvedValue([]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(mockApi.pullRequests.list).toHaveBeenCalledTimes(1);
    });
  });

  it("renders metric cards with correct counts", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", status: "Pending" }),
      buildPullRequestReview({ id: "2", status: "Approved" }),
      buildPullRequestReview({ id: "3", status: "NeedsChanges" }),
      buildPullRequestReview({ id: "4", status: "Pending" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("4")).toBeInTheDocument();
    });
    expect(screen.getByText("Total PRs")).toBeInTheDocument();
    // Scope metric card assertions to avoid matching dropdown options and StatusBadge spans
    const pendingLabel = screen.getByText("Pending", { selector: ".text-sm.text-gray-500" });
    const pendingCard = pendingLabel.closest(".bg-white.rounded-lg.border.p-4")!;
    expect(within(pendingCard).getByText("2")).toBeInTheDocument();
    const approvedLabel = screen.getByText("Approved", { selector: ".text-sm.text-gray-500" });
    const approvedCard = approvedLabel.closest(".bg-white.rounded-lg.border.p-4")!;
    expect(within(approvedCard).getByText("1").closest("div")).toBeTruthy();
  });

  it("renders PR table rows", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 42, repo: "org/repo", status: "Pending", reviewedBy: "reviewer@test.com" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#42")).toBeInTheDocument();
    });
    expect(screen.getByText("org/repo")).toBeInTheDocument();
    expect(screen.getByText("reviewer@test.com")).toBeInTheDocument();
  });

  it("shows empty state when no PRs", async () => {
    mockApi.pullRequests.list.mockResolvedValue([]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("No pull requests found.")).toBeInTheDocument();
    });
  });

  it("shows error state on fetch failure", async () => {
    mockApi.pullRequests.list.mockRejectedValue(new Error("Network error"));
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("Error: Network error")).toBeInTheDocument();
    });
  });

  it("sorts by PR number on header click", async () => {
    const user = userEvent.setup();
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 10 }),
      buildPullRequestReview({ id: "2", prNumber: 5 }),
      buildPullRequestReview({ id: "3", prNumber: 20 }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#10")).toBeInTheDocument();
    });
    const prHeader = screen.getByText(/^PR #/);
    await user.click(prHeader);
    const cells = screen.getAllByText(/^#\d+$/);
    const numbers = cells.map((c) => parseInt(c.textContent!.slice(1)));
    expect(numbers).toEqual([5, 10, 20]);
  });

  it("filters by status dropdown", async () => {
    const user = userEvent.setup();
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 1, status: "Pending" }),
      buildPullRequestReview({ id: "2", prNumber: 2, status: "Approved" }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("#1")).toBeInTheDocument();
    });
    const select = screen.getByRole("combobox");
    await user.selectOptions(select, "Approved");
    expect(screen.queryByText("#1")).not.toBeInTheDocument();
    expect(screen.getByText("#2")).toBeInTheDocument();
  });

  it("shows unreviewed PR with dash for reviewer", async () => {
    mockApi.pullRequests.list.mockResolvedValue([
      buildPullRequestReview({ id: "1", prNumber: 1, reviewedBy: undefined }),
    ]);
    render(<PrQueuePage />);
    await waitFor(() => {
      expect(screen.getByText("—")).toBeInTheDocument();
    });
  });
});
