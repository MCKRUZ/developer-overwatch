import type { Budget, BudgetLevel, BudgetPeriod, Forecast, ForecastKind, Pace, PaceStatus } from "../types";
import { toLocalIsoDate } from "./localDate";
import { formatCost } from "./tokenFormat";
import { formatMinutes } from "./usageFormat";

export type PaceUnit = "day" | "month" | "window";

const PACE_WORDS: Record<PaceStatus, string> = {
  Ahead: "ahead of pace",
  OnPace: "on pace",
  Behind: "behind pace",
};

const FIXED_FORECASTS: Record<Exclude<ForecastKind, "RunsOutWithinPeriod">, string> = {
  LastsPastPeriodEnd: "lasts the period",
  AlreadyExceeded: "already over",
  NoSpendYet: "no spend yet",
  NoRateYet: "not enough activity to forecast yet",
};

const LEVEL_LABELS: Record<BudgetLevel, string> = {
  Ok: "Within limit",
  Amber: "Nearing limit",
  Red: "Limit reached",
};

function pad(n: number): string {
  return String(n).padStart(2, "0");
}

export function formatUsedPercent(fraction: number): string {
  return `${Math.round(fraction * 100)}%`;
}

/** "$5.80 left", or "over by $1.20" once spend passes the amount. */
export function remainingText(remainingUsd: number): string {
  return remainingUsd < 0 ? `over by ${formatCost(-remainingUsd)}` : `${formatCost(remainingUsd)} left`;
}

export function periodUnit(period: BudgetPeriod): PaceUnit {
  return period === "Daily" ? "day" : "month";
}

/** "ahead of pace: 42% used at 25% of the day". */
export function paceText(pace: Pace, unit: PaceUnit): string {
  return `${PACE_WORDS[pace.status]}: ${formatUsedPercent(pace.usedFraction)} used at ${formatUsedPercent(pace.elapsedFraction)} of the ${unit}`;
}

/** The viewer's local wall-clock time of a UTC instant, as 24-hour HH:mm. */
export function formatLocalTime(iso: string): string {
  const at = new Date(iso);
  return `${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

/** A run-out later today reads as a clock time, a later day as a local date. */
export function forecastText(forecast: Forecast, now: Date): string {
  if (forecast.kind !== "RunsOutWithinPeriod") return FIXED_FORECASTS[forecast.kind];
  if (!forecast.runsOutAt) return "runs out within the period";
  const runsOut = new Date(forecast.runsOutAt);
  return toLocalIsoDate(runsOut) === toLocalIsoDate(now)
    ? `runs out about ${formatLocalTime(forecast.runsOutAt)}`
    : `runs out on ${toLocalIsoDate(runsOut)}`;
}

export function levelLabel(level: BudgetLevel): string {
  return LEVEL_LABELS[level];
}

/** "resets in about 1h 47m (15:00), estimate". A part minute rounds up, so an open window never reads 0m. */
export function resetText(resetsAtUtc: string, now: Date): string {
  const minutes = Math.ceil((new Date(resetsAtUtc).getTime() - now.getTime()) / 60_000);
  return `resets in about ${formatMinutes(minutes)} (${formatLocalTime(resetsAtUtc)}), estimate`;
}

export function formatMultiple(multiple: number): string {
  return `${multiple.toFixed(1)}x`;
}

/** A Team budget has no developer email. */
export function budgetWho(budget: Pick<Budget, "scope" | "developerEmail">): string {
  return budget.developerEmail ?? "Team";
}
