import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { api } from "../api";
import type { Session, GovernanceViolation } from "../types";
import StatusBadge from "../components/StatusBadge";
import SeverityBadge from "../components/SeverityBadge";

export default function SessionDetailPage() {
  const { sessionId } = useParams<{ sessionId: string }>();
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [annotationContent, setAnnotationContent] = useState("");
  const [annotationEmail, setAnnotationEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [violationsExpanded, setViolationsExpanded] = useState(false);
  const [fetchedViolations, setFetchedViolations] = useState<GovernanceViolation[]>([]);

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

  if (loading) return <div className="text-gray-500">Loading session...</div>;
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

  const turns = [...session.conversationTurns].sort((a, b) => a.turnIndex - b.turnIndex);
  const violations = fetchedViolations.length > 0 ? fetchedViolations : session.governanceViolations;
  const fileChanges = session.fileChanges;
  const annotations = session.architectAnnotations;

  const violationCounts = {
    total: violations.length,
    critical: violations.filter((v) => v.severity === "Critical").length,
    error: violations.filter((v) => v.severity === "Error").length,
    warning: violations.filter((v) => v.severity === "Warning").length,
    info: violations.filter((v) => v.severity === "Info").length,
  };

  function formatTime(iso: string): string {
    return new Date(iso).toLocaleTimeString();
  }

  return (
    <div>
      <div className="mb-6">
        <Link to="/sessions" className="text-blue-600 hover:underline text-sm">
          &larr; Back to Sessions
        </Link>
      </div>

      <div className="bg-white rounded-lg border p-6 mb-6">
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-xl font-semibold font-mono">{session.id.slice(0, 12)}...</h2>
            <p className="text-gray-500 mt-1">{session.developerEmail}</p>
          </div>
          <StatusBadge status={session.status} />
        </div>
        <div className="grid grid-cols-4 gap-4 mt-4 text-sm">
          <div>
            <span className="text-gray-500">Repo:</span>{" "}
            <span>{session.repoPath ?? "---"}</span>
          </div>
          <div>
            <span className="text-gray-500">Started:</span>{" "}
            <span>{new Date(session.startedAt).toLocaleString()}</span>
          </div>
          <div>
            <span className="text-gray-500">Ended:</span>{" "}
            <span>{session.endedAt ? new Date(session.endedAt).toLocaleString() : "In progress"}</span>
          </div>
          <div>
            <span className="text-gray-500">Turns:</span> <span>{turns.length}</span>
          </div>
        </div>
      </div>

      <div className="bg-white rounded-lg border mb-6">
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
          <span className="text-gray-400 text-sm">{violationsExpanded ? "▼" : "▶"}</span>
        </button>

        {violationsExpanded && violations.length > 0 && (
          <div className="border-t px-4 pb-4">
            <div className="space-y-2 mt-3">
              {violations.map((v) => (
                <div
                  key={v.id}
                  className={`flex items-start gap-3 p-3 rounded ${v.resolved ? "bg-gray-50 opacity-60" : "bg-red-50"}`}
                >
                  <SeverityBadge severity={v.severity} />
                  <div className="flex-1">
                    <div className="font-medium text-sm">{v.ruleId}</div>
                    <div className="text-sm text-gray-600">{v.message}</div>
                    {v.resolved && (
                      <div className="text-xs text-gray-400 mt-1">
                        Resolved by {v.resolvedBy}: {v.resolutionReason}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        {violationsExpanded && violations.length === 0 && (
          <div className="border-t px-4 py-3 text-sm text-gray-500">
            No violations recorded for this session.
          </div>
        )}
      </div>

      <div className="bg-white rounded-lg border p-4 mb-6">
        <h3 className="font-semibold mb-3">Conversation Timeline</h3>
        <div className="space-y-3">
          {turns.length === 0 ? (
            <div className="text-gray-500 text-sm">No conversation turns recorded.</div>
          ) : (
            turns.map((turn) => (
              <div
                key={turn.id}
                className={`p-3 rounded text-sm ${
                  turn.role === "user"
                    ? "bg-blue-50 border-l-4 border-blue-400"
                    : turn.role === "assistant"
                      ? "bg-gray-50 border-l-4 border-gray-400"
                      : "bg-yellow-50 border-l-4 border-yellow-400"
                } ${turn.isDecision ? "ring-2 ring-amber-300" : ""}`}
              >
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium capitalize">{turn.role}</span>
                  <span className="text-gray-400 text-xs">{formatTime(turn.timestamp)}</span>
                  {turn.isDecision && (
                    <span className="text-xs bg-amber-100 text-amber-800 px-1.5 py-0.5 rounded">
                      Decision
                    </span>
                  )}
                  {turn.tokenCount != null && (
                    <span className="text-xs text-gray-400">{turn.tokenCount} tokens</span>
                  )}
                </div>
                {turn.content && (
                  <pre className="whitespace-pre-wrap text-sm text-gray-700 max-h-40 overflow-y-auto">
                    {turn.content}
                  </pre>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {fileChanges.length > 0 && (
        <div className="bg-white rounded-lg border p-4 mb-6">
          <h3 className="font-semibold mb-3">File Changes ({fileChanges.length})</h3>
          <div className="space-y-2">
            {fileChanges.map((fc) => (
              <div key={fc.id} className="p-3 bg-gray-50 rounded">
                <div className="flex items-center gap-2 text-sm">
                  <span className="font-mono">{fc.filePath}</span>
                  {fc.changeType && (
                    <span
                      className={`text-xs px-1.5 py-0.5 rounded ${
                        fc.changeType === "Created"
                          ? "bg-green-100 text-green-700"
                          : fc.changeType === "Deleted"
                            ? "bg-red-100 text-red-700"
                            : "bg-blue-100 text-blue-700"
                      }`}
                    >
                      {fc.changeType}
                    </span>
                  )}
                </div>
                {fc.diffText && (
                  <pre className="mt-2 text-xs bg-gray-900 text-gray-100 p-3 rounded overflow-x-auto max-h-60 overflow-y-auto">
                    {fc.diffText}
                  </pre>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="bg-white rounded-lg border p-4 mb-6">
        <h3 className="font-semibold mb-3">
          Annotations {annotations.length > 0 && `(${annotations.length})`}
        </h3>
        {annotations.length > 0 && (
          <div className="space-y-2 mb-4">
            {annotations.map((a) => (
              <div key={a.id} className="p-3 bg-indigo-50 rounded text-sm">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium">{a.createdBy}</span>
                  <span className="text-gray-400 text-xs">
                    {new Date(a.createdAt).toLocaleString()}
                  </span>
                </div>
                <p className="text-gray-700">{a.content}</p>
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
            className="border rounded px-3 py-1.5 text-sm w-48"
            required
          />
          <input
            type="text"
            placeholder="Add annotation..."
            value={annotationContent}
            onChange={(e) => setAnnotationContent(e.target.value)}
            className="border rounded px-3 py-1.5 text-sm flex-1"
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
