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
  /** The price list date the figures were computed with, when the response already carries it. */
  asOf?: string;
}

export default function PriceEstimateNote({ className, asOf: givenAsOf }: PriceEstimateNoteProps) {
  const [fetchedAsOf, setFetchedAsOf] = useState<string | null>(null);

  useEffect(() => {
    if (givenAsOf) return;
    let active = true;
    loadPricingAsOf().then((date) => {
      if (active) setFetchedAsOf(date);
    });
    return () => {
      active = false;
    };
  }, [givenAsOf]);

  const asOf = givenAsOf ?? fetchedAsOf;

  const classes = ["text-xs text-gray-500 dark:text-gray-400", className].filter(Boolean).join(" ");

  return <p className={classes}>{asOf ? `${LABEL} (prices as of ${asOf})` : LABEL}</p>;
}
