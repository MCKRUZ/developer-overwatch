import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import { Routes, Route } from "react-router-dom";
import SessionDetailPage from "./SessionDetailPage";
import { api } from "../api";
import {
  buildSessionDetail,
  buildGovernanceViolation,
  buildArchitectAnnotation,
  buildModelUsage,
} from "../test/factories";

vi.mock("../api", () => ({
  api: {
    pricing: { get: vi.fn().mockResolvedValue({ asOf: "2026-11-09" }) },
    sessions: {
      get: vi.fn(),
      dailyUsage: vi.fn(),
      modelBreakdown: vi.fn(),
      timeline: vi.fn(),
    },
    annotations: {
      create: vi.fn(),
    },
    governance: {
      violations: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

function renderWithRoute(sessionId: string) {
  return render(
    <Routes>
      <Route path="/sessions/:sessionId" element={<SessionDetailPage />} />
    </Routes>,
    { initialEntries: [`/sessions/${sessionId}`] },
  );
}

describe("SessionDetailPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApi.governance.violations.mockResolvedValue([]);
    mockApi.sessions.dailyUsage.mockResolvedValue([]);
    mockApi.sessions.modelBreakdown.mockResolvedValue([]);
    mockApi.sessions.timeline.mockResolvedValue({
      conversationTurns: [],
      toolExecutions: [],
      fileChanges: [],
      hasMore: false,
    });
  });

  it("shows loading state initially", () => {
    mockApi.sessions.get.mockReturnValue(new Promise(() => {}));
    renderWithRoute("sess-001");
    expect(screen.getByText("Loading session...")).toBeInTheDocument();
  });

  it("shows error state on fetch failure", async () => {
    mockApi.sessions.get.mockRejectedValue(new Error("Not found"));
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Error: Not found")).toBeInTheDocument();
    });
  });

  it("renders session header metadata", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSessionDetail({
        id: "abcdef123456-rest-of-id",
        repoPath: "org/my-repo",
        status: "Completed",
        conversationTurnCount: 2,
      }),
    );
    renderWithRoute("abcdef123456-rest-of-id");
    await waitFor(() => {
      expect(screen.getByText("abcdef123456...")).toBeInTheDocument();
    });
    expect(screen.getByText("org/my-repo")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("shows the session's custom title instead of the raw GUID when available", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSessionDetail({ id: "abcdef123456-rest-of-id", title: "initial-setup" }),
    );
    renderWithRoute("abcdef123456-rest-of-id");
    await waitFor(() => {
      expect(screen.getByText("initial-setup")).toBeInTheDocument();
    });
    expect(screen.queryByText("abcdef123456...")).not.toBeInTheDocument();
    expect(screen.getByText("abcdef123456-rest-of-id")).toBeInTheDocument();
  });

  it("renders back link to sessions list", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSessionDetail());
    renderWithRoute("sess-001");
    await waitFor(() => {
      const link = screen.getByRole("link", { name: /Back to Sessions/ });
      expect(link).toHaveAttribute("href", "/sessions");
    });
  });

  it("shows total tokens and estimated cost", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSessionDetail({
        totalInputTokens: 100,
        totalOutputTokens: 50,
        totalCacheCreationInputTokens: 850,
        totalCacheReadInputTokens: 4000,
        estimatedCostUsd: 1.2345,
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("5,000")).toBeInTheDocument();
    });
    expect(screen.getByText("$1.23")).toBeInTheDocument();
  });

  it("renders governance violations with severity badges in collapsible section", async () => {
    const user = userEvent.setup();
    mockApi.sessions.get.mockResolvedValue(
      buildSessionDetail({
        governanceViolations: [
          buildGovernanceViolation({ id: "v1", ruleId: "no-console", severity: "Warning", message: "console.log found" }),
          buildGovernanceViolation({ id: "v2", ruleId: "no-secrets", severity: "Critical", message: "Secret exposed", resolved: true, resolvedBy: "admin", resolutionReason: "False positive" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Governance Violations (2)")).toBeInTheDocument();
    });

    expect(screen.getByText(/1 Critical/)).toBeInTheDocument();
    expect(screen.getByText(/1 Warning/)).toBeInTheDocument();

    await user.click(screen.getByText("Governance Violations (2)"));

    expect(screen.getByText("no-console")).toBeInTheDocument();
    expect(screen.getByText("console.log found")).toBeInTheDocument();
    expect(screen.getByText("Warning")).toBeInTheDocument();
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText(/Resolved by admin: False positive/)).toBeInTheDocument();
  });

  it("renders existing annotations", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSessionDetail({
        architectAnnotations: [
          buildArchitectAnnotation({ id: "a1", content: "Solid approach", createdBy: "lead@test.com" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Solid approach")).toBeInTheDocument();
    });
    expect(screen.getByText("lead@test.com")).toBeInTheDocument();
  });

  it("submits annotation form and refetches session", async () => {
    const user = userEvent.setup();
    const initialSession = buildSessionDetail({ architectAnnotations: [] });
    const updatedSession = buildSessionDetail({
      architectAnnotations: [
        buildArchitectAnnotation({ content: "New note", createdBy: "me@test.com" }),
      ],
    });
    mockApi.sessions.get.mockResolvedValueOnce(initialSession);
    mockApi.annotations.create.mockResolvedValue({ annotationId: "a-new" });
    mockApi.sessions.get.mockResolvedValueOnce(updatedSession);

    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByPlaceholderText("Your email")).toBeInTheDocument();
    });

    await user.type(screen.getByPlaceholderText("Your email"), "me@test.com");
    await user.type(screen.getByPlaceholderText("Add annotation..."), "New note");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => {
      expect(mockApi.annotations.create).toHaveBeenCalledWith({
        sessionId: "sess-001",
        content: "New note",
        createdBy: "me@test.com",
      });
    });
    await waitFor(() => {
      expect(mockApi.sessions.get).toHaveBeenCalledTimes(2);
    });
    await waitFor(() => {
      expect(screen.getByText("New note")).toBeInTheDocument();
    });
  });

  it("shows submitting state on annotation button", async () => {
    const user = userEvent.setup();
    mockApi.sessions.get.mockResolvedValue(buildSessionDetail());
    mockApi.annotations.create.mockReturnValue(new Promise(() => {}));

    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByPlaceholderText("Your email")).toBeInTheDocument();
    });

    await user.type(screen.getByPlaceholderText("Your email"), "me@test.com");
    await user.type(screen.getByPlaceholderText("Add annotation..."), "A note");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => {
      expect(screen.getByText("Adding...")).toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Adding..." })).toBeDisabled();
  });

  it("fetches violations from governance API", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSessionDetail({ governanceViolations: [] }));
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v-fetched", ruleId: "fetched-rule", severity: "Error", message: "Fetched violation" }),
    ]);

    renderWithRoute("sess-001");

    await waitFor(() => {
      expect(mockApi.governance.violations).toHaveBeenCalledWith("sess-001");
    });
    expect(screen.getByText("Governance Violations (1)")).toBeInTheDocument();
  });

  it("shows a single stat instead of a pie chart when only one model was used", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSessionDetail());
    mockApi.sessions.modelBreakdown.mockResolvedValue([
      buildModelUsage({ model: "claude-sonnet-5", estimatedCostUsd: 12.34 }),
    ]);
    renderWithRoute("sess-001");

    await waitFor(() => {
      expect(screen.getByText("claude-sonnet-5")).toBeInTheDocument();
    });
    expect(screen.getByText("$12.34")).toBeInTheDocument();
    expect(screen.getByText("100% of this session's cost")).toBeInTheDocument();
  });

  it("shows a pie chart when multiple models were used", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSessionDetail());
    mockApi.sessions.modelBreakdown.mockResolvedValue([
      buildModelUsage({ model: "claude-opus", estimatedCostUsd: 10 }),
      buildModelUsage({ model: "claude-haiku", estimatedCostUsd: 1 }),
    ]);
    renderWithRoute("sess-001");

    await waitFor(() => {
      expect(screen.getByText("Cost by Model")).toBeInTheDocument();
    });
    expect(screen.queryByText("100% of this session's cost")).not.toBeInTheDocument();
  });
});
