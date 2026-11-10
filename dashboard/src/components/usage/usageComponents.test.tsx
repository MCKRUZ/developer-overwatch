import { describe, it, expect, vi } from "vitest";
import { fireEvent } from "@testing-library/react";
import { render, screen, within, userEvent } from "../../test/render";
import PeriodPicker from "./PeriodPicker";
import UsageTiles from "./UsageTiles";
import UsageBucketsTable from "./UsageBucketsTable";
import UsageSplitTabs from "./UsageSplitTabs";
import RunningSessionsList from "./RunningSessionsList";
import {
  buildRunningSession,
  buildUsageBucket,
  buildUsageSplitRow,
  buildUsageSplits,
  buildUsageTotals,
} from "../../test/usageFactories";

describe("PeriodPicker", () => {
  it("should report a granularity change with the range kept, so switching to weeks keeps the chosen dates", async () => {
    const onChange = vi.fn();
    render(<PeriodPicker granularity="day" from="2026-11-01" to="2026-11-30" onChange={onChange} />);

    await userEvent.setup().selectOptions(screen.getByLabelText("Granularity"), "week");

    expect(onChange).toHaveBeenCalledWith({ granularity: "week", from: "2026-11-01", to: "2026-11-30" });
  });

  it("should report each edited date as a local YYYY-MM-DD value", () => {
    const onChange = vi.fn();
    render(<PeriodPicker granularity="month" from="2026-11-01" to="2026-11-30" onChange={onChange} />);

    fireEvent.change(screen.getByLabelText("From"), { target: { value: "2026-10-01" } });
    fireEvent.change(screen.getByLabelText("To"), { target: { value: "2026-12-31" } });

    expect(onChange).toHaveBeenCalledWith({ granularity: "month", from: "2026-10-01", to: "2026-11-30" });
    expect(onChange).toHaveBeenCalledWith({ granularity: "month", from: "2026-11-01", to: "2026-12-31" });
  });
});

describe("UsageTiles", () => {
  it("should show every token kind apart, the cost, the hit rate and the burn, as rules 13-15, 25-27 require", () => {
    render(
      <UsageTiles
        totals={buildUsageTotals({
          inputTokens: 1204318,
          outputTokens: 84112,
          cacheReadInputTokens: 9840220,
          cacheCreationInputTokens: 1120400,
          estimatedCostUsd: 71.9,
          cacheHitRate: 0.87,
        })}
        burn={{ tokensPerMinute: 4200, costPerMinute: 0.21, lookbackMinutes: 15 }}
      />,
    );

    expect(screen.getByTestId("tile-input")).toHaveTextContent((1204318).toLocaleString());
    expect(screen.getByTestId("tile-output")).toHaveTextContent((84112).toLocaleString());
    expect(screen.getByTestId("tile-cache-read")).toHaveTextContent((9840220).toLocaleString());
    expect(screen.getByTestId("tile-cache-write")).toHaveTextContent((1120400).toLocaleString());
    expect(screen.getByTestId("tile-cost")).toHaveTextContent("$71.90");
    expect(screen.getByTestId("tile-hit-rate")).toHaveTextContent("87%");
    expect(screen.getByTestId("tile-burn")).toHaveTextContent(`${(4200).toLocaleString()} tokens/min`);
    expect(screen.getByTestId("tile-burn")).toHaveTextContent("$0.21/min");
    expect(screen.getByTestId("tile-burn")).toHaveTextContent("all token kinds, last 15 min");
  });

  it("should show a dash for the hit rate when the period had no input", () => {
    render(
      <UsageTiles
        totals={buildUsageTotals({ cacheHitRate: undefined })}
        burn={{ tokensPerMinute: 0, costPerMinute: 0, lookbackMinutes: 15 }}
      />,
    );
    expect(screen.getByTestId("tile-hit-rate")).toHaveTextContent("—");
  });
});

describe("UsageBucketsTable", () => {
  it("should render the shared columns so the table and the exports cannot drift apart", () => {
    render(<UsageBucketsTable buckets={[buildUsageBucket({ start: "2026-11-04", estimatedCostUsd: 18.4 })]} />);

    expect(screen.getByRole("columnheader", { name: "Est. cost (USD)" })).toBeInTheDocument();
    const row = screen.getByRole("row", { name: /2026-11-04/ });
    expect(within(row).getByText("$18.40")).toBeInTheDocument();
  });

  it("should say so when the period has no buckets", () => {
    render(<UsageBucketsTable buckets={[]} />);
    expect(screen.getByText("No usage in this period.")).toBeInTheDocument();
  });
});

