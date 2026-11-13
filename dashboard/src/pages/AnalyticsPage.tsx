import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ReferenceLine,
  ResponsiveContainer,
  CartesianGrid,
  Legend,
} from "recharts";
import { api } from "../api";
import type { TeamAnalytics, SessionListItem } from "../types";
import ChartCard from "../components/ChartCard";
import ExportButtons from "../components/ExportButtons";
import PriceEstimateNote from "../components/PriceEstimateNote";
import { exportCsv } from "../utils/exportCsv";
import { exportPdf } from "../utils/exportPdf";
import { formatCost } from "../utils/tokenFormat";

const COLORS = {
  primary: "#2563eb",
  secondary: "#7c3aed",
  danger: "#dc2626",
};

// Validated categorical palette (dataviz skill) — fixed hue order, never cycled.
// Beyond 8 developers, extras fold into "Other" rather than generating a new hue.
const USER_PALETTE = [
  "#2a78d6",
  "#eb6834",
  "#1baf7a",
  "#eda100",
  "#e87ba4",
  "#008300",
  "#4a3aa7",
  "#e34948",
];
const OTHER_BUCKET = "Other";

interface DayUserRow {
  date: string;
  total: number;
  [developer: string]: string | number;
}

export function deriveSessionsByDayAndUser(sessions: SessionListItem[]): {
  data: DayUserRow[];
  developers: string[];
} {
  const sessionCounts = new Map<string, number>();
  for (const s of sessions) {
    sessionCounts.set(s.developerEmail, (sessionCounts.get(s.developerEmail) ?? 0) + 1);
  }
  const topDevelopers = [...sessionCounts.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, USER_PALETTE.length)
    .map(([email]) => email);
  const topSet = new Set(topDevelopers);
  const hasOverflow = sessionCounts.size > topDevelopers.length;
  const developers = hasOverflow ? [...topDevelopers, OTHER_BUCKET] : topDevelopers;

  const byDay = new Map<string, DayUserRow>();
  for (const s of sessions) {
    const day = s.startedAt.slice(0, 10);
    if (!byDay.has(day)) {
      const row: DayUserRow = { date: day, total: 0 };
      for (const dev of developers) row[dev] = 0;
      byDay.set(day, row);
    }
    const row = byDay.get(day)!;
    const key = topSet.has(s.developerEmail) ? s.developerEmail : OTHER_BUCKET;
    row[key] = (row[key] as number) + 1;
    row.total = (row.total as number) + 1;
  }

  return {
    data: [...byDay.values()].sort((a, b) => a.date.localeCompare(b.date)),
    developers,
  };
}

function EmptyChart({ message }: { message: string }) {
  return (
    <div className="flex items-center justify-center h-[250px] text-gray-400 dark:text-gray-500 text-sm">
      {message}
    </div>
  );
}

const CHART_TICK = { fontSize: 12, fill: "var(--chart-text)" };
const CHART_TICK_SM = { fontSize: 11, fill: "var(--chart-text)" };

