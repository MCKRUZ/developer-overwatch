import type {
  SessionListItem,
  SessionDetail,
  SessionTimelinePage,
  DailyUsage,
  ModelUsage,
  PullRequestReview,
  PullRequestDetail,
  TeamAnalytics,
  DeveloperAnalytics,
  GovernanceViolation,
} from "./types";

const BASE = "/api";

let tokenGetter: (() => string | null) | null = null;

export function setTokenGetter(getter: () => string | null): void {
  tokenGetter = getter;
}

export function clearTokenGetter(): void {
  tokenGetter = null;
}

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const token = tokenGetter?.();
  const headers: Record<string, string> = {
    ...(init?.headers as Record<string, string> | undefined),
  };

  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  const finalInit: RequestInit | undefined =
    token || init ? { ...init, headers } : init;

  const res = await fetch(url, finalInit);
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`${res.status}: ${body}`);
  }
  return res.json() as Promise<T>;
}

export interface SessionListFilters {
  developer?: string;
  repo?: string;
  from?: string;
  to?: string;
  status?: SessionListItem["status"];
}

export const api = {
  sessions: {
    list(filters?: SessionListFilters): Promise<SessionListItem[]> {
      const params = new URLSearchParams();
      if (filters?.developer) params.set("developer", filters.developer);
      if (filters?.repo) params.set("repo", filters.repo);
      if (filters?.from) params.set("from", filters.from);
      if (filters?.to) params.set("to", filters.to);
      if (filters?.status) params.set("status", filters.status);
      const qs = params.toString();
      return fetchJson(`${BASE}/sessions${qs ? `?${qs}` : ""}`);
    },
    get(id: string): Promise<SessionDetail> {
      return fetchJson(`${BASE}/sessions/${id}`);
    },
    timeline(id: string, before?: string, limit = 50): Promise<SessionTimelinePage> {
      const params = new URLSearchParams();
      if (before) params.set("before", before);
      params.set("limit", String(limit));
      return fetchJson(`${BASE}/sessions/${id}/timeline?${params.toString()}`);
    },
    timelineAround(
      id: string,
      anchor: { turnId?: string; toolExecutionId?: string },
      limit = 50,
    ): Promise<SessionTimelinePage> {
      const params = new URLSearchParams();
      if (anchor.turnId) params.set("turnId", anchor.turnId);
      if (anchor.toolExecutionId) params.set("toolExecutionId", anchor.toolExecutionId);
      params.set("limit", String(limit));
      return fetchJson(`${BASE}/sessions/${id}/timeline/around?${params.toString()}`);
    },
    dailyUsage(id: string): Promise<DailyUsage[]> {
      return fetchJson(`${BASE}/sessions/${id}/daily-usage`);
    },
    modelBreakdown(id: string): Promise<ModelUsage[]> {
      return fetchJson(`${BASE}/sessions/${id}/model-breakdown`);
    },
  },

  pullRequests: {
    list(repo?: string): Promise<PullRequestReview[]> {
      const params = new URLSearchParams();
      if (repo) params.set("repo", repo);
      const qs = params.toString();
      return fetchJson(`${BASE}/prs${qs ? `?${qs}` : ""}`);
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
    team(from?: string, to?: string, developer?: string): Promise<TeamAnalytics> {
      const params = new URLSearchParams();
      if (from) params.set("from", from);
      if (to) params.set("to", to);
      if (developer) params.set("developer", developer);
      const qs = params.toString();
      return fetchJson(`${BASE}/analytics/team${qs ? `?${qs}` : ""}`);
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
