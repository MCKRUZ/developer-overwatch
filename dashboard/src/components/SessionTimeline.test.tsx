import { describe, it, expect } from "vitest";
import { render, screen, userEvent } from "../test/render";
import SessionTimeline from "./SessionTimeline";
import {
  buildSession,
  buildConversationTurn,
  buildToolExecution,
  buildFileChange,
} from "../test/factories";

describe("SessionTimeline", () => {
  it("renders empty state when no events exist", () => {
    const session = buildSession({
      conversationTurns: [],
      toolExecutions: [],
      fileChanges: [],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("No events recorded for this session.")).toBeInTheDocument();
    expect(screen.getByText("Session Timeline")).toBeInTheDocument();
  });

  it("renders conversation turns with role and content", () => {
    const session = buildSession({
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
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("user")).toBeInTheDocument();
    expect(screen.getByText("assistant")).toBeInTheDocument();
    expect(screen.getByText("Help me fix this")).toBeInTheDocument();
    expect(screen.getByText("Looking into it")).toBeInTheDocument();
  });

  it("renders tool executions with name and duration", () => {
    const session = buildSession({
      toolExecutions: [
        buildToolExecution({
          id: "tool-1",
          toolName: "Grep",
          durationMs: 250,
          timestamp: "2026-05-27T08:03:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("Grep")).toBeInTheDocument();
    expect(screen.getByText("250ms")).toBeInTheDocument();
  });

  it("renders file changes with path and change type badge", () => {
    const session = buildSession({
      fileChanges: [
        buildFileChange({
          id: "fc-1",
          filePath: "src/index.ts",
          changeType: "Created",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("src/index.ts")).toBeInTheDocument();
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("merges all event types in chronological order", () => {
    const session = buildSession({
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
        buildToolExecution({
          id: "tool-1",
          toolName: "Read",
          timestamp: "2026-05-27T08:03:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);

    const events = screen.getAllByTestId("timeline-event");
    expect(events).toHaveLength(3);

    // First event: user turn (08:01)
    expect(events[0]).toHaveTextContent("First event");
    // Second event: tool (08:03)
    expect(events[1]).toHaveTextContent("Read");
    // Third event: assistant turn (08:05)
    expect(events[2]).toHaveTextContent("Third event");
  });

  it("highlights decision turns with Decision badge and yellow border", () => {
    const session = buildSession({
      conversationTurns: [
        buildConversationTurn({
          id: "t1",
          turnIndex: 0,
          isDecision: true,
          content: "Chose Result<T> pattern",
          timestamp: "2026-05-27T08:01:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("Decision")).toBeInTheDocument();
    expect(screen.getByText("Chose Result<T> pattern")).toBeInTheDocument();
  });

  it("truncates long conversation content with expand button", async () => {
    const user = userEvent.setup();
    const longContent = "A".repeat(300);
    const session = buildSession({
      conversationTurns: [
        buildConversationTurn({
          id: "t1",
          turnIndex: 0,
          content: longContent,
          timestamp: "2026-05-27T08:01:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);

    // Content should be truncated
    expect(screen.getByText("Show more")).toBeInTheDocument();
    expect(screen.queryByText(longContent)).not.toBeInTheDocument();

    // Expand content
    await user.click(screen.getByText("Show more"));
    expect(screen.getByText(longContent)).toBeInTheDocument();
    expect(screen.getByText("Show less")).toBeInTheDocument();
  });

  it("expands tool execution details on click", async () => {
    const user = userEvent.setup();
    const session = buildSession({
      toolExecutions: [
        buildToolExecution({
          id: "tool-1",
          toolName: "Write",
          inputParams: '{"path":"src/main.ts"}',
          outputSummary: "File written successfully",
          timestamp: "2026-05-27T08:02:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);

    // Input/output not visible initially
    expect(screen.queryByText("Input")).not.toBeInTheDocument();
    expect(screen.queryByText("Output")).not.toBeInTheDocument();

    await user.click(screen.getByText("Show details"));
    expect(screen.getByText("Input")).toBeInTheDocument();
    expect(screen.getByText('{"path":"src/main.ts"}')).toBeInTheDocument();
    expect(screen.getByText("Output")).toBeInTheDocument();
    expect(screen.getByText("File written successfully")).toBeInTheDocument();
  });

  it("expands file diff text on click", async () => {
    const user = userEvent.setup();
    const session = buildSession({
      fileChanges: [
        buildFileChange({
          id: "fc-1",
          filePath: "src/utils.ts",
          changeType: "Modified",
          diffText: "+added line\n-removed line",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);

    // Diff not visible initially
    expect(screen.queryByText("+added line")).not.toBeInTheDocument();
    expect(screen.getByText("Show diff")).toBeInTheDocument();

    await user.click(screen.getByText("Show diff"));
    expect(screen.getByText(/\+added line/)).toBeInTheDocument();
  });

  it("renders correct timeline dots per event type", () => {
    const session = buildSession({
      conversationTurns: [
        buildConversationTurn({ id: "t1", turnIndex: 0, timestamp: "2026-05-27T08:01:00Z" }),
      ],
      toolExecutions: [
        buildToolExecution({ id: "tool-1", timestamp: "2026-05-27T08:02:00Z" }),
      ],
      fileChanges: [
        buildFileChange({ id: "fc-1" }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByTestId("timeline-dot-turn")).toBeInTheDocument();
    expect(screen.getByTestId("timeline-dot-tool")).toBeInTheDocument();
    expect(screen.getByTestId("timeline-dot-file")).toBeInTheDocument();
  });

  it("shows token count on conversation turns", () => {
    const session = buildSession({
      conversationTurns: [
        buildConversationTurn({
          id: "t1",
          turnIndex: 0,
          tokenCount: 500,
          timestamp: "2026-05-27T08:01:00Z",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("500 tokens")).toBeInTheDocument();
  });

  it("uses tool execution timestamp for linked file changes", () => {
    const session = buildSession({
      startedAt: "2026-05-27T08:00:00Z",
      toolExecutions: [
        buildToolExecution({
          id: "tool-1",
          toolName: "Write",
          timestamp: "2026-05-27T08:10:00Z",
        }),
      ],
      fileChanges: [
        buildFileChange({
          id: "fc-1",
          filePath: "linked-file.ts",
          toolExecutionId: "tool-1",
        }),
      ],
    });
    render(<SessionTimeline session={session} />);

    // Both events render
    expect(screen.getByText("linked-file.ts")).toBeInTheDocument();
    expect(screen.getByText("Write")).toBeInTheDocument();
  });

  it("renders all three change type badges correctly", () => {
    const session = buildSession({
      fileChanges: [
        buildFileChange({ id: "fc-1", filePath: "a.ts", changeType: "Created" }),
        buildFileChange({ id: "fc-2", filePath: "b.ts", changeType: "Modified" }),
        buildFileChange({ id: "fc-3", filePath: "c.ts", changeType: "Deleted" }),
      ],
    });
    render(<SessionTimeline session={session} />);
    expect(screen.getByText("Created")).toBeInTheDocument();
    expect(screen.getByText("Modified")).toBeInTheDocument();
    expect(screen.getByText("Deleted")).toBeInTheDocument();
  });
});