describe("UsageSplitTabs", () => {
  const splits = buildUsageSplits({
    model: [
      buildUsageSplitRow({ key: "opus", label: "opus", estimatedCostUsd: 22.4, costShare: 0.61, totalTokens: 900 }),
      buildUsageSplitRow({ key: "sonnet", label: "sonnet", estimatedCostUsd: 13.6, costShare: 0.37 }),
    ],
    session: [
      buildUsageSplitRow({
        key: "7bb533f1-aaaa",
        label: "7bb533f1",
        firstAt: "2026-11-04T09:00:00Z",
        lastAt: "2026-11-04T11:14:00Z",
      }),
    ],
    subagent: [
      buildUsageSplitRow({ key: undefined, label: "main thread" }),
      buildUsageSplitRow({ key: "explore", label: "explore" }),
    ],
  });

  it("should open on the model split with tokens, cost and cost share per row", () => {
    render(<UsageSplitTabs splits={splits} />);

    const row = screen.getByRole("row", { name: /opus/ });
    expect(within(row).getByText("$22.40")).toBeInTheDocument();
    expect(within(row).getByText("61%")).toBeInTheDocument();
    expect(within(row).getByText("900")).toBeInTheDocument();
  });

  it("should show each session's duration from its first and last turn (rule 10)", async () => {
    render(<UsageSplitTabs splits={splits} />);

    await userEvent.setup().click(screen.getByRole("tab", { name: "Session" }));

    expect(screen.getByRole("columnheader", { name: "Duration" })).toBeInTheDocument();
    expect(within(screen.getByRole("row", { name: /7bb533f1/ })).getByText("2h 14m")).toBeInTheDocument();
  });

  it("should list the main thread beside subagents even though it has no key", async () => {
    render(<UsageSplitTabs splits={splits} />);

    await userEvent.setup().click(screen.getByRole("tab", { name: "Subagent" }));

    expect(screen.getByText("main thread")).toBeInTheDocument();
    expect(screen.getByText("explore")).toBeInTheDocument();
  });

  it("should offer every split the spec names, including branch, machine and agent", () => {
    render(<UsageSplitTabs splits={splits} />);
    const tabs = screen.getAllByRole("tab").map((t) => t.textContent);
    expect(tabs).toEqual(["Model", "Project", "Session", "Subagent", "Branch", "Developer", "Machine", "Agent"]);
  });

  it("should say so when a split has no rows", async () => {
    render(<UsageSplitTabs splits={splits} />);
    await userEvent.setup().click(screen.getByRole("tab", { name: "Branch" }));
    expect(screen.getByText("No usage in this period.")).toBeInTheDocument();
  });
});

describe("RunningSessionsList", () => {
  it("should show each running session's state and burn, so a lead sees who is active now", () => {
    render(
      <RunningSessionsList
        sessions={[
          buildRunningSession({ id: "a1", project: "overwatch", activityState: "Active" }),
          buildRunningSession({ id: "b2", project: "EPR", activityState: "Waiting", developerName: "Kevin" }),
          buildRunningSession({
            id: "c3",
            project: undefined,
            machineName: undefined,
            activityState: "Idle",
            idleForSeconds: 3120,
            currentContextTokens: undefined,
            burnTokensPerMinute: 0,
            burnCostPerMinute: 0,
          }),
        ]}
      />,
    );

    expect(screen.getByText("3 running")).toBeInTheDocument();
    expect(within(screen.getByRole("row", { name: /overwatch/ })).getByText("active")).toBeInTheDocument();
    const epr = screen.getByRole("row", { name: /EPR/ });
    expect(within(epr).getByText("waiting")).toBeInTheDocument();
    expect(within(epr).getByText("Kevin")).toBeInTheDocument();
    expect(within(epr).getByText(`${(4200).toLocaleString()} tokens/min ($0.21/min)`)).toBeInTheDocument();
    expect(screen.getByText("idle 52m")).toBeInTheDocument();
  });

  it("should say nothing is running rather than show an empty table", () => {
    render(<RunningSessionsList sessions={[]} />);
    expect(screen.getByText("No sessions running.")).toBeInTheDocument();
  });
});
