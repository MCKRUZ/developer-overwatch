import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act } from "@testing-library/react";
import { render, screen, within } from "../../test/render";
import BudgetStatusTable from "./BudgetStatusTable";
import UsageWindowCards from "./UsageWindowCards";
import PlanValueTable from "./PlanValueTable";
import MeterSections from "./MeterSections";
import type { MeterData } from "../../hooks/useMeterData";
import {
  buildBudget,
  buildBudgetStatus,
  buildDeveloperWindow,
  buildPlanValue,
  buildUsageWindow,
} from "../../test/meterFactories";

/** A local wall-clock instant as the UTC ISO string the API sends. */
function localIso(hour: number, minute: number, day = 13): string {
  return new Date(2026, 10, day, hour, minute).toISOString();
}

const NOW = new Date(2026, 10, 13, 13, 13);

describe("BudgetStatusTable", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(NOW);
  });
  afterEach(() => vi.useRealTimers());

  function rowFor(who: string): HTMLElement {
    return screen.getByRole("cell", { name: who }).closest("tr")!;
  }

  it("should show who, period, amount, spent, what is left, percent, pace and forecast for each budget", () => {
    render(
      <BudgetStatusTable
        statuses={[
          buildBudgetStatus({
            spentUsd: 44.2,
            remainingUsd: 5.8,
            usedFraction: 0.884,
            level: "Amber",
            pace: { usedFraction: 0.42, elapsedFraction: 0.25, status: "Ahead" },
            forecast: { kind: "RunsOutWithinPeriod", runsOutAt: localIso(15, 10) },
          }),
        ]}
      />,
    );

    const row = within(rowFor("Team"));
    expect(row.getByText("Daily")).toBeInTheDocument();
    expect(row.getByText("$50.00")).toBeInTheDocument();
    expect(row.getByText("$44.20")).toBeInTheDocument();
    expect(row.getByText("$5.80 left")).toBeInTheDocument();
    expect(row.getByText("88%")).toBeInTheDocument();
    expect(row.getByText("ahead of pace: 42% used at 25% of the day")).toBeInTheDocument();
    expect(row.getByText("runs out about 15:10")).toBeInTheDocument();
  });

  it("should word a monthly developer budget's pace against the month and say how far over it is", () => {
    render(
      <BudgetStatusTable
        statuses={[
          buildBudgetStatus({
            budget: buildBudget({ scope: "Developer", developerEmail: "bob@x.io", period: "Monthly", amountUsd: 300 }),
            spentUsd: 301.2,
            remainingUsd: -1.2,
            usedFraction: 1.004,
            level: "Red",
            pace: { usedFraction: 1.004, elapsedFraction: 0.4, status: "Ahead" },
            forecast: { kind: "AlreadyExceeded", runsOutAt: null },
          }),
        ]}
      />,
    );

    const row = within(rowFor("bob@x.io"));
    expect(row.getByText("Monthly")).toBeInTheDocument();
    expect(row.getByText("over by $1.20")).toBeInTheDocument();
    expect(row.getByText("ahead of pace: 100% used at 40% of the month")).toBeInTheDocument();
    expect(row.getByText("already over")).toBeInTheDocument();
  });

  it.each([
    ["Ok", 0.7499, "Within limit", "bg-red-50"],
    ["Amber", 0.75, "Nearing limit", "bg-amber-50"],
    ["Red", 1, "Limit reached", "bg-red-50"],
  ] as const)(
    "should mark a %s row at %s used with both a colour and a text label, so the alert does not rely on colour alone",
    (level, usedFraction, label, colour) => {
      render(<BudgetStatusTable statuses={[buildBudgetStatus({ level, usedFraction })]} />);

      const row = rowFor("Team");
      expect(within(row).getByText(label)).toBeInTheDocument();
      if (level === "Ok") {
        expect(row.className).not.toContain("bg-amber-50");
        expect(row.className).not.toContain(colour);
      } else {
        expect(row.className).toContain(colour);
      }
    },
  );
});

