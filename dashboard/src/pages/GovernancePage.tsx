import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import type { GovernanceViolation, Session } from "../types";
import SeverityBadge from "../components/SeverityBadge";

type SeverityFilter = "" | GovernanceViolation["severity"];
type StatusFilter = "" | "resolved" | "unresolved";

interface ViolationWithSession extends GovernanceViolation {
  developerEmail: string;
}

export default function GovernancePage() {
  const [violations, setViolations] = useState<ViolationWithSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [severityFilter, setSeverityFilter] = useState<SeverityFilter>("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [resolveTargetId, setResolveTargetId] = useState<string | null>(null);
  const [resolveBy, setResolveBy] = useState("");
  const [resolveReason, setResolveReason] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    loadViolations();
  }, []);

  async function loadViolations() {
    try {
      setLoading(true);
      const sessions: Session[] = await api.sessions.list();
      const allViolations: ViolationWithSession[] = [];

      for (const session of sessions) {
        const sessionViolations = await api.governance.violations(session.id);
        for (const v of sessionViolations) {
          allViolations.push({ ...v, developerEmail: session.developerEmail });
        }
      }

      allViolations.sort(
        (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
      );
      setViolations(allViolations);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load violations");
    } finally {
      setLoading(false);
    }
  }

  async function handleResolve(violationId: string) {
    if (!resolveBy.trim() || !resolveReason.trim()) return;
    setSubmitting(true);
    try {
      await api.governance.resolve(violationId, {
        resolvedBy: resolveBy,
        resolutionReason: resolveReason,
      });
      setViolations((prev) =>
        prev.map((v) =>
          v.id === violationId
            ? { ...v, resolved: true, resolvedBy: resolveBy, resolutionReason: resolveReason, resolvedAt: new Date().toISOString() }
            : v,
        ),
      );
      setResolveTargetId(null);
      setResolveBy("");
      setResolveReason("");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to resolve violation");
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) return <div className="text-gray-500">Loading violations...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;

  const filtered = violations.filter((v) => {
    if (severityFilter && v.severity !== severityFilter) return false;
    if (statusFilter === "resolved" && !v.resolved) return false;
    if (statusFilter === "unresolved" && v.resolved) return false;
    return true;
  });

  const counts = {
    total: violations.length,
    critical: violations.filter((v) => v.severity === "Critical").length,
    error: violations.filter((v) => v.severity === "Error").length,
    warning: violations.filter((v) => v.severity === "Warning").length,
    info: violations.filter((v) => v.severity === "Info").length,
    unresolved: violations.filter((v) => !v.resolved).length,
  };

  return (
    <div>
      <h2 className="text-xl font-semibold mb-4">Governance Violations</h2>

      <div className="grid grid-cols-3 gap-4 mb-6 sm:grid-cols-6">
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold">{counts.total}</div>
          <div className="text-sm text-gray-500">Total</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-red-600">{counts.critical}</div>
          <div className="text-sm text-gray-500">Critical</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-red-500">{counts.error}</div>
          <div className="text-sm text-gray-500">Error</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-yellow-600">{counts.warning}</div>
          <div className="text-sm text-gray-500">Warning</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-blue-600">{counts.info}</div>
          <div className="text-sm text-gray-500">Info</div>
        </div>
        <div className="bg-white rounded-lg border p-4">
          <div className="text-2xl font-bold text-orange-600">{counts.unresolved}</div>
          <div className="text-sm text-gray-500">Unresolved</div>
        </div>
      </div>

      <div className="flex gap-3 mb-4">
        <select
          value={severityFilter}
          onChange={(e) => setSeverityFilter(e.target.value as SeverityFilter)}
          className="border rounded px-3 py-1.5 text-sm"
          aria-label="Filter by severity"
        >
          <option value="">All Severities</option>
          <option value="Critical">Critical</option>
          <option value="Error">Error</option>
          <option value="Warning">Warning</option>
          <option value="Info">Info</option>
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value as StatusFilter)}
          className="border rounded px-3 py-1.5 text-sm"
          aria-label="Filter by status"
        >
          <option value="">All Statuses</option>
          <option value="resolved">Resolved</option>
          <option value="unresolved">Unresolved</option>
        </select>
      </div>

      {filtered.length === 0 ? (
        <div className="text-gray-500 text-center py-8">No violations found.</div>
      ) : (
        <div className="space-y-2">
          {filtered.map((v) => (
            <div key={v.id} className="bg-white rounded-lg border">
              <div
                className={`flex items-start gap-3 p-4 cursor-pointer ${v.resolved ? "opacity-60" : ""}`}
                onClick={() => setExpandedId(expandedId === v.id ? null : v.id)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    setExpandedId(expandedId === v.id ? null : v.id);
                  }
                }}
              >
                <SeverityBadge severity={v.severity} />
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-medium text-sm">{v.ruleId}</span>
                    {v.resolved ? (
                      <span className="text-xs bg-green-100 text-green-800 px-1.5 py-0.5 rounded">
                        Resolved
                      </span>
                    ) : (
                      <span className="text-xs bg-red-100 text-red-800 px-1.5 py-0.5 rounded">
                        Unresolved
                      </span>
                    )}
                  </div>
                  <div className="text-sm text-gray-600 mt-1">{v.message}</div>
                  <div className="flex items-center gap-3 mt-1 text-xs text-gray-400">
                    <Link
                      to={`/sessions/${v.sessionId}`}
                      className="text-blue-600 hover:underline"
                      onClick={(e) => e.stopPropagation()}
                    >
                      {v.sessionId.slice(0, 8)}...
                    </Link>
                    <span>{new Date(v.createdAt).toLocaleString()}</span>
                    <span>{v.developerEmail}</span>
                  </div>
                </div>
                <span className="text-gray-400 text-sm">{expandedId === v.id ? "▼" : "▶"}</span>
              </div>

              {expandedId === v.id && (
                <div className="border-t px-4 py-3 bg-gray-50">
                  {v.contextJson && (
                    <div className="mb-3">
                      <div className="text-xs font-medium text-gray-500 mb-1">Context</div>
                      <pre className="text-xs bg-gray-900 text-gray-100 p-3 rounded overflow-x-auto max-h-40 overflow-y-auto">
                        {(() => {
                          try {
                            return JSON.stringify(JSON.parse(v.contextJson), null, 2);
                          } catch {
                            return v.contextJson;
                          }
                        })()}
                      </pre>
                    </div>
                  )}

                  {v.resolved && (
                    <div className="mb-3">
                      <div className="text-xs font-medium text-gray-500 mb-1">Resolution</div>
                      <div className="text-sm">
                        <span className="text-gray-500">Resolved by:</span> {v.resolvedBy}
                      </div>
                      <div className="text-sm">
                        <span className="text-gray-500">Reason:</span> {v.resolutionReason}
                      </div>
                      {v.resolvedAt && (
                        <div className="text-sm">
                          <span className="text-gray-500">At:</span>{" "}
                          {new Date(v.resolvedAt).toLocaleString()}
                        </div>
                      )}
                    </div>
                  )}

                  {!v.resolved && resolveTargetId !== v.id && (
                    <button
                      type="button"
                      onClick={() => setResolveTargetId(v.id)}
                      className="bg-green-600 text-white px-3 py-1.5 rounded text-sm hover:bg-green-700"
                    >
                      Resolve
                    </button>
                  )}

                  {!v.resolved && resolveTargetId === v.id && (
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        handleResolve(v.id);
                      }}
                      className="flex gap-2 items-end"
                    >
                      <div>
                        <label className="text-xs text-gray-500 block mb-1" htmlFor={`resolve-by-${v.id}`}>
                          Resolved By
                        </label>
                        <input
                          id={`resolve-by-${v.id}`}
                          type="email"
                          placeholder="your@email.com"
                          value={resolveBy}
                          onChange={(e) => setResolveBy(e.target.value)}
                          className="border rounded px-3 py-1.5 text-sm w-48"
                          required
                        />
                      </div>
                      <div className="flex-1">
                        <label className="text-xs text-gray-500 block mb-1" htmlFor={`resolve-reason-${v.id}`}>
                          Reason
                        </label>
                        <input
                          id={`resolve-reason-${v.id}`}
                          type="text"
                          placeholder="Resolution reason..."
                          value={resolveReason}
                          onChange={(e) => setResolveReason(e.target.value)}
                          className="border rounded px-3 py-1.5 text-sm w-full"
                          required
                        />
                      </div>
                      <button
                        type="submit"
                        disabled={submitting}
                        className="bg-green-600 text-white px-4 py-1.5 rounded text-sm hover:bg-green-700 disabled:opacity-50"
                      >
                        {submitting ? "Saving..." : "Submit"}
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setResolveTargetId(null);
                          setResolveBy("");
                          setResolveReason("");
                        }}
                        className="bg-gray-200 text-gray-700 px-3 py-1.5 rounded text-sm hover:bg-gray-300"
                      >
                        Cancel
                      </button>
                    </form>
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
