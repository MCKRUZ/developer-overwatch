import type { UsageGranularity } from "../../types";

export interface UsagePeriod {
  granularity: UsageGranularity;
  /** Local YYYY-MM-DD dates, both inclusive. */
  from: string;
  to: string;
}

interface PeriodPickerProps extends UsagePeriod {
  onChange: (period: UsagePeriod) => void;
}

const INPUT_CLASS = "border dark:border-gray-600 rounded px-3 py-1.5 text-sm dark:bg-gray-800 dark:text-gray-100";

export default function PeriodPicker({ granularity, from, to, onChange }: PeriodPickerProps) {
  const period = { granularity, from, to };
  return (
    <div className="flex flex-wrap items-center gap-2">
      <label className="flex items-center gap-1 text-sm">
        <span className="sr-only">Granularity</span>
        <select
          aria-label="Granularity"
          value={granularity}
          onChange={(e) => onChange({ ...period, granularity: e.target.value as UsageGranularity })}
          className={INPUT_CLASS}
        >
          <option value="day">By day</option>
          <option value="week">By week</option>
          <option value="month">By month</option>
        </select>
      </label>
      <input
        type="date"
        aria-label="From"
        value={from}
        onChange={(e) => onChange({ ...period, from: e.target.value })}
        className={INPUT_CLASS}
      />
      <span className="text-sm text-gray-500 dark:text-gray-400">to</span>
      <input
        type="date"
        aria-label="To"
        value={to}
        onChange={(e) => onChange({ ...period, to: e.target.value })}
        className={INPUT_CLASS}
      />
    </div>
  );
}