export default function AnalyticsPage() {
  const navigate = useNavigate();
  const [analytics, setAnalytics] = useState<TeamAnalytics | null>(null);
  const [sessions, setSessions] = useState<SessionListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterDev, setFilterDev] = useState("");

  useEffect(() => {
    setLoading(true);
    Promise.all([
      api.analytics.team(undefined, undefined, filterDev || undefined),
      api.sessions.list({ developer: filterDev || undefined }),
    ])
      .then(([teamData, sessionData]) => {
        setAnalytics(teamData);
        setSessions(sessionData);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [filterDev]);

  const { data: sessionsByDayUser, developers: chartDevelopers } = useMemo(
    () => deriveSessionsByDayAndUser(sessions),
    [sessions],
  );
  const developerOptions = useMemo(
    () => [...new Set(sessions.map((s) => s.developerEmail))].sort(),
    [sessions],
  );

  if (loading) return <div className="text-gray-500 dark:text-gray-400">Loading analytics...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;
  if (!analytics) return <div className="text-gray-500 dark:text-gray-400">No data available.</div>;

  const approvalRate =
    analytics.totalPullRequests > 0
      ? Math.round((analytics.approvedPullRequests / analytics.totalPullRequests) * 100)
      : 0;

  const teamDailyBudget = analytics.dailyCostThresholdUsd;
  const overThreshold =
    teamDailyBudget !== null && analytics.dailyCostTrend.some((d) => d.estimatedCostUsd > teamDailyBudget);

  function drillIntoSessions(day: string, developer?: string) {
    const from = new Date(`${day}T00:00:00.000Z`).toISOString();
    const to = new Date(new Date(`${day}T00:00:00.000Z`).getTime() + 86_400_000).toISOString();
    const params = new URLSearchParams({ from, to });
    if (developer && developer !== OTHER_BUCKET) params.set("developer", developer);
    navigate(`/sessions?${params.toString()}`);
  }

  const exportHeaders = ["Developer", "Sessions", "Commits", "Violations"];
  const exportRows = analytics.developers.map((dev) => [
    dev.email,
    String(dev.sessionCount),
    String(dev.commitCount),
    String(dev.violationCount),
  ]);

  function handleExportCsv() {
    exportCsv("team-analytics", exportHeaders, exportRows);
  }

  function handleExportPdf() {
    exportPdf("team-analytics", "Developer Overwatch - Team Analytics", exportHeaders, exportRows);
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-xl font-semibold">Team Analytics</h2>
        <ExportButtons onExportCsv={handleExportCsv} onExportPdf={handleExportPdf} />
      </div>

      <div className="flex flex-wrap gap-3 mb-4">
        <select
          value={filterDev}
          onChange={(e) => setFilterDev(e.target.value)}
          aria-label="Developer"
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Developers</option>
          {developerOptions.map((dev) => (
            <option key={dev} value={dev}>
              {dev}
            </option>
          ))}
        </select>
      </div>

      <div className="grid grid-cols-4 gap-4 mb-6">
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold">{analytics.sessionsThisWeek}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Sessions This Week</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold">{approvalRate}%</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">PR Approval Rate</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold">{analytics.pendingPullRequests}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Pending PRs</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold text-red-600">{analytics.violationsThisWeek}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Violations This Week</div>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-4 mb-6">
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-lg font-bold">{analytics.totalSessions}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Total Sessions</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-lg font-bold">{analytics.totalPullRequests}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Total Pull Requests</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-lg font-bold">{analytics.totalViolations}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Total Violations</div>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 mb-6">
        <ChartCard title="Sessions Per Day by Developer">
          {sessionsByDayUser.length === 0 ? (
            <EmptyChart message="No session data available" />
          ) : (
            <>
              <ResponsiveContainer width="100%" height={250}>
                <BarChart data={sessionsByDayUser}>
                  <CartesianGrid strokeDasharray="3 3" stroke="var(--chart-grid)" />
                  <XAxis dataKey="date" tick={CHART_TICK} />
                  <YAxis allowDecimals={false} tick={CHART_TICK} />
                  <Tooltip />
                  <Legend />
                  {chartDevelopers.map((dev, i) => (
                    <Bar
                      key={dev}
                      dataKey={dev}
                      name={dev}
                      stackId="sessions"
                      fill={dev === OTHER_BUCKET ? "var(--chart-grid)" : USER_PALETTE[i]}
                      cursor="pointer"
                      onClick={(item) => drillIntoSessions((item.payload as DayUserRow).date, dev)}
                    />
                  ))}
                </BarChart>
              </ResponsiveContainer>
              <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                Click a segment to drill into that day&apos;s sessions for that developer.
              </p>
            </>
          )}
        </ChartCard>

        <ChartCard
          title={`Daily Cost Trend${overThreshold ? " ⚠" : ""}`}
        >
          <PriceEstimateNote className="mb-2" />
          {analytics.dailyCostTrend.length === 0 ? (
            <EmptyChart message="No cost data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <BarChart data={analytics.dailyCostTrend}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--chart-grid)" />
                <XAxis dataKey="date" tick={CHART_TICK} />
                <YAxis tick={CHART_TICK} tickFormatter={(v: number) => `$${v}`} />
                <Tooltip formatter={(value) => formatCost(Number(value))} />
                {teamDailyBudget !== null && (
                  <ReferenceLine
                    y={teamDailyBudget}
                    stroke={COLORS.danger}
                    strokeDasharray="4 4"
                    label={{ value: "Team daily budget", fill: COLORS.danger, fontSize: 11 }}
                  />
                )}
                <Bar dataKey="estimatedCostUsd" name="Cost (USD)" fill={COLORS.secondary} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </ChartCard>

        <ChartCard title="Developer Activity">
          {analytics.developers.length === 0 ? (
            <EmptyChart message="No developer data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <BarChart data={analytics.developers}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--chart-grid)" />
                <XAxis dataKey="email" tick={CHART_TICK_SM} />
                <YAxis allowDecimals={false} tick={CHART_TICK} />
                <Tooltip />
                <Legend />
                <Bar dataKey="sessionCount" name="Sessions" fill={COLORS.primary} />
                <Bar dataKey="commitCount" name="Commits" fill={COLORS.secondary} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </ChartCard>
      </div>

      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-hidden">
        <h3 className="font-semibold px-4 py-3 border-b dark:border-gray-700 bg-gray-50 dark:bg-gray-700">Developer Breakdown</h3>
        {analytics.developers.length === 0 ? (
          <div className="text-gray-500 dark:text-gray-400 text-sm text-center py-8">No developer data.</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Developer</th>
                <th className="text-right px-4 py-3 font-medium">Sessions</th>
                <th className="text-right px-4 py-3 font-medium">Commits</th>
                <th className="text-right px-4 py-3 font-medium">Violations</th>
              </tr>
            </thead>
            <tbody className="divide-y dark:divide-gray-700">
              {analytics.developers.map((dev) => (
                <tr key={dev.email} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                  <td className="px-4 py-3">{dev.email}</td>
                  <td className="px-4 py-3 text-right">{dev.sessionCount}</td>
                  <td className="px-4 py-3 text-right">{dev.commitCount}</td>
                  <td className="px-4 py-3 text-right">
                    <span className={dev.violationCount > 0 ? "text-red-600 font-medium" : ""}>
                      {dev.violationCount}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