describe("UsageWindowCards", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });
  afterEach(() => vi.useRealTimers());

  function cardFor(email: string): HTMLElement {
    return screen.getByRole("listitem", { name: `5-hour window for ${email}` });
  }

  it("should show percent used against the plan limit, with level, pace, forecast and an estimated reset", () => {
    render(
      <UsageWindowCards
        windows={[
          buildDeveloperWindow({
            window: buildUsageWindow({
              resetsAtUtc: localIso(15, 0),
              usedFraction: 0.42,
              spentUsd: 16.8,
              level: "Ok",
              pace: { usedFraction: 0.42, elapsedFraction: 0.64, status: "Behind" },
              forecast: { kind: "LastsPastPeriodEnd", runsOutAt: null },
            }),
          }),
        ]}
      />,
    );

    const card = within(cardFor("alice@x.io"));
    expect(card.getByText("Max 20x")).toBeInTheDocument();
    expect(card.getByText("42% used")).toBeInTheDocument();
    expect(card.getByText("$16.80 of $40.00")).toBeInTheDocument();
    expect(card.getByText("Within limit")).toBeInTheDocument();
    expect(card.getByText("behind pace: 42% used at 64% of the window")).toBeInTheDocument();
    expect(card.getByText("lasts the period")).toBeInTheDocument();
    expect(card.getByText("resets in about 1h 47m (15:00), estimate")).toBeInTheDocument();
  });

  it.each([
    ["Amber", "Nearing limit", "border-amber-400"],
    ["Red", "Limit reached", "border-red-500"],
  ] as const)("should flag a %s window in words as well as colour", (level, label, colour) => {
    render(<UsageWindowCards windows={[buildDeveloperWindow({ window: buildUsageWindow({ level }) })]} />);

    expect(within(cardFor("alice@x.io")).getByText(label)).toBeInTheDocument();
    expect(cardFor("alice@x.io").className).toContain(colour);
  });

  it("should offer to set a plan limit when the developer has none, linking to settings", () => {
    render(
      <UsageWindowCards
        windows={[
          buildDeveloperWindow({
            developerEmail: "bob@x.io",
            planName: null,
            windowLimitUsd: null,
            window: buildUsageWindow({ spentUsd: 3.5, usedFraction: null, level: null, pace: null, forecast: null }),
          }),
        ]}
      />,
    );

    const card = within(cardFor("bob@x.io"));
    expect(card.getByText("No plan")).toBeInTheDocument();
    expect(card.getByText("$3.50 so far")).toBeInTheDocument();
    expect(card.getByRole("link", { name: "Set a plan limit" })).toHaveAttribute("href", "/settings");
    expect(card.queryByText(/% used/)).not.toBeInTheDocument();
  });

  it("should say there is no usage when the developer has a plan but no open window", () => {
    render(<UsageWindowCards windows={[buildDeveloperWindow({ window: null })]} />);

    expect(within(cardFor("alice@x.io")).getByText("No usage in the current window")).toBeInTheDocument();
  });

  it("should tick the reset countdown down each minute without refetching", () => {
    render(<UsageWindowCards windows={[buildDeveloperWindow({ window: buildUsageWindow({ resetsAtUtc: localIso(15, 0) }) })]} />);
    expect(screen.getByText("resets in about 1h 47m (15:00), estimate")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    expect(screen.getByText("resets in about 1h 46m (15:00), estimate")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(45 * 60_000);
    });
    expect(screen.getByText("resets in about 1h 1m (15:00), estimate")).toBeInTheDocument();
  });

  it("should treat a window whose reset has passed as closed, rather than counting below zero", () => {
    render(<UsageWindowCards windows={[buildDeveloperWindow({ window: buildUsageWindow({ resetsAtUtc: localIso(13, 14) }) })]} />);
    expect(screen.getByText("resets in about 1m (13:14), estimate")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(60_000);
    });

    expect(screen.getByText("No usage in the current window")).toBeInTheDocument();
    expect(screen.queryByText(/resets in/)).not.toBeInTheDocument();
  });

  it("should stop ticking once unmounted", () => {
    const { unmount } = render(<UsageWindowCards windows={[buildDeveloperWindow()]} />);
    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe("PlanValueTable", () => {
  it("should show the plan, its price, the API value month to date and the multiple", () => {
    render(<PlanValueTable values={[buildPlanValue()]} />);

    const row = within(screen.getByRole("cell", { name: "alice@x.io" }).closest("tr")!);
    expect(row.getByText("Max 20x")).toBeInTheDocument();
    expect(row.getByText("$200.00")).toBeInTheDocument();
    expect(row.getByText("$1140.00")).toBeInTheDocument();
    expect(row.getByText("5.7x")).toBeInTheDocument();
  });
});

describe("MeterSections", () => {
  const loaded: MeterData = {
    budgets: [buildBudgetStatus()],
    budgetsError: null,
    windows: [buildDeveloperWindow()],
    windowsError: null,
    planValues: [buildPlanValue()],
    planValuesError: null,
  };

  it("should label every section's dollar figures as API-equivalent estimates", () => {
    render(<MeterSections meter={loaded} pricesAsOf="2026-11-01" />);

    for (const name of ["Budgets", "5-hour windows (estimate)", "Plan vs API value"]) {
      const section = within(screen.getByRole("region", { name }));
      expect(section.getByText("API-equivalent estimate (prices as of 2026-11-01)")).toBeInTheDocument();
    }
  });

  it("should show loading text for sections still loading", () => {
    render(
      <MeterSections meter={{ ...loaded, budgets: null, windows: null, planValues: null }} pricesAsOf="2026-11-01" />,
    );

    expect(screen.getByText("Loading budgets...")).toBeInTheDocument();
    expect(screen.getByText("Loading 5-hour windows...")).toBeInTheDocument();
    expect(screen.getByText("Loading plan value...")).toBeInTheDocument();
  });

  it("should show each section's own error and keep the others", () => {
    render(
      <MeterSections
        meter={{ ...loaded, budgetsError: "503: busy", windowsError: "500: boom", planValuesError: "502: bad" }}
        pricesAsOf="2026-11-01"
      />,
    );

    expect(screen.getByText("Budgets unavailable: 503: busy")).toBeInTheDocument();
    expect(screen.getByText("5-hour windows unavailable: 500: boom")).toBeInTheDocument();
    expect(screen.getByText("Plan value unavailable: 502: bad")).toBeInTheDocument();
  });

  it("should point to settings when there are no budgets or plans yet", () => {
    render(<MeterSections meter={{ ...loaded, budgets: [], windows: [], planValues: [] }} pricesAsOf="2026-11-01" />);

    expect(screen.getByText("No budgets yet.")).toBeInTheDocument();
    expect(screen.getByText("No usage in the last 5 hours and no developer plans.")).toBeInTheDocument();
    expect(screen.getByText("No developer plans yet.")).toBeInTheDocument();
    const links = screen.getAllByRole("link", { name: "Set them up in Settings" });
    expect(links).toHaveLength(2);
    for (const link of links) expect(link).toHaveAttribute("href", "/settings");
  });
});
