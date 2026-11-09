import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
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
} from "recharts";
import { api } from "../api";
import type { SessionDetail, GovernanceViolation, DailyUsage, ModelUsage } from "../types";
import StatusBadge from "../components/StatusBadge";
import SeverityBadge from "../components/SeverityBadge";
import ChartCard from "../components/ChartCard";
import SessionTimeline from "../components/SessionTimeline";
import { formatTotalTokens, formatCost, tokenBreakdownTooltip } from "../utils/tokenFormat";
import { violationSessionLink } from "../utils/violationLink";

const PIE_PALETTE = ["#2563eb", "#7c3aed", "#16a34a", "#d97706", "#dc2626", "#0891b2"];
const CHART_TICK = { fontSize: 12, fill: "var(--chart-text)" };

function renderModelLabel(props: { name?: string; percent?: number }) {
  const { name, percent } = props;
  if (!name || percent == null) return "";
  return `${name} ${(percent * 100).toFixed(0)}%`;
}

export default function SessionDetailPage() {
  const { sessionId } = useParams<{ sessionId: string }>();
  const [session, setSession] = useState<SessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [annotationContent, setAnnotationContent] = useState("");
  const [annotationEmail, setAnnotationEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [violationsExpanded, setViolationsExpanded] = useState(false);
  const [fetchedViolations, setFetchedViolations] = useState<GovernanceViolation[]>([]);
  const [dailyUsage, setDailyUsage] = useState<DailyUsage[]>([]);
  const [modelBreakdown, setModelBreakdown] = useState<ModelUsage[]>([]);

  useEffect(() => {
    if (!sessionId) return;
    api.sessions
      .get(sessionId)
      .then(setSession)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, [sessionId]);

  useEffect(() => {
    if (!sessionId) return;
    api.governance
      .violations(sessionId)
      .then(setFetchedViolations)
      .catch(() => {});
  }, [sessionId]);

  useEffect(() => {
    if (!sessionId) return;
    api.sessions.dailyUsage(sessionId).then(setDailyUsage).catch(() => {});
    api.sessions.modelBreakdown(sessionId).then(setModelBreakdown).catch(() => {});
  }, [sessionId]);

  if (loading) return <div className="text-gray-500 dark:text-gray-400">Loading session...</div>;
  if (error) return <div className="text-red-600">Error: {error}</div>;
  if (!session) return <div className="text-red-600">Session not found</div>;

  async function handleAddAnnotation(e: React.FormEvent) {
    e.preventDefault();
    if (!sessionId || !annotationContent.trim() || !annotationEmail.trim()) return;
    setSubmitting(true);
    try {
      await api.annotations.create({
        sessionId,
        content: annotationContent,
        createdBy: annotationEmail,
      });
      const updated = await api.sessions.get(sessionId);
      setSession(updated);
      setAnnotationContent("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add annotation");
    } finally {
      setSubmitting(false);
    }
  }

  const violations = fetchedViolations.length > 0 ? fetchedViolations : session.governanceViolations;
  const annotations = session.architectAnnotations;

  const violationCounts = {
    total: violations.length,
    critical: violations.filter((v) => v.severity === "Critical").length,
    error: violations.filter((v) => v.severity === "Error").length,
    warning: violations.filter((v) => v.severity === "Warning").length,
    info: violations.filter((v) => v.severity === "Info").length,
  };

  return (
    <div>
      <div className="mb-6">
        <Link to="/sessions" className="text-blue-600 dark:text-blue-400 hover:underline text-sm">
          &larr; Back to Sessions
        </Link>
      </div>

      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-6 mb-6">
        <div className="flex items-start justify-between">
          <div>
            <h2
              className={`text-xl font-semibold ${session.title ? "" : "font-mono"}`}
              title={session.id}
            >
              {session.title || `${session.id.slice(0, 12)}...`}
            </h2>
            {session.title && (
              <p className="text-xs text-gray-400 dark:text-gray-500 font-mono">{session.id}</p>
            )}
            <p className="text-gray-500 dark:text-gray-400 mt-1">{session.developerEmail}</p>
          </div>
          <StatusBadge status={session.status} />
        </div>
        <div className="grid grid-cols-4 gap-4 mt-4 text-sm">
          <div>
            <span className="text-gray-500 dark:text-gray-400">Repo:</span>{" "}
            <span>{session.repoPath ?? "---"}</span>
          </div>
          <div>
            <span className="text-gray-500 dark:text-gray-400">Started:</span>{" "}
            <span>{new Date(session.startedAt).toLocaleString()}</span>
          </div>
          <div>
            <span className="text-gray-500 dark:text-gray-400">Ended:</span>{" "}
            <span>{session.endedAt ? new Date(session.endedAt).toLocaleString() : "In progress"}</span>
          </div>
          <div>
            <span className="text-gray-500 dark:text-gray-400">Turns:</span>{" "}
            <span>{session.conversationTurnCount}</span>
          </div>
        </div>
        <div className="grid grid-cols-4 gap-4 mt-3 text-sm">
          <div>
            <span className="text-gray-500 dark:text-gray-400">Total Tokens:</span>{" "}
            <span title={tokenBreakdownTooltip(session)}>
              {formatTotalTokens({
                inputTokens: session.totalInputTokens,
                outputTokens: session.totalOutputTokens,
                cacheCreationInputTokens: session.totalCacheCreationInputTokens,
                cacheReadInputTokens: session.totalCacheReadInputTokens,
              })}
            </span>
          </div>
          <div>
            <span className="text-gray-500 dark:text-gray-400">Est. Cost:</span>{" "}
            <span>{formatCost(session.estimatedCostUsd)}</span>
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 mb-6">
        <button
          type="button"
          onClick={() => setViolationsExpanded(!violationsExpanded)}
          className="w-full flex items-center justify-between p-4 text-left"
        >
          <div className="flex items-center gap-3">
            <h3 className="font-semibold">Governance Violations ({violationCounts.total})</h3>
            {violationCounts.total > 0 && (
              <div className="flex gap-1.5">
                {violationCounts.critical > 0 && (
                  <span className="text-xs bg-red-600 text-white px-1.5 py-0.5 rounded">
                    {violationCounts.critical} Critical
                  </span>
                )}
                {violationCounts.error > 0 && (
                  <span className="text-xs bg-red-100 text-red-800 px-1.5 py-0.5 rounded">
                    {violationCounts.error} Error
                  </span>
                )}
                {violationCounts.warning > 0 && (
                  <span className="text-xs bg-yellow-100 text-yellow-800 px-1.5 py-0.5 rounded">
                    {violationCounts.warning} Warning
                  </span>
                )}
                {violationCounts.info > 0 && (
                  <span className="text-xs bg-blue-100 text-blue-800 px-1.5 py-0.5 rounded">
                    {violationCounts.info} Info
                  </span>
                )}
              </div>
            )}
          </div>
          <span className="text-gray-400 dark:text-gray-500 text-sm">{violationsExpanded ? "▼" : "▶"}</span>
        </button>

        {violationsExpanded && violations.length > 0 && (
          <div className="border-t dark:border-gray-700 px-4 pb-4">
            <div className="space-y-2 mt-3">
              {violations.map((v) => (
                <div
                  key={v.id}
                  className={`flex items-start gap-3 p-3 rounded ${v.resolved ? "bg-gray-50 dark:bg-gray-700 opacity-60" : "bg-red-50 dark:bg-red-900/20"}`}
                >
                  <SeverityBadge severity={v.severity} />
                  <div className="flex-1">
                    <div className="font-medium text-sm">{v.ruleId}</div>
                    <div className="text-sm text-gray-600 dark:text-gray-300">{v.message}</div>
                    {v.resolved && (
                      <div className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                        Resolved by {v.resolvedBy}: {v.resolutionReason}
                      </div>
                    )}
                  </div>
                  {(v.conversationTurnId || v.toolExecutionId) && (
                    <Link
                      to={violationSessionLink(v)}
                      className="text-xs text-blue-600 dark:text-blue-400 hover:underline whitespace-nowrap"
                    >
                      Jump to issue
                    </Link>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {violationsExpanded && violations.length === 0 && (
          <div className="border-t dark:border-gray-700 px-4 py-3 text-sm text-gray-500 dark:text-gray-400">
            No violations recorded for this session.
          </div>
        )}
      </div>

      {(dailyUsage.length > 0 || modelBreakdown.length > 0) && (
        <div className="grid grid-cols-2 gap-4 mb-6">
          {dailyUsage.length > 0 && (
            <ChartCard title="Cost by Day">
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={dailyUsage}>
                  <CartesianGrid strokeDasharray="3 3" stroke="var(--chart-grid)" />
                  <XAxis dataKey="date" tick={CHART_TICK} />
                  <YAxis tick={CHART_TICK} tickFormatter={(v: number) => `$${v}`} />
                  <Tooltip formatter={(value) => formatCost(Number(value))} />
                  <Bar dataKey="estimatedCostUsd" name="Cost (USD)" fill="#7c3aed" />
                </BarChart>
              </ResponsiveContainer>
            </ChartCard>
          )}

          {modelBreakdown.length === 1 && modelBreakdown[0] && (
            <ChartCard title="Cost by Model">
              {/* A single model is 100% of the cost by definition — a one-slice pie has nothing
                  to compare against and just wastes space, so show the number directly instead. */}
              <div className="h-[220px] flex flex-col items-center justify-center gap-1">
                <div className="text-sm text-gray-500 dark:text-gray-400">{modelBreakdown[0].model}</div>
                <div className="text-2xl font-semibold">{formatCost(modelBreakdown[0].estimatedCostUsd)}</div>
                <div className="text-xs text-gray-400 dark:text-gray-500">100% of this session's cost</div>
              </div>
            </ChartCard>
          )}

          {modelBreakdown.length > 1 && (
            <ChartCard title="Cost by Model">
              <ResponsiveContainer width="100%" height={220}>
                <PieChart>
                  <Pie
                    data={modelBreakdown}
                    dataKey="estimatedCostUsd"
                    nameKey="model"
                    cx="50%"
                    cy="50%"
                    outerRadius={80}
                    label={renderModelLabel}
                  >
                    {modelBreakdown.map((entry, index) => (
                      <Cell key={entry.model} fill={PIE_PALETTE[index % PIE_PALETTE.length]} />
                    ))}
                  </Pie>
                  <Tooltip formatter={(value) => formatCost(Number(value))} />
                </PieChart>
              </ResponsiveContainer>
            </ChartCard>
          )}
        </div>
      )}

      <div className="mb-6">
        <SessionTimeline sessionId={session.id} />
      </div>

      <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4 mb-6">
        <h3 className="font-semibold mb-3">
          Annotations {annotations.length > 0 && `(${annotations.length})`}
        </h3>
        {annotations.length > 0 && (
          <div className="space-y-2 mb-4">
            {annotations.map((a) => (
              <div key={a.id} className="p-3 bg-indigo-50 dark:bg-indigo-900/20 rounded text-sm">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium">{a.createdBy}</span>
                  <span className="text-gray-400 dark:text-gray-500 text-xs">
                    {new Date(a.createdAt).toLocaleString()}
                  </span>
                </div>
                <p className="text-gray-700 dark:text-gray-300">{a.content}</p>
              </div>
            ))}
          </div>
        )}
        <form onSubmit={handleAddAnnotation} className="flex gap-2">
          <input
            type="email"
            placeholder="Your email"
            value={annotationEmail}
            onChange={(e) => setAnnotationEmail(e.target.value)}
            className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm w-48 bg-white dark:bg-gray-700 dark:text-gray-100"
            required
          />
          <input
            type="text"
            placeholder="Add annotation..."
            value={annotationContent}
            onChange={(e) => setAnnotationContent(e.target.value)}
            className="border dark:border-gray-600 rounded px-3 py-1.5 text-sm flex-1 bg-white dark:bg-gray-700 dark:text-gray-100"
            required
          />
          <button
            type="submit"
            disabled={submitting}
            className="bg-indigo-600 text-white px-4 py-1.5 rounded text-sm hover:bg-indigo-700 disabled:opacity-50"
          >
            {submitting ? "Adding..." : "Add"}
          </button>
        </form>
      </div>
    </div>
  );
}
