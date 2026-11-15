import { describe, it, expect } from "vitest";
import { render, screen, within } from "../../test/render";
import PromptCostTable from "./PromptCostTable";
import SubagentSplitTable from "./SubagentSplitTable";
import { buildPromptCost, buildSubagentUsage } from "../../test/sessionInsightFactories";

function bodyRows(): HTMLElement[] {
  const [, ...rows] = screen.getAllByRole("row");
  return rows;
}

describe("PromptCostTable", () => {
  it("should show number, label, tokens, cost and subagent share for each prompt", () => {
    render(
      <PromptCostTable
        prompts={[
          buildPromptCost({ index: 1, label: "Fix the login bug", totalTokens: 12_345, estimatedCostUsd: 2, subagentCostUsd: 0.5 }),
        ]}
      />,
    );
    const cells = within(bodyRows()[0]!).getAllByRole("cell").map((c) => c.textContent);
    expect(cells).toEqual(["1", "Fix the login bug", "12,345", "$2.00", "25%"]);
  });

  it("should say no text was captured when a prompt has no label", () => {
    render(<PromptCostTable prompts={[buildPromptCost({ index: 3, label: undefined })]} />);
    expect(screen.getByText("(no text captured)")).toBeInTheDocument();
  });

  it("should label the turns before the first prompt as such rather than as a prompt", () => {
    render(<PromptCostTable prompts={[buildPromptCost({ index: 0, label: null })]} />);
    expect(screen.getByText("(before the first prompt)")).toBeInTheDocument();
    expect(within(bodyRows()[0]!).getAllByRole("cell")[0]).toHaveTextContent("—");
  });

  it("should show no subagent share when a prompt cost nothing", () => {
    render(<PromptCostTable prompts={[buildPromptCost({ estimatedCostUsd: 0, subagentCostUsd: 0 })]} />);
    expect(within(bodyRows()[0]!).getAllByRole("cell")[4]).toHaveTextContent("—");
  });

  it("should mark the three most expensive prompts by rank, keeping the table in prompt order", () => {
    render(
      <PromptCostTable
        prompts={[
          buildPromptCost({ index: 1, label: "a", estimatedCostUsd: 0.1 }),
          buildPromptCost({ index: 2, label: "b", estimatedCostUsd: 4 }),
          buildPromptCost({ index: 3, label: "c", estimatedCostUsd: 1 }),
          buildPromptCost({ index: 4, label: "d", estimatedCostUsd: 9 }),
          buildPromptCost({ index: 5, label: "e", estimatedCostUsd: 0.5 }),
        ]}
      />,
    );
    const rows = bodyRows();
    expect(rows.map((r) => within(r).getAllByRole("cell")[0]!.textContent)).toEqual(["1", "2", "3", "4", "5"]);
    expect(within(rows[3]!).getByText("#1 cost")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("#2 cost")).toBeInTheDocument();
    expect(within(rows[2]!).getByText("#3 cost")).toBeInTheDocument();
    expect(screen.getAllByText(/^#\d cost$/)).toHaveLength(3);
  });

  it("should break ties by prompt order", () => {
    render(
      <PromptCostTable
        prompts={[1, 1, 0.2, 0.2, 1].map((cost, i) => buildPromptCost({ index: i + 1, estimatedCostUsd: cost }))}
      />,
    );
    const rows = bodyRows();
    expect(within(rows[0]!).getByText("#1 cost")).toBeInTheDocument();
    expect(within(rows[1]!).getByText("#2 cost")).toBeInTheDocument();
    expect(within(rows[4]!).getByText("#3 cost")).toBeInTheDocument();
    expect(screen.getAllByText(/^#\d cost$/)).toHaveLength(3);
  });

  it("should not mark prompts that cost nothing", () => {
    render(
      <PromptCostTable
        prompts={[2, 0, 0, 0].map((cost, i) => buildPromptCost({ index: i + 1, estimatedCostUsd: cost }))}
      />,
    );
    expect(within(bodyRows()[0]!).getByText("#1 cost")).toBeInTheDocument();
    expect(screen.getAllByText(/^#\d cost$/)).toHaveLength(1);
  });

  it("should mark nothing when there are three prompts or fewer, since every one would be a top 3", () => {
    render(
      <PromptCostTable
        prompts={[3, 1, 2].map((cost, i) => buildPromptCost({ index: i + 1, estimatedCostUsd: cost }))}
      />,
    );
    expect(screen.queryByText(/^#\d cost$/)).not.toBeInTheDocument();
  });

  it("should say so when the session has no prompts", () => {
    render(<PromptCostTable prompts={[]} />);
    expect(screen.getByText("No prompts recorded.")).toBeInTheDocument();
  });
});

describe("SubagentSplitTable", () => {
  it("should name the main thread and give each row its share of the session cost", () => {
    render(
      <SubagentSplitTable
        rows={[
          buildSubagentUsage({ subagentName: undefined, totalTokens: 30_000, estimatedCostUsd: 3 }),
          buildSubagentUsage({ subagentName: "code-reviewer", totalTokens: 10_000, estimatedCostUsd: 1 }),
        ]}
      />,
    );
    const rows = bodyRows();
    expect(within(rows[0]!).getAllByRole("cell").map((c) => c.textContent)).toEqual([
      "main thread",
      "30,000",
      "$3.00",
      "75%",
    ]);
    expect(within(rows[1]!).getAllByRole("cell").map((c) => c.textContent)).toEqual([
      "code-reviewer",
      "10,000",
      "$1.00",
      "25%",
    ]);
  });

  it("should show no share when the session cost nothing", () => {
    render(<SubagentSplitTable rows={[buildSubagentUsage({ estimatedCostUsd: 0 })]} />);
    expect(within(bodyRows()[0]!).getAllByRole("cell")[3]).toHaveTextContent("—");
  });

  it("should say so when the session has no usage", () => {
    render(<SubagentSplitTable rows={[]} />);
    expect(screen.getByText("No usage recorded.")).toBeInTheDocument();
  });
});
