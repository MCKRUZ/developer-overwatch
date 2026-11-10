import { useEffect, useState } from "react";

/** How often the page checks whether the local date has changed. */
export const LOCAL_DATE_CHECK_MS = 60_000;

function startOfLocalDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

/**
 * Today's local date at midnight. The value changes identity only when the local date changes,
 * so ranges derived from it recompute once a day, within `LOCAL_DATE_CHECK_MS` of midnight.
 */
export function useLocalToday(): Date {
  const [today, setToday] = useState(() => startOfLocalDay(new Date()));

  useEffect(() => {
    const timer = setInterval(() => {
      const now = startOfLocalDay(new Date());
      setToday((prev) => (prev.getTime() === now.getTime() ? prev : now));
    }, LOCAL_DATE_CHECK_MS);
    return () => clearInterval(timer);
  }, []);

  return today;
}
