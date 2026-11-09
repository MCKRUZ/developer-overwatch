import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api";
import type { SessionListItem } from "../types";
import StatusBadge from "../components/StatusBadge";
import ExportButtons from "../components/ExportButtons";
import { exportCsv } from "../utils/exportCsv";
import { exportPdf } from "../utils/exportPdf";
import { formatTotalTokens, formatCost, dailyUsageFor, tokenBreakdownTooltip, totalTokens } from "../utils/tokenFormat";
import { sessionDisplayName } from "../utils/sessionDisplay";
import { repoDisplayName, osUserFromRepoPath } from "../utils/pathDisplay";

function todayIsoDate(): string {
  return new Date().toISOString().slice(0, 10);
}

type DateRangePreset = "today" | "7d" | "30d" | "all" | "custom";

const ACTIVE_PAGE_SIZE = 10;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

function duration(start: string, end?: string): string {
  if (!end) return "In progress";
  const ms = new Date(end).getTime() - new Date(start).getTime();
  const mins = Math.round(ms / 60_000);
  if (mins < 60) return `${mins}m`;
  return `${Math.floor(mins / 60)}h ${mins % 60}m`;
}

function SessionsTable({ sessions, referenceDay }: { sessions: SessionListItem[]; referenceDay: string }) {
  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-hidden">
      <table className="w-full text-sm">
        <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
          <tr>
            <th className="text-left px-4 py-3 font-medium">Session</th>
            <th className="text-left px-4 py-3 font-medium">Developer</th>
            <th className="text-left px-4 py-3 font-medium">User</th>
            <th className="text-left px-4 py-3 font-medium">Repo</th>
            <th className="text-left px-4 py-3 font-medium">Status</th>
            <th className="text-left px-4 py-3 font-medium">Started</th>
            <th className="text-left px-4 py-3 font-medium">Duration</th>
            <th className="text-left px-4 py-3 font-medium">Daily Tokens</th>
            <th className="text-left px-4 py-3 font-medium">Daily Cost</th>
          </tr>
        </thead>
        <tbody className="divide-y dark:divide-gray-700">
          {sessions.map((s) => {
            const daily = dailyUsageFor(s, referenceDay);
            return (
              <tr key={s.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    <Link
                      to={`/sessions/${s.id}`}
                      title={s.id}
                      className={`text-blue-600 dark:text-blue-400 hover:underline text-xs ${s.title ? "" : "font-mono"}`}
                    >
                      {sessionDisplayName(s)}
                    </Link>
                    {s.violationCount > 0 && (
                      <span
                        title={`${s.violationCount} governance violation${s.violationCount === 1 ? "" : "s"}`}
                        className="inline-flex items-center px-1.5 py-0.5 rounded text-xs font-medium bg-red-600 text-white"
                      >
                        {s.violationCount}
                      </span>
                    )}
                  </div>
                </td>
                <td className="px-4 py-3">{s.developerEmail}</td>
                <td className="px-4 py-3 text-gray-500 dark:text-gray-400" title={s.repoPath ?? undefined}>
                  {osUserFromRepoPath(s.repoPath)}
                </td>
                <td className="px-4 py-3 text-gray-500 dark:text-gray-400" title={s.repoPath ?? undefined}>
                  {repoDisplayName(s.repoPath)}
                </td>
                <td className="px-4 py-3">
                  <StatusBadge status={s.status} />
                </td>
                <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatDate(s.startedAt)}</td>
                <td className="px-4 py-3 text-gray-500 dark:text-gray-400">
                  {duration(s.startedAt, s.endedAt)}
                </td>
                <td
                  className="px-4 py-3 text-gray-500 dark:text-gray-400"
                  title={tokenBreakdownTooltip({
                    totalInputTokens: daily.inputTokens,
                    totalOutputTokens: daily.outputTokens,
                    totalCacheReadInputTokens: daily.cacheReadInputTokens,
                    totalCacheCreationInputTokens: daily.cacheCreationInputTokens,
                  })}
                >
                  {formatTotalTokens(daily)}
                </td>
                <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatCost(daily.estimatedCostUsd)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function presetToRange(preset: DateRangePreset): { from?: string; to?: string } {
  if (preset === "all") return {};
  const now = new Date();
  const to = new Date(now);
  to.setDate(to.getDate() + 1);
  to.setHours(0, 0, 0, 0);
  const from = new Date(to);
  if (preset === "today") from.setDate(from.getDate() - 1);
  if (preset === "7d") from.setDate(from.getDate() - 7);
  if (preset === "30d") from.setDate(from.getDate() - 30);
  return { from: from.toISOString(), to: to.toISOString() };
}

export default function SessionsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [sessions, setSessions] = useState<SessionListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterDev, setFilterDev] = useState(() => searchParams.get("developer") ?? "");
  const [filterRepo, setFilterRepo] = useState("");
  const [filterStatus, setFilterStatus] = useState<SessionListItem["status"] | "">("");
  const initialFrom = useMemo(() => searchParams.get("from"), []);
  const initialTo = useMemo(() => searchParams.get("to"), []);
  const [datePreset, setDatePreset] = useState<DateRangePreset>(
    initialFrom && initialTo ? "custom" : "today",
  );
  const [customRange, setCustomRange] = useState<{ from: string; to: string } | null>(
    initialFrom && initialTo ? { from: initialFrom, to: initialTo } : null,
  );

  useEffect(() => {
    setLoading(true);
    const { from, to } = datePreset === "custom" && customRange ? customRange : presetToRange(datePreset);
    api.sessions
      .list({
        developer: filterDev || undefined,
        repo: filterRepo || undefined,
        status: filterStatus || undefined,
        from,
        to,
      })
      .then(setSessions)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [filterDev, filterRepo, filterStatus, datePreset, customRange]);

  function clearDrillDownFilter() {
    setDatePreset("today");
    setCustomRange(null);
    setFilterDev("");
    setSearchParams({});
  }

  const developers = useMemo(() => [...new Set(sessions.map((s) => s.developerEmail))], [sessions]);
  const repos = useMemo(
    () => [...new Set(sessions.map((s) => s.repoPath).filter((r): r is string => !!r))],
    [sessions],
  );

  const sorted = useMemo(
    () => [...sessions].sort((a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()),
    [sessions],
  );

  // Active sessions are pinned above the day groups regardless of when they started — a
  // resumed session from days ago is still happening now and shouldn't get buried in a
  // historical day group the current date filter wouldn't otherwise even show.
  const activeSessions = useMemo(() => sorted.filter((s) => s.status === "Active"), [sorted]);

  const [activePage, setActivePage] = useState(0);
  const totalActivePages = Math.max(1, Math.ceil(activeSessions.length / ACTIVE_PAGE_SIZE));
  const clampedActivePage = Math.min(activePage, totalActivePages - 1);
  const pagedActiveSessions = useMemo(
    () => activeSessions.slice(clampedActivePage * ACTIVE_PAGE_SIZE, (clampedActivePage + 1) * ACTIVE_PAGE_SIZE),
    [activeSessions, clampedActivePage],
  );

  useEffect(() => {
    setActivePage(0);
  }, [sessions]);

  const dayGroups = useMemo(() => {
    const groups = new Map<string, SessionListItem[]>();
    for (const s of sorted) {
      if (s.status === "Active") continue;
      const day = s.startedAt.slice(0, 10);
      if (!groups.has(day)) groups.set(day, []);
      groups.get(day)!.push(s);
    }
    return [...groups.entries()].map(([day, items]) => {
      const dailyTotals = items.map((s) => dailyUsageFor(s, day));
      return {
        day,
        items,
        totalCost: dailyTotals.reduce((sum, d) => sum + d.estimatedCostUsd, 0),
        totalTokens: dailyTotals.reduce((sum, d) => sum + totalTokens(d), 0),
      };
    });
  }, [sorted]);

  const today = todayIsoDate();
  const exportHeaders = [
    "ID", "Developer", "User", "Repo", "Status", "Started", "Ended", "Daily Tokens", "Daily Cost", "Violations",
  ];
  const exportRows = sorted.map((s) => {
    const referenceDay = s.status === "Active" ? today : s.startedAt.slice(0, 10);
    const daily = dailyUsageFor(s, referenceDay);
    return [
      s.id,
      s.developerEmail,
      osUserFromRepoPath(s.repoPath),
      repoDisplayName(s.repoPath),
      s.status,
      formatDate(s.startedAt),
      s.endedAt ? formatDate(s.endedAt) : "In progress",
      formatTotalTokens(daily),
      formatCost(daily.estimatedCostUsd),
      String(s.violationCount),
    ];
  });

  function handleExportCsv() {
    exportCsv("sessions", exportHeaders, exportRows);
  }

  function handleExportPdf() {
    exportPdf("sessions", "Developer Overwatch - Sessions", exportHeaders, exportRows);
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold">Sessions</h2>
        <ExportButtons onExportCsv={handleExportCsv} onExportPdf={handleExportPdf} />
      </div>

      {datePreset === "custom" && customRange && (
        <div className="flex items-center gap-2 mb-3 text-sm bg-indigo-50 dark:bg-indigo-900/20 border border-indigo-200 dark:border-indigo-800 rounded px-3 py-2">
          <span>
            Drilled into{" "}
            <strong>{new Date(customRange.from).toLocaleDateString()}</strong>
            {filterDev && (
              <>
                {" "}
                for <strong>{filterDev}</strong>
              </>
            )}
          </span>
          <button
            type="button"
            onClick={clearDrillDownFilter}
            className="text-indigo-600 dark:text-indigo-400 hover:underline text-xs ml-auto"
          >
            Clear
          </button>
        </div>
      )}

      <div className="flex flex-wrap gap-3 mb-4">
        <select
          value={datePreset}
          onChange={(e) => {
            setDatePreset(e.target.value as DateRangePreset);
            setCustomRange(null);
          }}
          aria-label="Date range"
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          {datePreset === "custom" && <option value="custom">Custom (drill-down)</option>}
          <option value="today">Today</option>
          <option value="7d">Last 7 days</option>
          <option value="30d">Last 30 days</option>
          <option value="all">All time</option>
        </select>
        <select
          value={filterDev}
          onChange={(e) => setFilterDev(e.target.value)}
          aria-label="Developer"
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Developers</option>
          {developers.map((dev) => (
            <option key={dev} value={dev}>
              {dev}
            </option>
          ))}
        </select>
        <select
          value={filterRepo}
          onChange={(e) => setFilterRepo(e.target.value)}
          aria-label="Repo"
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Repos</option>
          {repos.map((repo) => (
            <option key={repo} value={repo}>
              {repoDisplayName(repo)}
            </option>
          ))}
        </select>
        <select
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as SessionListItem["status"] | "")}
          aria-label="Status"
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Statuses</option>
          <option value="Active">Active</option>
          <option value="Stale">Stale</option>
          <option value="Completed">Completed</option>
          <option value="Failed">Failed</option>
        </select>
      </div>

      {loading ? (
        <div className="text-gray-500 dark:text-gray-400">Loading sessions...</div>
      ) : error ? (
        <div className="text-red-600">Error: {error}</div>
      ) : sorted.length === 0 ? (
        <div className="text-gray-500 dark:text-gray-400 text-center py-8">No sessions found.</div>
      ) : (
        <div className="space-y-6">
          {activeSessions.length > 0 && (
            <div>
              <div className="flex items-baseline gap-3 mb-2 text-sm">
                <h3 className="font-semibold flex items-center gap-1.5">
                  <span className="inline-block w-2 h-2 rounded-full bg-green-500" />
                  Active Now
                </h3>
                <span className="text-gray-500 dark:text-gray-400">
                  {activeSessions.length} session{activeSessions.length === 1 ? "" : "s"}
                </span>
              </div>
              <SessionsTable sessions={pagedActiveSessions} referenceDay={today} />
              {totalActivePages > 1 && (
                <div className="flex items-center justify-end gap-3 mt-2 text-sm">
                  <button
                    type="button"
                    onClick={() => setActivePage((p) => Math.max(0, p - 1))}
                    disabled={clampedActivePage === 0}
                    className="px-2 py-1 rounded border dark:border-gray-600 text-gray-600 dark:text-gray-300 disabled:opacity-40 disabled:cursor-not-allowed"
                  >
                    Prev
                  </button>
                  <span className="text-gray-500 dark:text-gray-400">
                    Page {clampedActivePage + 1} of {totalActivePages}
                  </span>
                  <button
                    type="button"
                    onClick={() => setActivePage((p) => Math.min(totalActivePages - 1, p + 1))}
                    disabled={clampedActivePage >= totalActivePages - 1}
                    className="px-2 py-1 rounded border dark:border-gray-600 text-gray-600 dark:text-gray-300 disabled:opacity-40 disabled:cursor-not-allowed"
                  >
                    Next
                  </button>
                </div>
              )}
            </div>
          )}

          {dayGroups.map(({ day, items, totalCost, totalTokens }) => (
            <div key={day}>
              <div className="flex items-baseline gap-3 mb-2 text-sm">
                <h3 className="font-semibold">
                  {new Date(day).toLocaleDateString(undefined, {
                    weekday: "long",
                    year: "numeric",
                    month: "long",
                    day: "numeric",
                  })}
                </h3>
                <span className="text-gray-500 dark:text-gray-400">
                  {items.length} session{items.length === 1 ? "" : "s"} · {totalTokens.toLocaleString()} tokens ·{" "}
                  {formatCost(totalCost)}
                </span>
              </div>
              <SessionsTable sessions={items} referenceDay={day} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
