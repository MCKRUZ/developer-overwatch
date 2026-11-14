import { useId, type ReactNode } from "react";
import PriceEstimateNote from "../PriceEstimateNote";

interface SettingsSectionProps {
  title: string;
  error: string | null;
  children: ReactNode;
}

export default function SettingsSection({ title, error, children }: SettingsSectionProps) {
  const headingId = useId();
  return (
    <section aria-labelledby={headingId} className="space-y-3">
      <h3 id={headingId} className="text-lg font-medium">
        {title}
      </h3>
      <PriceEstimateNote />
      {error && (
        <p role="alert" className="text-sm text-red-600 dark:text-red-400">
          {error}
        </p>
      )}
      {children}
    </section>
  );
}
