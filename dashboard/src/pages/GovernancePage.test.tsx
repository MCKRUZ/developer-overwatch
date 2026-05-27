import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import GovernancePage from "./GovernancePage";
import { api } from "../api";
import { buildSession, buildGovernanceViolation } from "../test/factories";

vi.mock("../api", () => ({
  api: {
    sessions: {
      list: vi.fn(),
    },
    governance: {
      violations: vi.fn(),
      resolve: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("GovernancePage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows loading state initially", () => {
    mockApi.sessions.list.mockReturnValue(new Promise(() => {}));
    render(<GovernancePage />);
    expect(screen.getByText("Loading violations...")).toBeInTheDocument();
  });

  it("renders violation list from multiple sessions", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001", developerEmail: "alice@test.com" }),
      buildSession({ id: "sess-002", developerEmail: "bob@test.com" }),
    ]);
    mockApi.governance.violations
      .mockResolvedValueOnce([
        buildGovernanceViolation({ id: "v1", sessionId: "sess-001", ruleId: "no-console-log", severity: "Warning", message: "console.log found" }),
      ])
      .mockResolvedValueOnce([
        buildGovernanceViolation({ id: "v2", sessionId: "sess-002", ruleId: "no-secrets", severity: "Critical", message: "Secret exposed" }),
      ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("no-console-log")).toBeInTheDocument();
    });
    expect(screen.getByText("no-secrets")).toBeInTheDocument();
    expect(screen.getByText("console.log found")).toBeInTheDocument();
    expect(screen.getByText("Secret exposed")).toBeInTheDocument();
  });

  it("filters by severity", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v1", ruleId: "rule-warn", severity: "Warning", message: "A warning" }),
      buildGovernanceViolation({ id: "v2", ruleId: "rule-crit", severity: "Critical", message: "A critical issue" }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("rule-warn")).toBeInTheDocument();
    });
    expect(screen.getByText("rule-crit")).toBeInTheDocument();

    const severitySelect = screen.getByLabelText("Filter by severity");
    await user.selectOptions(severitySelect, "Critical");

    expect(screen.queryByText("rule-warn")).not.toBeInTheDocument();
    expect(screen.getByText("rule-crit")).toBeInTheDocument();
  });

  it("filters by resolved status", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v1", ruleId: "resolved-rule", resolved: true, resolvedBy: "admin@test.com", resolutionReason: "Fixed" }),
      buildGovernanceViolation({ id: "v2", ruleId: "open-rule", resolved: false }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("resolved-rule")).toBeInTheDocument();
    });
    expect(screen.getByText("open-rule")).toBeInTheDocument();

    const statusSelect = screen.getByLabelText("Filter by status");
    await user.selectOptions(statusSelect, "unresolved");

    expect(screen.queryByText("resolved-rule")).not.toBeInTheDocument();
    expect(screen.getByText("open-rule")).toBeInTheDocument();
  });

  it("expands violation to show details and resolve button", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({
        id: "v1",
        ruleId: "expandable-rule",
        resolved: false,
        contextJson: '{"file":"src/main.ts","line":42}',
      }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("expandable-rule")).toBeInTheDocument();
    });

    expect(screen.queryByText("Resolve")).not.toBeInTheDocument();

    const row = screen.getByText("expandable-rule").closest("[role='button']")!;
    await user.click(row);

    expect(screen.getByText("Resolve")).toBeInTheDocument();
    expect(screen.getByText("Context")).toBeInTheDocument();
  });

  it("shows resolve form when Resolve button clicked", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v1", ruleId: "to-resolve", resolved: false }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("to-resolve")).toBeInTheDocument();
    });

    const row = screen.getByText("to-resolve").closest("[role='button']")!;
    await user.click(row);

    const resolveBtn = screen.getByText("Resolve");
    await user.click(resolveBtn);

    expect(screen.getByLabelText("Resolved By")).toBeInTheDocument();
    expect(screen.getByLabelText("Reason")).toBeInTheDocument();
    expect(screen.getByText("Submit")).toBeInTheDocument();
    expect(screen.getByText("Cancel")).toBeInTheDocument();
  });

  it("submits resolve form and updates violation", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v1", ruleId: "fix-me", resolved: false }),
    ]);
    mockApi.governance.resolve.mockResolvedValue({ violationId: "v1" });

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("fix-me")).toBeInTheDocument();
    });

    const row = screen.getByText("fix-me").closest("[role='button']")!;
    await user.click(row);
    await user.click(screen.getByText("Resolve"));

    await user.type(screen.getByLabelText("Resolved By"), "admin@test.com");
    await user.type(screen.getByLabelText("Reason"), "Fixed the issue");
    await user.click(screen.getByText("Submit"));

    await waitFor(() => {
      expect(mockApi.governance.resolve).toHaveBeenCalledWith("v1", {
        resolvedBy: "admin@test.com",
        resolutionReason: "Fixed the issue",
      });
    });
  });

  it("handles empty state", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("No violations found.")).toBeInTheDocument();
    });
  });

  it("handles empty state with no sessions", async () => {
    mockApi.sessions.list.mockResolvedValue([]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("No violations found.")).toBeInTheDocument();
    });
  });

  it("shows error state on fetch failure", async () => {
    mockApi.sessions.list.mockRejectedValue(new Error("Network error"));

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("Error: Network error")).toBeInTheDocument();
    });
  });

  it("displays severity metric cards with correct counts", async () => {
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v1", severity: "Critical" }),
      buildGovernanceViolation({ id: "v2", severity: "Warning" }),
      buildGovernanceViolation({ id: "v3", severity: "Warning" }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("Total")).toBeInTheDocument();
    });

    const criticalLabel = screen.getByText("Critical", { selector: ".text-sm.text-gray-500" });
    const criticalCard = criticalLabel.closest(".bg-white.rounded-lg.border.p-4")!;
    expect(criticalCard.querySelector(".text-2xl")!.textContent).toBe("1");

    const warningLabel = screen.getByText("Warning", { selector: ".text-sm.text-gray-500" });
    const warningCard = warningLabel.closest(".bg-white.rounded-lg.border.p-4")!;
    expect(warningCard.querySelector(".text-2xl")!.textContent).toBe("2");
  });

  it("shows resolved violation details in expanded view", async () => {
    const user = userEvent.setup();
    mockApi.sessions.list.mockResolvedValue([
      buildSession({ id: "sess-001" }),
    ]);
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({
        id: "v1",
        ruleId: "resolved-detail",
        resolved: true,
        resolvedBy: "admin@test.com",
        resolutionReason: "All good now",
        resolvedAt: "2026-05-27T10:00:00Z",
      }),
    ]);

    render(<GovernancePage />);

    await waitFor(() => {
      expect(screen.getByText("resolved-detail")).toBeInTheDocument();
    });

    const row = screen.getByText("resolved-detail").closest("[role='button']")!;
    await user.click(row);

    expect(screen.getByText("Resolution")).toBeInTheDocument();
    expect(screen.getByText("admin@test.com")).toBeInTheDocument();
    expect(screen.getByText("All good now")).toBeInTheDocument();
  });
});
