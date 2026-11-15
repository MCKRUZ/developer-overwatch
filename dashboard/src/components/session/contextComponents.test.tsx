import { describe, it, expect } from "vitest";
import { render, screen, within, userEvent } from "../../test/render";
import ContextPanel from "./ContextPanel";
import ContextBreakoutView from "./ContextBreakoutView";
import WatchList from "./WatchList";
import {
  buildContextBreakout,
  buildEmptySessionContext,
  buildSessionContext,
  buildUnusedToolGroup,
} from "../../test/sessionInsightFactories";

describe("ContextPanel", () => {
  it("should show current context against its window, the peak, and that both are estimates", () => {
    render(<ContextPanel context={buildSessionContext()} />);
    expect(screen.getByText("Context: 148K / 200K (74%)")).toBeInTheDocument();
    expect(screen.getByText("Peak: 190K (95%)")).toBeInTheDocument();
    expect(screen.getByText("estimate · windows as of 2026-11-01")).toBeInTheDocument();
  });

  it("should colour the fill bar green below 75% with no warning badge", () => {
    render(<ContextPanel context={buildSessionContext({ currentFill: 0.74 })} />);
    const bar = screen.getByRole("meter", { name: "Context fill" });
    expect(bar).toHaveAttribute("aria-valuenow", "74");
    expect(bar.firstElementChild).toHaveClass("bg-green-500");
    expect(screen.queryByText("Nearing limit")).not.toBeInTheDocument();
  });

  it("should turn amber at 75% and say so in words", () => {
    render(<ContextPanel context={buildSessionContext({ currentContextTokens: 150_000, currentFill: 0.75 })} />);
    expect(screen.getByRole("meter", { name: "Context fill" }).firstElementChild).toHaveClass("bg-amber-500");
    expect(screen.getByText("Nearing limit")).toBeInTheDocument();
  });

  it("should turn red at 90% and keep the bar inside its track when the fill passes 100%", () => {
    render(<ContextPanel context={buildSessionContext({ currentContextTokens: 208_000, currentFill: 1.04 })} />);
    expect(screen.getByText("Context: 208K / 200K (104%)")).toBeInTheDocument();
    const fill = screen.getByRole("meter", { name: "Context fill" }).firstElementChild as HTMLElement;
    expect(fill).toHaveClass("bg-red-500");
    expect(fill.style.width).toBe("100%");
    expect(screen.getByText("Limit reached")).toBeInTheDocument();
  });

  it("should count compactions and quote the newest one's size before it ran", () => {
    const breakout = buildContextBreakout({
      compactions: [
        { at: "2026-11-13T14:00:00Z", trigger: "auto", preTokens: 392_600, postTokens: 40_000 },
        { at: "2026-11-13T12:00:00Z", trigger: "manual", preTokens: 150_000, postTokens: 30_000 },
      ],
    });
    render(<ContextPanel context={buildSessionContext({ breakout })} />);
    expect(screen.getByText("Compacted 2× · last at 393K (auto)")).toBeInTheDocument();
  });

  it("should leave out the compaction line when the session never compacted", () => {
    render(<ContextPanel context={buildSessionContext()} />);
    expect(screen.queryByText(/Compacted/)).not.toBeInTheDocument();
  });

  it("should say nothing is measured yet when no main-thread turn carried context", () => {
    render(<ContextPanel context={buildEmptySessionContext()} />);
    expect(screen.getByText("Context: not measured yet")).toBeInTheDocument();
    expect(screen.queryByRole("meter")).not.toBeInTheDocument();
    expect(screen.queryByText(/Peak:/)).not.toBeInTheDocument();
  });

  it("should show bare token counts when the window size is unknown", () => {
    render(
      <ContextPanel
        context={buildSessionContext({ currentFill: null, peakFill: undefined })}
      />,
    );
    expect(screen.getByText("Context: 148K")).toBeInTheDocument();
    expect(screen.getByText("Peak: 190K")).toBeInTheDocument();
    expect(screen.queryByRole("meter")).not.toBeInTheDocument();
  });
});

