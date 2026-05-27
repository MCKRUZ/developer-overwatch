import { useEffect, useState } from "react";
import { api } from "../api";
import type { PullRequestReview } from "../types";
import StatusBadge from "../components/StatusBadge";

type SortField = "prNumber" | "repo" | "status";
type FilterStatus = "" | "Pending" | "Approved" | "NeedsChanges";

export default function PrQueuePage() {
  const [prs, setPrs] = useState<PullRequestReview[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [sortField, setSortField] = useState<SortField>("prNumber");
  const [sortAsc, setSortAsc] = useState(false);
  const [filterStatus, setFilterStatus] = useState<FilterStatus>("");

  useEffect(() => {
    api.pullRequests
      .list()
      .then(setPrs)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-gray-500 dark:text-gray-400">Loading pull requests...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;

  const filtered = filterStatus ? prs.filter((pr) => pr.status === filterStatus) : prs;

  const sorted = [...filtered].sort((a, b) => {
    const aVal = a[sortField];
    const bVal = b[sortField];
    if (aVal < bVal) return sortAsc ? -1 : 1;
    if (aVal > bVal) return sortAsc ? 1 : -1;
    return 0;
  });

  const counts = {
    total: prs.length,
    pending: prs.filter((p) => p.status === "Pending").length,
    approved: prs.filter((p) => p.status === "Approved").length,
    needsChanges: prs.filter((p) => p.status === "NeedsChanges").length,
  };

  function handleSort(field: SortField) {
    if (sortField === field) {
      setSortAsc(!sortAsc);
    } else {
      setSortField(field);
      setSortAsc(true);
    }
  }

  function sortIndicator(field: SortField) {
    if (sortField !== field) return "";
    return sortAsc ? " ↑" : " ↓";
  }

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">PR Queue</h2>

      <div className="grid grid-cols-4 gap-4 mb-6">
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold">{counts.total}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Total PRs</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold text-yellow-600">{counts.pending}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Pending</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold text-green-600">{counts.approved}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Approved</div>
        </div>
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
          <div className="text-2xl font-bold text-red-600">{counts.needsChanges}</div>
          <div className="text-sm text-gray-500 dark:text-gray-400">Needs Changes</div>
        </div>
      </div>

      <div className="mb-4">
        <select
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as FilterStatus)}
          className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm bg-white dark:bg-gray-800 dark:text-gray-100"
        >
          <option value="">All Statuses</option>
          <option value="Pending">Pending</option>
          <option value="Approved">Approved</option>
          <option value="NeedsChanges">Needs Changes</option>
        </select>
      </div>

      {sorted.length === 0 ? (
        <div className="text-gray-500 dark:text-gray-400 text-center py-8">No pull requests found.</div>
      ) : (
        <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
              <tr>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("prNumber")}
                >
                  PR #{sortIndicator("prNumber")}
                </th>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("repo")}
                >
                  Repository{sortIndicator("repo")}
                </th>
                <th
                  className="text-left px-4 py-3 font-medium cursor-pointer select-none"
                  onClick={() => handleSort("status")}
                >
                  Status{sortIndicator("status")}
                </th>
                <th className="text-left px-4 py-3 font-medium">Reviewed By</th>
              </tr>
            </thead>
            <tbody className="divide-y dark:divide-gray-700">
              {sorted.map((pr) => (
                <tr key={pr.id} className="hover:bg-gray-50 dark:hover:bg-gray-700">
                  <td className="px-4 py-3 font-mono">#{pr.prNumber}</td>
                  <td className="px-4 py-3">{pr.repo}</td>
                  <td className="px-4 py-3">
                    <StatusBadge status={pr.status} />
                  </td>
                  <td className="px-4 py-3 text-gray-500 dark:text-gray-400">{pr.reviewedBy ?? "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
