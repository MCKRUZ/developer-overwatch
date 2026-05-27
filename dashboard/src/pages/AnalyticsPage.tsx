import { useEffect, useState } from "react";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  PieChart,
  Pie,
  Cell,
  ResponsiveContainer,
  CartesianGrid,
  Legend,
} from "recharts";
import { api } from "../api";
import type { TeamAnalytics, Session } from "../types";
import ChartCard from "../components/ChartCard";

const COLORS = {
  primary: "#2563eb",
  secondary: "#7c3aed",
  success: "#16a34a",
  warning: "#d97706",
  danger: "#dc2626",
  info: "#0891b2",
};

const SEVERITY_COLORS: Record<string, string> = {
  Critical: "#dc2626",
  Error: "#ea580c",
  Warning: "#d97706",
  Info: "#2563eb",
};

const PIE_PALETTE = [
  COLORS.primary,
  COLORS.secondary,
  COLORS.success,
  COLORS.warning,
  COLORS.danger,
  COLORS.info,
];

interface SessionsByDay {
  date: string;
  count: number;
}

interface ToolUsageEntry {
  name: string;
  count: number;
}

interface SeverityEntry {
  name: string;
  count: number;
  color: string;
}

function deriveSessionsByDay(sessions: Session[]): SessionsByDay[] {
  const counts = new Map<string, number>();
  for (const s of sessions) {
    const day = s.startedAt.slice(0, 10);
    counts.set(day, (counts.get(day) ?? 0) + 1);
  }
  return [...counts.entries()]
    .map(([date, count]) => ({ date, count }))
    .sort((a, b) => a.date.localeCompare(b.date));
}

function deriveToolUsage(sessions: Session[]): ToolUsageEntry[] {
  const counts = new Map<string, number>();
  for (const s of sessions) {
    for (const t of s.toolExecutions) {
      counts.set(t.toolName, (counts.get(t.toolName) ?? 0) + 1);
    }
  }
  return [...counts.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count)
    .slice(0, 8);
}

const SEVERITY_KEYS = ["Critical", "Error", "Warning", "Info"] as const;

function deriveViolationsBySeverity(sessions: Session[]): SeverityEntry[] {
  const counts = new Map<string, number>(
    SEVERITY_KEYS.map((k) => [k, 0]),
  );
  for (const s of sessions) {
    for (const v of s.governanceViolations) {
      const prev = counts.get(v.severity);
      if (prev !== undefined) {
        counts.set(v.severity, prev + 1);
      }
    }
  }
  return [...counts.entries()]
    .filter(([, count]) => count > 0)
    .map(([name, count]) => ({
      name,
      count,
      color: SEVERITY_COLORS[name] ?? "#6b7280",
    }));
}

function EmptyChart({ message }: { message: string }) {
  return (
    <div className="flex items-center justify-center h-[250px] text-gray-400 text-sm">
      {message}
    </div>
  );
}

function renderToolLabel(props: { name?: string; percent?: number }) {
  const { name, percent } = props;
  if (!name || percent == null) return "";
  return `${name} ${(percent * 100).toFixed(0)}%`;
}

function renderSeverityLabel(props: { name?: string; value?: number }) {
  const { name, value } = props;
  if (!name || value == null) return "";
  return `${name} (${value})`;
}

