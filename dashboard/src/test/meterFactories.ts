import type {
  Budget,
  BudgetStatus,
  CurrentUser,
  DeveloperPlan,
  DeveloperWindow,
  PlanValue,
  UsageWindow,
} from "../types";

export function buildBudget(overrides?: Partial<Budget>): Budget {
  return {
    id: "b-team-daily",
    scope: "Team",
    developerEmail: null,
    period: "Daily",
    amountUsd: 50,
    createdAt: "2026-11-01T00:00:00Z",
    updatedAt: "2026-11-01T00:00:00Z",
    ...overrides,
  };
}

export function buildBudgetStatus(overrides?: Partial<BudgetStatus>): BudgetStatus {
  return {
    budget: buildBudget(),
    periodStartUtc: "2026-11-13T05:00:00Z",
    periodEndUtc: "2026-11-14T05:00:00Z",
    spentUsd: 10,
    remainingUsd: 40,
    usedFraction: 0.2,
    level: "Ok",
    pace: { usedFraction: 0.2, elapsedFraction: 0.4, status: "Behind" },
    forecast: { kind: "LastsPastPeriodEnd", runsOutAt: null },
    ...overrides,
  };
}

export function buildDeveloperPlan(overrides?: Partial<DeveloperPlan>): DeveloperPlan {
  return {
    id: "p-alice",
    developerEmail: "alice@x.io",
    planName: "Max 20x",
    monthlyPriceUsd: 200,
    windowLimitUsd: 40,
    createdAt: "2026-11-01T00:00:00Z",
    updatedAt: "2026-11-01T00:00:00Z",
    ...overrides,
  };
}

/** Local 10:00 to 15:00 on 2026-11-13, whatever the machine's time zone, so a test clock set in local time sees it open. */
export function buildUsageWindow(overrides?: Partial<UsageWindow>): UsageWindow {
  return {
    startUtc: new Date(2026, 10, 13, 10, 0).toISOString(),
    resetsAtUtc: new Date(2026, 10, 13, 15, 0).toISOString(),
    spentUsd: 16.8,
    tokens: 1_250_000,
    usedFraction: 0.42,
    level: "Ok",
    pace: { usedFraction: 0.42, elapsedFraction: 0.5, status: "Behind" },
    forecast: { kind: "LastsPastPeriodEnd", runsOutAt: null },
    ...overrides,
  };
}

export function buildDeveloperWindow(overrides?: Partial<DeveloperWindow>): DeveloperWindow {
  return {
    developerEmail: "alice@x.io",
    planName: "Max 20x",
    windowLimitUsd: 40,
    window: buildUsageWindow(),
    ...overrides,
  };
}

export function buildPlanValue(overrides?: Partial<PlanValue>): PlanValue {
  return {
    developerEmail: "alice@x.io",
    planName: "Max 20x",
    monthlyPriceUsd: 200,
    monthStartUtc: "2026-11-01T04:00:00Z",
    apiValueUsd: 1140,
    valueMultiple: 5.7,
    ...overrides,
  };
}

export function buildCurrentUser(overrides?: Partial<CurrentUser>): CurrentUser {
  return {
    id: "u1",
    name: "Lead",
    email: "lead@x.io",
    roles: ["Lead"],
    authMethod: "Bearer",
    canEditBudgets: true,
    ...overrides,
  };
}
