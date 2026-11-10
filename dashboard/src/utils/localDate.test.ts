import { describe, it, expect, afterEach } from "vitest";
import { toLocalIsoDate, lastDaysRange } from "./localDate";

// The dashboard has no Node typings; Node re-reads TZ when it changes, so Date follows it.
const env = (globalThis as unknown as { process: { env: Record<string, string | undefined> } }).process.env;

describe("localDate", () => {
  const originalTz = env.TZ;
  afterEach(() => {
    // Assigning undefined would store the string "undefined" as the zone.
    if (originalTz === undefined) delete env.TZ;
    else env.TZ = originalTz;
  });

  it("should give the viewer's local date in the evening, not the UTC date that has already rolled over", () => {
    env.TZ = "America/New_York";
    // 22:30 on 9 November in New York is already 10 November in UTC.
    expect(toLocalIsoDate(new Date("2026-11-10T03:30:00Z"))).toBe("2026-11-09");
  });

  it("should zero-pad month and day so the server parses YYYY-MM-DD", () => {
    expect(toLocalIsoDate(new Date(2026, 0, 5, 12))).toBe("2026-01-05");
  });

  it("should span the last N local days, today included", () => {
    expect(lastDaysRange(new Date(2026, 10, 3, 9), 7)).toEqual({ from: "2026-10-28", to: "2026-11-03" });
  });
});
