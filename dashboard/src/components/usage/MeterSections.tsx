import { useId, type ReactNode } from "react";
import { Link } from "react-router-dom";
import type { MeterData } from "../../hooks/useMeterData";
import PriceEstimateNote from "../PriceEstimateNote";
import BudgetStatusTable from "./BudgetStatusTable";
import UsageWindowCards from "./UsageWindowCards";
import PlanValueTable from "./PlanValueTable";

const MUTED = "text-sm text-gray-500 dark:text-gray-400";

interface MeterSectionProps<T> {
  title: string;
  /** Lower-case name for the loading and error text. */
  noun: string;
  items: readonly T[] | null;
  error: string | null;
  pricesAsOf: string;
  empty: ReactNode;
  children: (items: readonly T[]) => ReactNode;
}

function MeterSection<T>({ title, noun, items, error, pricesAsOf, empty, children }: MeterSectionProps<T>) {
  const headingId = useId();
  const nounTitle = noun.charAt(0).toUpperCase() + noun.slice(1);
  let body: ReactNode;
  if (error) body = <p className="text-sm text-red-600 dark:text-red-400">{`${nounTitle} unavailable: ${error}`}</p>;
  else if (!items) body = <p className={MUTED}>{`Loading ${noun}...`}</p>;
  else if (items.length === 0) body = empty;
  else body = children(items);

  return (
    <section aria-labelledby={headingId} className="space-y-2">
      <h3 id={headingId} className="text-lg font-medium">
        {title}
      </h3>
      <PriceEstimateNote asOf={pricesAsOf} />
      {body}
    </section>
  );
}

function EmptyWithSettingsLink({ message }: { message: string }) {
  return (
    <p className={MUTED}>
      {message}{" "}
      <Link to="/settings" className="text-blue-600 dark:text-blue-400 hover:underline">
        Set them up in Settings
      </Link>
    </p>
  );
}

interface MeterSectionsProps {
  meter: MeterData;
  pricesAsOf: string;
}

/** Budgets, 5-hour windows and plan value on the Usage page; each loads and fails on its own. */
export default function MeterSections({ meter, pricesAsOf }: MeterSectionsProps) {
  return (
    <>
      <MeterSection
        title="Budgets"
        noun="budgets"
        items={meter.budgets}
        error={meter.budgetsError}
        pricesAsOf={pricesAsOf}
        empty={<EmptyWithSettingsLink message="No budgets yet." />}
      >
        {(statuses) => <BudgetStatusTable statuses={statuses} />}
      </MeterSection>
      <MeterSection
        title="5-hour windows (estimate)"
        noun="5-hour windows"
        items={meter.windows}
        error={meter.windowsError}
        pricesAsOf={pricesAsOf}
        empty={<p className={MUTED}>No usage in the last 5 hours and no developer plans.</p>}
      >
        {(windows) => <UsageWindowCards windows={windows} />}
      </MeterSection>
      <MeterSection
        title="Plan vs API value"
        noun="plan value"
        items={meter.planValues}
        error={meter.planValuesError}
        pricesAsOf={pricesAsOf}
        empty={<EmptyWithSettingsLink message="No developer plans yet." />}
      >
        {(values) => <PlanValueTable values={values} />}
      </MeterSection>
    </>
  );
}
