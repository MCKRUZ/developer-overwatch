import { describe, it, expect, vi, beforeEach } from "vitest";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import { act, render, screen, within } from "../../test/render";
import { SESSION_REFRESH_THROTTLE_MS } from "../../hooks/useLiveRefresh";
import SessionInsights from "./SessionInsights";
import { api } from "../../api";
import {
  buildEmptySessionContext,
  buildPromptCost,
  buildSessionContext,
  buildSubagentUsage,
} from "../../test/sessionInsightFactories";

vi.mock("../../api", () => ({
  api: {
    sessions: {
      context: vi.fn(),
      prompts: vi.fn(),
      subagentBreakdown: vi.fn(),
    },
  },
}));

const { hub } = vi.hoisted(() => {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const connection = {
    on: (event: string, handler: (...args: unknown[]) => void) => handlers.set(event, handler),
    off: (event: string) => handlers.delete(event),
  };
  const emit = (event: string, ...args: unknown[]) => handlers.get(event)?.(...args);
  return { hub: { connection, emit } };
});

vi.mock("../../hooks/useSignalR", () => ({
  useSignalR: () => ({
    connection: hub.connection as unknown as HubConnection,
    state: HubConnectionState.Connected,
    connectedEpoch: 0,
  }),
}));

const mockApi = vi.mocked(api);

const NOT_AVAILABLE = "Not available for this Claude Code version";

function region(name: string): HTMLElement {
  return screen.getByRole("region", { name });
}

describe("SessionInsights", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockApi.sessions.context.mockResolvedValue(buildSessionContext());
    mockApi.sessions.prompts.mockResolvedValue([buildPromptCost({ label: "Fix the login bug" })]);
    mockApi.sessions.subagentBreakdown.mockResolvedValue([buildSubagentUsage({ subagentName: "code-reviewer" })]);
  });

  it("should reload all three reads when this session's usage changes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      render(<SessionInsights sessionId="sess-9" />);
      expect(await screen.findByText("Fix the login bug")).toBeInTheDocument();
      mockApi.sessions.prompts.mockResolvedValue([buildPromptCost({ label: "Now the tests" })]);

      await act(async () => {
        hub.emit("UsageUpdated", { sessionId: "other", at: "t" });
        hub.emit("UsageUpdated", { sessionId: "sess-9", at: "t" });
        await vi.advanceTimersByTimeAsync(SESSION_REFRESH_THROTTLE_MS);
      });

      expect(await screen.findByText("Now the tests")).toBeInTheDocument();
      expect(mockApi.sessions.context).toHaveBeenCalledTimes(2);
      expect(mockApi.sessions.subagentBreakdown).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it("should load all three reads for this session", async () => {
    render(<SessionInsights sessionId="sess-9" />);
    expect(await screen.findByText("Fix the login bug")).toBeInTheDocument();
    expect(mockApi.sessions.context).toHaveBeenCalledWith("sess-9");
    expect(mockApi.sessions.prompts).toHaveBeenCalledWith("sess-9");
    expect(mockApi.sessions.subagentBreakdown).toHaveBeenCalledWith("sess-9");
  });

  it("should lay the sections out in order: context, breakout, watch list, prompts, subagents", async () => {
    render(<SessionInsights sessionId="sess-9" />);
    await screen.findByText("code-reviewer");
    expect(screen.getAllByRole("region").map((r) => r.querySelector("h3")?.textContent)).toEqual([
      "Context",
      "Context Window Breakout",
      "Watch List",
      "Cost per prompt",
      "Subagent split",
    ]);
  });

  it("should show each section's own loading text while its read is in flight", () => {
    mockApi.sessions.context.mockReturnValue(new Promise(() => {}));
    mockApi.sessions.prompts.mockReturnValue(new Promise(() => {}));
    mockApi.sessions.subagentBreakdown.mockReturnValue(new Promise(() => {}));
    render(<SessionInsights sessionId="sess-9" />);
    expect(within(region("Context")).getByText("Loading context...")).toBeInTheDocument();
    expect(within(region("Context Window Breakout")).getByText("Loading context...")).toBeInTheDocument();
    expect(within(region("Watch List")).getByText("Loading context...")).toBeInTheDocument();
    expect(within(region("Cost per prompt")).getByText("Loading prompts...")).toBeInTheDocument();
    expect(within(region("Subagent split")).getByText("Loading subagent split...")).toBeInTheDocument();
  });

  it("should keep the prompt and subagent tables when the context read fails", async () => {
    mockApi.sessions.context.mockRejectedValue(new Error("500: boom"));
    render(<SessionInsights sessionId="sess-9" />);
    expect(await within(region("Context")).findByText("Context unavailable: 500: boom")).toBeInTheDocument();
    expect(within(region("Context Window Breakout")).getByText("Context unavailable: 500: boom")).toBeInTheDocument();
    expect(within(region("Watch List")).getByText("Context unavailable: 500: boom")).toBeInTheDocument();
    expect(await screen.findByText("Fix the login bug")).toBeInTheDocument();
    expect(screen.getByText("code-reviewer")).toBeInTheDocument();
  });

  it("should fail the prompt and subagent sections on their own", async () => {
    mockApi.sessions.prompts.mockRejectedValue(new Error("503: down"));
    mockApi.sessions.subagentBreakdown.mockRejectedValue(new Error("504: slow"));
    render(<SessionInsights sessionId="sess-9" />);
    expect(await within(region("Cost per prompt")).findByText("Prompts unavailable: 503: down")).toBeInTheDocument();
    expect(await within(region("Subagent split")).findByText("Subagent split unavailable: 504: slow")).toBeInTheDocument();
    expect(within(region("Context")).getByText("Context: 148K / 200K (74%)")).toBeInTheDocument();
  });

  it("should say not available for both estimates when the server omits them, and still show tool errors", async () => {
    // WhenWritingNull drops null members, so an old agent's session arrives with neither key.
    const context = buildEmptySessionContext({ toolErrorCount: 2, lastToolErrorName: "Bash" });
    expect("breakout" in context).toBe(false);
    expect("unusedTools" in context).toBe(false);
    mockApi.sessions.context.mockResolvedValue(context);
    render(<SessionInsights sessionId="sess-9" />);
    expect(await within(region("Context Window Breakout")).findByText(NOT_AVAILABLE)).toBeInTheDocument();
    expect(within(region("Watch List")).getByText(NOT_AVAILABLE)).toBeInTheDocument();
    expect(within(region("Watch List")).getByText("2 tool errors · last: Bash failed")).toBeInTheDocument();
    expect(within(region("Context")).getByText("Context: not measured yet")).toBeInTheDocument();
  });
});
