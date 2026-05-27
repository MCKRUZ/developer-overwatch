import type {
  Session,
  PullRequestReview,
  PullRequestDetail,
  TeamAnalytics,
  DeveloperAnalytics,
  GovernanceViolation,
} from "./types";

const BASE = "/api";

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`${res.status}: ${body}`);
  }
  return res.json() as Promise<T>;
}

export const api = {
  sessions: {
    list(developer?: string, repo?: string): Promise<Session[]> {
      const params = new URLSearchParams();
      if (developer) params.set("developer", developer);
      if (repo) params.set("repo", repo);
      const qs = params.toString();
      return fetchJson(`${BASE}/sessions${qs ? `?${qs}` : ""}`);
    },
    get(id: string): Promise<Session> {
      return fetchJson(`${BASE}/sessions/${id}`);
    },
  },

  pullRequests: {
    list(repo?: string): Promise<PullRequestReview[]> {
      const qs = repo ? `?repo=${encodeURIComponent(repo)}` : "";
      return fetchJson(`${BASE}/prs${qs}`);
    },
    get(owner: string, repoName: string, prNumber: number): Promise<PullRequestDetail> {
      return fetchJson(`${BASE}/prs/${owner}/${repoName}/${prNumber}`);
    },
    review(
      owner: string,
      repoName: string,
      prNumber: number,
      body: { repo: string; prNumber: number; status: PullRequestReview["status"]; reviewedBy: string; notes?: string },
    ): Promise<{ reviewId: string }> {
      return fetchJson(`${BASE}/prs/${owner}/${repoName}/${prNumber}/review`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },

  analytics: {
    team(): Promise<TeamAnalytics> {
      return fetchJson(`${BASE}/analytics/team`);
    },
    developer(email: string): Promise<DeveloperAnalytics> {
      return fetchJson(`${BASE}/analytics/developer/${encodeURIComponent(email)}`);
    },
  },

  governance: {
    violations(sessionId: string, severity?: GovernanceViolation["severity"]): Promise<GovernanceViolation[]> {
      const params = new URLSearchParams();
      if (severity) params.set("severity", severity);
      const qs = params.toString();
      return fetchJson(`${BASE}/governance/violations/${sessionId}${qs ? `?${qs}` : ""}`);
    },
    resolve(
      violationId: string,
      body: { resolvedBy: string; resolutionReason: string },
    ): Promise<{ violationId: string }> {
      return fetchJson(`${BASE}/governance/violations/${violationId}/resolve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },

  annotations: {
    create(body: {
      sessionId: string;
      content: string;
      createdBy: string;
      turnId?: string;
      toolExecutionId?: string;
    }): Promise<{ annotationId: string }> {
      return fetchJson(`${BASE}/annotations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    },
  },
};
