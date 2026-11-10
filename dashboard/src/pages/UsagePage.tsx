import { useState } from "react";
import type { UsageSplitRow, UsageSummary } from "../types";
import { useUsageData } from "../hooks/useUsageData";
import PeriodPicker, { type UsagePeriod } from "../components/usage/PeriodPicker";
import UsageTiles from "../components/usage/UsageTiles";
import UsageBucketsTable from "../components/usage/UsageBucketsTable";
import UsageSplitTabs from "../components/usage/UsageSplitTabs";
import RunningSessionsList from "../components/usage/RunningSessionsList";
import ExportButtons from "../components/ExportButtons";
import PriceEstimateNote from "../components/PriceEstimateNote";
import { exportCsv } from "../utils/exportCsv";
import { exportJson } from "../utils/exportJson";
import { exportPdf } from "../utils/exportPdf";
import { lastDaysRange } from "../utils/localDate";
import { usageCsvTable, usageJsonExport, usagePdfTable } from "../utils/usageColumns";

const SELECT_CLASS = "border dark:border-gray-600 rounded px-3 py-1.5 text-sm dark:bg-gray-800 dark:text-gray-100";

/** Options from the split rows, plus the current choice: filtering narrows the splits to that one value. */
function filterOptions(rows: readonly UsageSplitRow[] | undefined, selected: string): string[] {
  const keys = (rows ?? []).map((r) => r.key).filter((k): k is string => !!k);
  return [...new Set(selected ? [selected, ...keys] : keys)];
}

interface FilterSelectProps {
  label: string;
  value: string;
  options: string[];
  onChange: (value: string) => void;
}

function FilterSelect({ label, value, options, onChange }: FilterSelectProps) {
  return (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} className={SELECT_CLASS}>
      <option value="">{`All ${label.toLowerCase()}s`}</option>
      {options.map((o) => (
        <option key={o} value={o}>
          {o}
        </option>
      ))}
    </select>
  );
}

function exportAll(summary: UsageSummary, format: "csv" | "json" | "pdf") {
  const filename = `usage-${summary.from}-to-${summary.to}`;
  if (format === "json") return exportJson(filename, usageJsonExport(summary));
  if (format === "csv") {
    const table = usageCsvTable(summary.buckets, summary.pricesAsOf);
    return exportCsv(filename, table.headers, table.rows);
  }
  const table = usagePdfTable(summary.buckets);
  exportPdf(
    filename,
    `Developer Overwatch - Usage ${summary.from} to ${summary.to} (API-equivalent estimate, prices as of ${summary.pricesAsOf})`,
    table.headers,
    table.rows,
  );
}

export default function UsagePage() {
  const [period, setPeriod] = useState<UsagePeriod>(() => ({ granularity: "day", ...lastDaysRange(new Date(), 7) }));
  const [developer, setDeveloper] = useState("");
  const [project, setProject] = useState("");
  const { summary, running, error, runningError } = useUsageData({
    ...period,
    developer: developer || undefined,
    project: project || undefined,
  });

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-xl font-semibold">Usage</h2>
        {summary && (
          <ExportButtons
            onExportCsv={() => exportAll(summary, "csv")}
            onExportJson={() => exportAll(summary, "json")}
            onExportPdf={() => exportAll(summary, "pdf")}
          />
        )}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <PeriodPicker {...period} onChange={setPeriod} />
        <FilterSelect
          label="Developer"
          value={developer}
          options={filterOptions(summary?.splits.developer, developer)}
          onChange={setDeveloper}
        />
        <FilterSelect
          label="Project"
          value={project}
          options={filterOptions(summary?.splits.project, project)}
          onChange={setProject}
        />
      </div>

      {error && <p className="text-red-600 dark:text-red-400 text-sm">{error}</p>}
      {!summary && !error && <p className="text-gray-500 dark:text-gray-400">Loading usage...</p>}

      {summary && (
        <>
          <PriceEstimateNote asOf={summary.pricesAsOf} />
          <UsageTiles totals={summary.totals} burn={summary.burn} />
          <section className="space-y-2">
            <h3 className="text-lg font-medium">Running now</h3>
            {runningError ? (
              <p className="text-sm text-red-600 dark:text-red-400">{`Running sessions unavailable: ${runningError}`}</p>
            ) : (
              <RunningSessionsList sessions={running} />
            )}
          </section>
          <section className="space-y-2">
            <h3 className="text-lg font-medium">By {period.granularity}</h3>
            <UsageBucketsTable buckets={summary.buckets} />
          </section>
          <section className="space-y-2">
            <h3 className="text-lg font-medium">Split</h3>
            <UsageSplitTabs splits={summary.splits} />
          </section>
        </>
      )}
    </div>
  );
}