describe("ContextBreakoutView", () => {
  it("should head the breakout with measured context against the window", () => {
    render(<ContextBreakoutView breakout={buildContextBreakout()} />);
    expect(screen.getByText("170K / 1.0M · 17%")).toBeInTheDocument();
  });

  it("should list each category by its friendly name in the server's order, conversation last", () => {
    render(<ContextBreakoutView breakout={buildContextBreakout()} />);
    const rows = screen.getAllByRole("listitem");
    expect(rows.map((r) => within(r).getAllByText(/./)[0]!.textContent)).toEqual([
      "System prompt",
      "MCP tools (github)",
      "Built-in tools",
      "Conversation",
    ]);
    expect(within(rows[1]!).getByText("30K")).toBeInTheDocument();
  });

  it("should size each bar by the category's share of the context", () => {
    render(<ContextBreakoutView breakout={buildContextBreakout()} />);
    const bar = screen.getByRole("meter", { name: "Conversation share" });
    expect((bar.firstElementChild as HTMLElement).style.width).toBe("47%");
  });

  it("should say which Claude Code request the estimate came from", () => {
    const breakout = buildContextBreakout();
    render(<ContextBreakoutView breakout={breakout} />);
    expect(
      screen.getByText(`Estimated from Claude Code 2.1.40 request at ${new Date(breakout.takenAt).toLocaleString()}`),
    ).toBeInTheDocument();
  });

  it("should say the breakout is not available when the agent sent no request shape", () => {
    render(<ContextBreakoutView breakout={undefined} />);
    expect(screen.getByText("Not available for this Claude Code version")).toBeInTheDocument();
  });
});

describe("WatchList", () => {
  it("should list unused groups with their per-request cost and a built-in or MCP tag", () => {
    render(
      <WatchList
        context={buildSessionContext({
          unusedTools: [
            buildUnusedToolGroup({ group: "github", isMcp: true, tokensPerRequest: 5_000 }),
            buildUnusedToolGroup({ group: "NotebookEdit", isMcp: false, toolCount: 1, tokensPerRequest: 800 }),
          ],
        })}
      />,
    );
    const items = screen.getAllByRole("listitem");
    expect(within(items[0]!).getByText("github: unused · 5K/request")).toBeInTheDocument();
    expect(within(items[0]!).getByText("MCP")).toBeInTheDocument();
    expect(within(items[1]!).getByText("NotebookEdit: unused · 800/request")).toBeInTheDocument();
    expect(within(items[1]!).getByText("built-in")).toBeInTheDocument();
  });

  it("should show the top 10 and expand the rest on request", async () => {
    const user = userEvent.setup();
    const unusedTools = Array.from({ length: 13 }, (_, i) =>
      buildUnusedToolGroup({ group: `server-${i + 1}`, tokensPerRequest: 20_000 - i * 1_000 }),
    );
    render(<WatchList context={buildSessionContext({ unusedTools })} />);
    expect(screen.getAllByRole("listitem")).toHaveLength(10);
    expect(screen.queryByText(/^server-11:/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "and 3 more" }));

    expect(screen.getAllByRole("listitem")).toHaveLength(13);
    expect(screen.getByText("server-13: unused · 8K/request")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /more/ })).not.toBeInTheDocument();
  });

  it("should say so when every loaded tool was used", () => {
    render(<WatchList context={buildSessionContext({ unusedTools: [] })} />);
    expect(screen.getByText("No unused tools")).toBeInTheDocument();
  });

  it("should count tool errors and name the last failing tool", () => {
    render(<WatchList context={buildSessionContext({ toolErrorCount: 2, lastToolErrorName: "Bash" })} />);
    expect(screen.getByText("2 tool errors · last: Bash failed")).toBeInTheDocument();
  });

  it("should use the singular for one error and drop the name when it is unknown", () => {
    render(<WatchList context={buildSessionContext({ toolErrorCount: 1, lastToolErrorName: undefined })} />);
    expect(screen.getByText("1 tool error")).toBeInTheDocument();
  });

  it("should say there were no tool errors", () => {
    render(<WatchList context={buildSessionContext({ toolErrorCount: 0, lastToolErrorName: null })} />);
    expect(screen.getByText("No tool errors")).toBeInTheDocument();
  });

  it("should still show tool errors when unused tools are not available", () => {
    render(<WatchList context={buildEmptySessionContext({ toolErrorCount: 2, lastToolErrorName: "Bash" })} />);
    expect(screen.getByText("Not available for this Claude Code version")).toBeInTheDocument();
    expect(screen.getByText("2 tool errors · last: Bash failed")).toBeInTheDocument();
  });
});