export default function AnalyticsPage() {
  const [analytics, setAnalytics] = useState<TeamAnalytics | null>(null);
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.analytics.team(), api.sessions.list()])
      .then(([teamData, sessionData]) => {
        setAnalytics(teamData);
        setSessions(sessionData);
      })
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500">Loading analytics...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;
  if (!analytics) return <div className="text-gray-500">No data available.</div>;

  const approvalRate =
    analytics.totalPullRequests > 0
      ? Math.round((analytics.approvedPullRequests / analytics.totalPullRequests) * 100)
      : 0;

  const sessionsByDay = deriveSessionsByDay(sessions);
  const toolUsage = deriveToolUsage(sessions);
  const violationsBySeverity = deriveViolationsBySeverity(sessions);

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">Team Analytics</h2>

      <div className="grid grid-cols-4 gap-4 mb-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{analytics.sessionsThisWeek}</div>
          <div className="text-sm text-gray-500">Sessions This Week</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{approvalRate}%</div>
          <div className="text-sm text-gray-500">PR Approval Rate</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{analytics.pendingPullRequests}</div>
          <div className="text-sm text-gray-500">Pending PRs</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-red-600">{analytics.violationsThisWeek}</div>
          <div className="text-sm text-gray-500">Violations This Week</div>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-4 mb-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalSessions}</div>
          <div className="text-sm text-gray-500">Total Sessions</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalPullRequests}</div>
          <div className="text-sm text-gray-500">Total Pull Requests</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-lg font-bold">{analytics.totalViolations}</div>
          <div className="text-sm text-gray-500">Total Violations</div>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4 mb-6">
        <ChartCard title="Sessions Over Time">
          {sessionsByDay.length === 0 ? (
            <EmptyChart message="No session data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <BarChart data={sessionsByDay}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" tick={{ fontSize: 12 }} />
                <YAxis allowDecimals={false} />
                <Tooltip />
                <Bar dataKey="count" name="Sessions" fill={COLORS.primary} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </ChartCard>

        <ChartCard title="Tool Usage Distribution">
          {toolUsage.length === 0 ? (
            <EmptyChart message="No tool usage data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <PieChart>
                <Pie
                  data={toolUsage}
                  dataKey="count"
                  nameKey="name"
                  cx="50%"
                  cy="50%"
                  outerRadius={90}
                  label={renderToolLabel}
                >
                  {toolUsage.map((_entry, index) => (
                    <Cell
                      key={`tool-${index}`}
                      fill={PIE_PALETTE[index % PIE_PALETTE.length]}
                    />
                  ))}
                </Pie>
                <Tooltip />
              </PieChart>
            </ResponsiveContainer>
          )}
        </ChartCard>

        <ChartCard title="Developer Activity">
          {analytics.developers.length === 0 ? (
            <EmptyChart message="No developer data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <BarChart data={analytics.developers}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="email" tick={{ fontSize: 11 }} />
                <YAxis allowDecimals={false} />
                <Tooltip />
                <Legend />
                <Bar dataKey="sessionCount" name="Sessions" fill={COLORS.primary} />
                <Bar dataKey="commitCount" name="Commits" fill={COLORS.secondary} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </ChartCard>

        <ChartCard title="Governance Violations by Severity">
          {violationsBySeverity.length === 0 ? (
            <EmptyChart message="No violation data available" />
          ) : (
            <ResponsiveContainer width="100%" height={250}>
              <PieChart>
                <Pie
                  data={violationsBySeverity}
                  dataKey="count"
                  nameKey="name"
                  cx="50%"
                  cy="50%"
                  outerRadius={90}
                  label={renderSeverityLabel}
                >
                  {violationsBySeverity.map((entry) => (
                    <Cell key={`sev-${entry.name}`} fill={entry.color} />
                  ))}
                </Pie>
                <Tooltip />
                <Legend />
              </PieChart>
            </ResponsiveContainer>
          )}
        </ChartCard>
      </div>

      <div className="bg-white rounded-lg border overflow-hidden">
        <h3 className="font-semibold px-4 py-3 border-b bg-gray-50">Developer Breakdown</h3>
        {analytics.developers.length === 0 ? (
          <div className="text-gray-500 text-sm text-center py-8">No developer data.</div>
        ) : (
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Developer</th>
                <th className="text-right px-4 py-3 font-medium">Sessions</th>
                <th className="text-right px-4 py-3 font-medium">Commits</th>
                <th className="text-right px-4 py-3 font-medium">Violations</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {analytics.developers.map((dev) => (
                <tr key={dev.email} className="hover:bg-gray-50">
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
