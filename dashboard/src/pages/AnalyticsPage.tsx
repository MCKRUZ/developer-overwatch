import { useEffect, useState } from "react";
import { api } from "../api";
import type { TeamAnalytics } from "../types";

export default function AnalyticsPage() {
  const [analytics, setAnalytics] = useState<TeamAnalytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.analytics
      .team()
      .then(setAnalytics)
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
