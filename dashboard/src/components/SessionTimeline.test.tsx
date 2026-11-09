import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, userEvent } from "../test/render";
import { triggerIntersections } from "../test/setup";
import SessionTimeline from "./SessionTimeline";
import { api } from "../api";
import {
  buildSessionTimelinePage,
  buildConversationTurn,
  buildToolExecution,
  buildFileChange,
} from "../test/factories";

vi.mock("../api", () => ({
  api: {
    sessions: {
      timeline: vi.fn(),
      timelineAround: vi.fn(),
    },
  },
}));

const mockApi = vi.mocked(api);

describe("SessionTimeline", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders empty state when no events exist", async () => {
    mockApi.sessions.timeline.mockResolvedValue(buildSessionTimelinePage());
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("No events recorded for this session.")).toBeInTheDocument();
    });
    expect(screen.getByText("Session Timeline")).toBeInTheDocument();
  });

  it("renders conversation turns with role and content", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({
            id: "t1",
            turnIndex: 0,
            role: "user",
            content: "Help me fix this",
            timestamp: "2026-05-27T08:01:00Z",
          }),
          buildConversationTurn({
            id: "t2",
            turnIndex: 1,
            role: "assistant",
            content: "Looking into it",
            timestamp: "2026-05-27T08:02:00Z",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("user")).toBeInTheDocument();
    });
    expect(screen.getByText("assistant")).toBeInTheDocument();
    expect(screen.getByText("Help me fix this")).toBeInTheDocument();
    expect(screen.getByText("Looking into it")).toBeInTheDocument();
  });

  it("groups tool executions into a single collapsible card showing name and total duration", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        toolExecutions: [
          buildToolExecution({
            id: "tool-1",
            toolName: "Grep",
            durationMs: 250,
            timestamp: "2026-05-27T08:03:00Z",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("Grep")).toBeInTheDocument();
    });
    expect(screen.getByText("1 tool call")).toBeInTheDocument();
    expect(screen.getByText("250ms total")).toBeInTheDocument();
  });

  it("renders file changes with path and change type badge once the group is expanded", async () => {
    const user = userEvent.setup();
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        fileChanges: [
          buildFileChange({ id: "fc-1", filePath: "src/index.ts", changeType: "Created" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("1 file change")).toBeInTheDocument();
    });
    expect(screen.queryByText("src/index.ts")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button"));

    expect(screen.getByText("src/index.ts")).toBeInTheDocument();
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("renders newest-first, grouping the tool call between the two turns", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({
            id: "t1",
            turnIndex: 0,
            role: "user",
            content: "First event",
            timestamp: "2026-05-27T08:01:00Z",
          }),
          buildConversationTurn({
            id: "t3",
            turnIndex: 1,
            role: "assistant",
            content: "Third event",
            timestamp: "2026-05-27T08:05:00Z",
          }),
        ],
        toolExecutions: [
          buildToolExecution({ id: "tool-1", toolName: "Read", timestamp: "2026-05-27T08:03:00Z" }),
        ],
      }),
    );
    const { container } = render(<SessionTimeline sessionId="sess-001" />);

    await waitFor(() => {
      expect(screen.getByText("Third event")).toBeInTheDocument();
    });
    const text = container.textContent ?? "";
    const thirdIdx = text.indexOf("Third event");
    const toolIdx = text.indexOf("Read");
    const firstIdx = text.indexOf("First event");
    expect(thirdIdx).toBeLessThan(toolIdx);
    expect(toolIdx).toBeLessThan(firstIdx);
  });

  it("highlights decision turns with Decision badge", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({
            id: "t1",
            turnIndex: 0,
            isDecision: true,
            content: "Chose Result<T> pattern",
            timestamp: "2026-05-27T08:01:00Z",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("Decision")).toBeInTheDocument();
    });
    expect(screen.getByText("Chose Result<T> pattern")).toBeInTheDocument();
  });

  it("flags turns where the agent redacted a secret", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({
            id: "t1",
            turnIndex: 0,
            content: "here's my [REDACTED]",
            containsRedactedSecret: true,
            timestamp: "2026-05-27T08:01:00Z",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("secret redacted")).toBeInTheDocument();
    });
  });

  it("truncates long conversation content with expand button", async () => {
    const user = userEvent.setup();
    const longContent = "A".repeat(500);
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, content: longContent, timestamp: "2026-05-27T08:01:00Z" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);

    await waitFor(() => {
      expect(screen.getByText("Show more")).toBeInTheDocument();
    });
    expect(screen.queryByText(longContent)).not.toBeInTheDocument();

    await user.click(screen.getByText("Show more"));
    expect(screen.getByText(longContent)).toBeInTheDocument();
    expect(screen.getByText("Show less")).toBeInTheDocument();
  });

  it("expands tool execution details on click", async () => {
    const user = userEvent.setup();
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        toolExecutions: [
          buildToolExecution({
            id: "tool-1",
            toolName: "Write",
            inputParams: '{"path":"src/main.ts"}',
            outputSummary: "File written successfully",
            timestamp: "2026-05-27T08:02:00Z",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /tool call/ })).toBeInTheDocument();
    });
    await user.click(screen.getByRole("button", { name: /tool call/ }));
    await user.click(screen.getByText("Show details"));

    expect(screen.getByText("Input")).toBeInTheDocument();
    expect(screen.getByText('{"path":"src/main.ts"}')).toBeInTheDocument();
    expect(screen.getByText("Output")).toBeInTheDocument();
    expect(screen.getByText("File written successfully")).toBeInTheDocument();
  });

  it("expands file diff text on click", async () => {
    const user = userEvent.setup();
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        fileChanges: [
          buildFileChange({
            id: "fc-1",
            filePath: "src/utils.ts",
            changeType: "Modified",
            diffText: "+added line\n-removed line",
          }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);

    await waitFor(() => {
      expect(screen.getByText("1 file change")).toBeInTheDocument();
    });
    await user.click(screen.getByRole("button"));
    await user.click(screen.getByText("Show diff"));

    expect(screen.getByText(/\+added line/)).toBeInTheDocument();
  });

  it("shows token count on conversation turns", async () => {
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, tokenCount: 500, timestamp: "2026-05-27T08:01:00Z" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("500 tokens")).toBeInTheDocument();
    });
  });

  it("shows a linked file change as a chip on its owning tool once expanded", async () => {
    const user = userEvent.setup();
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        toolExecutions: [
          buildToolExecution({ id: "tool-1", toolName: "Write", timestamp: "2026-05-27T08:10:00Z" }),
        ],
        fileChanges: [
          buildFileChange({ id: "fc-1", filePath: "linked-file.ts", toolExecutionId: "tool-1" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("Write")).toBeInTheDocument();
    });
    await user.click(screen.getByRole("button", { name: /tool call/ }));

    expect(screen.getByText("linked-file.ts")).toBeInTheDocument();
  });

  it("renders all three change type badges correctly", async () => {
    const user = userEvent.setup();
    mockApi.sessions.timeline.mockResolvedValue(
      buildSessionTimelinePage({
        fileChanges: [
          buildFileChange({ id: "fc-1", filePath: "a.ts", changeType: "Created" }),
          buildFileChange({ id: "fc-2", filePath: "b.ts", changeType: "Modified" }),
          buildFileChange({ id: "fc-3", filePath: "c.ts", changeType: "Deleted" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("3 file changes")).toBeInTheDocument();
    });
    await user.click(screen.getByRole("button"));

    expect(screen.getByText("Created")).toBeInTheDocument();
    expect(screen.getByText("Modified")).toBeInTheDocument();
    expect(screen.getByText("Deleted")).toBeInTheDocument();
  });

  it("fetches an older page when the bottom sentinel scrolls into view", async () => {
    mockApi.sessions.timeline.mockResolvedValueOnce(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({ id: "new", turnIndex: 1, content: "newer turn", timestamp: "2026-05-27T08:05:00Z" }),
        ],
        nextCursor: "2026-05-27T08:05:00Z",
        hasMore: true,
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />);
    await waitFor(() => {
      expect(screen.getByText("newer turn")).toBeInTheDocument();
    });
    expect(screen.getByText("Scroll down for older events")).toBeInTheDocument();

    mockApi.sessions.timeline.mockResolvedValueOnce(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({ id: "old", turnIndex: 0, content: "older turn", timestamp: "2026-05-27T08:01:00Z" }),
        ],
        hasMore: false,
      }),
    );
    triggerIntersections(true);

    await waitFor(() => {
      expect(screen.getByText("older turn")).toBeInTheDocument();
    });
    expect(mockApi.sessions.timeline).toHaveBeenLastCalledWith(
      "sess-001",
      "2026-05-27T08:05:00Z",
    );
  });

  it("fetches the anchored page and highlights the target turn when ?turn= is present", async () => {
    mockApi.sessions.timelineAround.mockResolvedValue(
      buildSessionTimelinePage({
        conversationTurns: [
          buildConversationTurn({ id: "t1", turnIndex: 0, content: "before", timestamp: "2026-05-27T08:00:00Z" }),
          buildConversationTurn({ id: "t2", turnIndex: 1, content: "the flagged turn", timestamp: "2026-05-27T08:01:00Z" }),
          buildConversationTurn({ id: "t3", turnIndex: 2, content: "after", timestamp: "2026-05-27T08:02:00Z" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />, {
      initialEntries: ["/sessions/sess-001?turn=t2"],
    });

    await waitFor(() => {
      expect(screen.getByText("the flagged turn")).toBeInTheDocument();
    });
    expect(mockApi.sessions.timelineAround).toHaveBeenCalledWith(
      "sess-001",
      { turnId: "t2", toolExecutionId: undefined },
    );
    expect(screen.getByText("Jumped to flagged item")).toBeInTheDocument();
    expect(screen.getByText("the flagged turn").closest('[data-testid="timeline-turn"]')).toHaveClass(
      "ring-2",
    );
  });

  it("fetches the anchored page and auto-expands the group containing a flagged tool", async () => {
    mockApi.sessions.timelineAround.mockResolvedValue(
      buildSessionTimelinePage({
        toolExecutions: [
          buildToolExecution({ id: "tool-flagged", toolName: "Bash", timestamp: "2026-05-27T08:00:00Z" }),
        ],
      }),
    );
    render(<SessionTimeline sessionId="sess-001" />, {
      initialEntries: ["/sessions/sess-001?tool=tool-flagged"],
    });

    await waitFor(() => {
      expect(screen.getAllByText("Bash").length).toBeGreaterThan(0);
    });
    expect(mockApi.sessions.timelineAround).toHaveBeenCalledWith(
      "sess-001",
      { turnId: undefined, toolExecutionId: "tool-flagged" },
    );
    // Group is auto-expanded — "Show details" only renders inside the expanded tool row.
    expect(screen.getByText("Show details")).toBeInTheDocument();
  });
});
