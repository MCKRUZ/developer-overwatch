import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import type { Session } from "../types";
import StatusBadge from "../components/StatusBadge";
import ExportButtons from "../components/ExportButtons";
import { exportCsv } from "../utils/exportCsv";
import { exportPdf } from "../utils/exportPdf";

export default function SessionsPage() {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterDev, setFilterDev] = useState("");

  useEffect(() => {
    api.sessions
      .list()
      .then(setSessions)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500 dark:text-gray-400">Loading sessions...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;

  const filtered = filterDev
    ? sessions.filter((s) => s.developerEmail.toLowerCase().includes(filterDev.toLowerCase()))
    : sessions;

  const sorted = [...filtered].sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime(),
  );

  const developers = [...new Set(sessions.map((s) => s.developerEmail))];

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

  const exportHeaders = ["ID", "Developer", "Repo", "Status", "Started", "Ended", "Turns", "Tools"];
  const exportRows = sorted.map((s) => [
    s.id,
    s.developerEmail,
    s.repoPath ?? "",
    s.status,
    formatDate(s.startedAt),
    s.endedAt ? formatDate(s.endedAt) : "In progress",
    String(s.conversationTurns.length),
    String(s.toolExecutions.length),
  ]);

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

      <div className="mb-4">
        <select
          value={filterDev}
          onChange={(e) => setFilterDev(e.target.value)}
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Developers</option>
          {developers.map((dev) => (
            <option key={dev} value={dev}>
              {dev}
            </option>
          ))}
        </select>
      </div>

      {sorted.length === 0 ? (
        <div className="text-gray-500 dark:text-gray-400 text-center py-8">No sessions found.</div>
      ) : (
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
              <tr>
                <th className="text-left px-4 py-3 font-medium">Session</th>
                <th className="text-left px-4 py-3 font-medium">Developer</th>
                <th className="text-left px-4 py-3 font-medium">Repo</th>
                <th className="text-left px-4 py-3 font-medium">Status</th>
                <th className="text-left px-4 py-3 font-medium">Started</th>
                <th className="text-left px-4 py-3 font-medium">Duration</th>
              </tr>
            </thead>
            <tbody className="divide-y dark:divide-gray-700">
              {sorted.map((s) => (
                <tr key={s.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                  <td className="px-4 py-3">
                    <Link
                      to={`/sessions/${s.id}`}
                      className="text-blue-600 dark:text-blue-400 hover:underline font-mono text-xs"
                    >
                      {s.id.slice(0, 8)}...
                    </Link>
                  </td>
                  <td className="px-4 py-3">{s.developerEmail}</td>
                  <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{s.repoPath ?? "—"}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={s.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{formatDate(s.startedAt)}</td>
                  <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{duration(s.startedAt, s.endedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
