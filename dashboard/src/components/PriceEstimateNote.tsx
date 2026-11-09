import { useEffect, useState } from "react";
import { api } from "../api";

const LABEL = "API-equivalent estimate";

// One successful fetch per page load: every note on screen shares this promise.
// A failure (offline, or a 401 before the auth token is ready) is not cached, so the next mount retries.
let pricingAsOf: Promise<string | null> | null = null;

function loadPricingAsOf(): Promise<string | null> {
  pricingAsOf ??= api.pricing
    .get()
    .then((info) => info.asOf)
    .catch(() => {
      pricingAsOf = null;
      return null;
    });
  return pricingAsOf;
}

export function resetPriceEstimateCache(): void {
  pricingAsOf = null;
}

interface PriceEstimateNoteProps {
  className?: string;
}

export default function PriceEstimateNote({ className }: PriceEstimateNoteProps) {
  const [asOf, setAsOf] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    loadPricingAsOf().then((date) => {
      if (active) setAsOf(date);
    });
    return () => {
      active = false;
    };
  }, []);

  const classes = ["text-xs text-gray-500 dark:text-gray-400", className].filter(Boolean).join(" ");

  return <p className={classes}>{asOf ? `${LABEL} (prices as of ${asOf})` : LABEL}</p>;
}
