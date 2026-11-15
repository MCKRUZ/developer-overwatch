import { useId, type ReactNode } from "react";

export const MUTED = "text-sm text-gray-500 dark:text-gray-400";

export const NOT_AVAILABLE_TEXT = "Not available for this Claude Code version";

export function NotAvailable() {
  return <p className={MUTED}>{NOT_AVAILABLE_TEXT}</p>;
}

interface SessionSectionProps<T> {
  title: string;
  /** Lower-case name of what is loaded, for the loading and error text. */
  noun: string;
  data: T | null;
  error: string | null;
  children: (data: T) => ReactNode;
}

/** A named card on the session page that loads and fails on its own. */
export default function SessionSection<T>({ title, noun, data, error, children }: SessionSectionProps<T>) {
  const headingId = useId();
  const nounTitle = noun.charAt(0).toUpperCase() + noun.slice(1);
  let body: ReactNode;
  if (error) body = <p className="text-sm text-red-600 dark:text-red-400">{`${nounTitle} unavailable: ${error}`}</p>;
  else if (data === null) body = <p className={MUTED}>{`Loading ${noun}...`}</p>;
  else body = children(data);

  return (
    <section
      aria-labelledby={headingId}
      className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4 space-y-2"
    >
      <h3 id={headingId} className="font-semibold">
        {title}
      </h3>
      {body}
    </section>
  );
}
