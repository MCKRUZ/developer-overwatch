import type { RunningSession } from "../types";

/** A 0..1 rate as a whole percent; "—" when absent (no input means no hit rate, not 0%). */
export function formatPercent(rate: number | undefined): string {
  return rate != null ? `${Math.round(rate * 100)}%` : "—";
}

/** Dollars per minute. Sub-cent burn keeps four decimals so a live session never reads $0.00. */
export function formatPerMinuteUsd(usd: number): string {
  return usd > 0 && usd < 0.01 ? `$${usd.toFixed(4)}` : `$${usd.toFixed(2)}`;
}

export function formatMinutes(totalMinutes: number): string {
  if (totalMinutes < 60) return `${totalMinutes}m`;
  return `${Math.floor(totalMinutes / 60)}h ${totalMinutes % 60}m`;
}

/** Duration between two ISO instants, to the whole minute. */
export function formatSpan(firstAt: string, lastAt: string): string {
  return formatMinutes(Math.floor((new Date(lastAt).getTime() - new Date(firstAt).getTime()) / 60_000));
}

/** Rule 21's wording: active, waiting, or idle with how long. */
export function runningStateLabel(session: Pick<RunningSession, "activityState" | "idleForSeconds">): string {
  switch (session.activityState) {
    case "Active":
      return "active";
    case "Waiting":
      return "waiting";
    case "Idle":
      return session.idleForSeconds != null
        ? `idle ${formatMinutes(Math.floor(session.idleForSeconds / 60))}`
        : "idle";
    default:
      return "unknown";
  }
}
