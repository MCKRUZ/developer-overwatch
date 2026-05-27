import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import { Routes, Route } from "react-router-dom";
import SessionDetailPage from "./SessionDetailPage";
import { api } from "../api";
import {
  buildSession,
  buildConversationTurn,
  buildGovernanceViolation,
  buildFileChange,
  buildArchitectAnnotation,
} from "../test/factories";

vi.mock("../api", () => ({
  api: {
    sessions: {
      get: vi.fn(),
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
      buildSession({
        id: "abcdef123456-rest-of-id",
        repoPath: "org/my-repo",
        status: "Completed",
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0 }),
          buildConversationTurn({ id: "t2", turnIndex: 1 }),
        ],
      }),
    );
    renderWithRoute("abcdef123456-rest-of-id");
    await waitFor(() => {
      expect(screen.getByText("abcdef123456...")).toBeInTheDocument();
    });
    expect(screen.getByText("org/my-repo")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("renders back link to sessions list", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSession());
    renderWithRoute("sess-001");
    await waitFor(() => {
      const link = screen.getByRole("link", { name: /Back to Sessions/ });
      expect(link).toHaveAttribute("href", "/sessions");
    });
  });

  it("renders governance violations with severity badges in collapsible section", async () => {
    const user = userEvent.setup();
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
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

    // Severity breakdown badges should be visible in the header
    expect(screen.getByText(/1 Critical/)).toBeInTheDocument();
    expect(screen.getByText(/1 Warning/)).toBeInTheDocument();

    // Expand the section to see violation details
    await user.click(screen.getByText("Governance Violations (2)"));

    expect(screen.getByText("no-console")).toBeInTheDocument();
    expect(screen.getByText("console.log found")).toBeInTheDocument();
    expect(screen.getByText("Warning")).toBeInTheDocument();
    expect(screen.getByText("Critical")).toBeInTheDocument();
    expect(screen.getByText(/Resolved by admin: False positive/)).toBeInTheDocument();
  });

  it("renders conversation timeline with role-based styling", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, role: "user", content: "Fix the bug" }),
          buildConversationTurn({ id: "t2", turnIndex: 1, role: "assistant", content: "Looking at it now" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Fix the bug")).toBeInTheDocument();
    });
    expect(screen.getByText("Looking at it now")).toBeInTheDocument();
    expect(screen.getByText("user")).toBeInTheDocument();
    expect(screen.getByText("assistant")).toBeInTheDocument();
  });

  it("highlights decision turns", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, isDecision: true, content: "Decided to use Result pattern" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("Decision")).toBeInTheDocument();
    });
  });

  it("shows token counts on turns", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, tokenCount: 150 }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("150 tokens")).toBeInTheDocument();
    });
  });

  it("renders file changes in timeline", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
        fileChanges: [
          buildFileChange({ id: "fc1", filePath: "src/app.ts", changeType: "Modified", diffText: "+new line" }),
        ],
      }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("src/app.ts")).toBeInTheDocument();
    });
    expect(screen.getByText("Modified")).toBeInTheDocument();
    // Diff text is behind an expand button
    expect(screen.getByText("Show diff")).toBeInTheDocument();
  });

  it("renders existing annotations", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({
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
    const initialSession = buildSession({ architectAnnotations: [] });
    const updatedSession = buildSession({
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
    mockApi.sessions.get.mockResolvedValue(buildSession());
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

  it("shows empty timeline message when no events exist", async () => {
    mockApi.sessions.get.mockResolvedValue(
      buildSession({ conversationTurns: [], toolExecutions: [], fileChanges: [] }),
    );
    renderWithRoute("sess-001");
    await waitFor(() => {
      expect(screen.getByText("No events recorded for this session.")).toBeInTheDocument();
    });
  });

  it("fetches violations from governance API", async () => {
    mockApi.sessions.get.mockResolvedValue(buildSession({ governanceViolations: [] }));
    mockApi.governance.violations.mockResolvedValue([
      buildGovernanceViolation({ id: "v-fetched", ruleId: "fetched-rule", severity: "Error", message: "Fetched violation" }),
    ]);

    renderWithRoute("sess-001");

    await waitFor(() => {
      expect(mockApi.governance.violations).toHaveBeenCalledWith("sess-001");
    });
    expect(screen.getByText("Governance Violations (1)")).toBeInTheDocument();
  });
});
