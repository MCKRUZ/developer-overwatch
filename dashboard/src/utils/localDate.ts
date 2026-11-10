function pad(n: number): string {
  return String(n).padStart(2, "0");
}

/** The date on the viewer's own calendar. `toISOString()` would give the UTC date, a day ahead every evening west of UTC. */
export function toLocalIsoDate(date: Date): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** The last `days` local days, today included, as inclusive YYYY-MM-DD dates. */
export function lastDaysRange(now: Date, days: number): { from: string; to: string } {
  const start = new Date(now.getFullYear(), now.getMonth(), now.getDate() - (days - 1));
  return { from: toLocalIsoDate(start), to: toLocalIsoDate(now) };
}
