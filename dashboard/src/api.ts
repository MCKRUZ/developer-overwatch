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
  PricingInfo,
  UsageGranularity,
  UsageSummary,
  RunningSession,
  Budget,
  BudgetPeriod,
  BudgetScope,
  BudgetStatus,
  CurrentUser,
  DeveloperPlan,
  DeveloperWindow,
  PlanValue,
} from "./types";

const BASE = "/api";

let tokenGetter: (() => string | null) | null = null;

export function setTokenGetter(getter: () => string | null): void {
  tokenGetter = getter;
}

export function clearTokenGetter(): void {
  tokenGetter = null;
}

/** The signed-in user's current bearer token, or null when nobody is signed in. */
export function getAccessToken(): string | null {
  return tokenGetter?.() ?? null;
}

/** A non-2xx response. `body` is the raw response text, e.g. problem details JSON. */
export class ApiError extends Error {
  readonly status: number;
  readonly body: string;

  constructor(status: number, body: string) {
    super(`${status}: ${body}`);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
  }
}

async function send(url: string, init?: RequestInit): Promise<Response> {
  const token = getAccessToken();
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
    throw new ApiError(res.status, await res.text());
  }
  return res;
}

async function fetchJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await send(url, init);
  return res.json() as Promise<T>;
}

/** For 204 responses, which carry no body to parse. */
async function fetchNoContent(url: string, init: RequestInit): Promise<void> {
  await send(url, init);
}

function jsonBody(method: "POST" | "PUT", body: unknown): RequestInit {
  return { method, headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) };
}

function viewerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

export interface CreateBudgetRequest {
  scope: BudgetScope;
  /** Required for Developer scope, null for Team. */
  developerEmail: string | null;
  period: BudgetPeriod;
  amountUsd: number;
}

export interface UpdateDeveloperPlanRequest {
  planName: string;
  monthlyPriceUsd: number;
  windowLimitUsd: number | null;
}

export interface CreateDeveloperPlanRequest extends UpdateDeveloperPlanRequest {
  developerEmail: string;
}

export interface SessionListFilters {
  developer?: string;
  repo?: string;
  from?: string;
  to?: string;
  status?: SessionListItem["status"];
}

export interface UsageSummaryParams {
  /** Local dates (YYYY-MM-DD), both inclusive. */
  from: string;
  to: string;
  granularity: UsageGranularity;
  developer?: string;
  project?: string;
}

/** The running list depends on who and which project, never on the period. */
export type UsageRunningFilters = Pick<UsageSummaryParams, "developer" | "project">;

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

  usage: {
    summary(params: UsageSummaryParams): Promise<UsageSummary> {
      const qs = new URLSearchParams({
        from: params.from,
        to: params.to,
        granularity: params.granularity,
        timeZone: viewerTimeZone(),
      });
      if (params.developer) qs.set("developer", params.developer);
      if (params.project) qs.set("project", params.project);
      return fetchJson(`${BASE}/usage/summary?${qs.toString()}`);
    },
    running(filters: UsageRunningFilters = {}): Promise<RunningSession[]> {
      const qs = new URLSearchParams();
      if (filters.developer) qs.set("developer", filters.developer);
      if (filters.project) qs.set("project", filters.project);
      const query = qs.toString();
      return fetchJson(`${BASE}/usage/running${query ? `?${query}` : ""}`);
    },
    windows(): Promise<DeveloperWindow[]> {
      return fetchJson(`${BASE}/usage/windows`);
    },
    planValue(): Promise<PlanValue[]> {
      const qs = new URLSearchParams({ tz: viewerTimeZone() });
      return fetchJson(`${BASE}/usage/plan-value?${qs.toString()}`);
    },
  },

  budgets: {
    list(): Promise<Budget[]> {
      return fetchJson(`${BASE}/budgets`);
    },
    status(): Promise<BudgetStatus[]> {
      const qs = new URLSearchParams({ tz: viewerTimeZone() });
      return fetchJson(`${BASE}/budgets/status?${qs.toString()}`);
    },
    create(body: CreateBudgetRequest): Promise<Budget> {
      return fetchJson(`${BASE}/budgets`, jsonBody("POST", body));
    },
    update(id: string, amountUsd: number): Promise<Budget> {
      return fetchJson(`${BASE}/budgets/${encodeURIComponent(id)}`, jsonBody("PUT", { amountUsd }));
    },
    remove(id: string): Promise<void> {
      return fetchNoContent(`${BASE}/budgets/${encodeURIComponent(id)}`, { method: "DELETE" });
    },
  },

  developerPlans: {
    list(): Promise<DeveloperPlan[]> {
      return fetchJson(`${BASE}/developer-plans`);
    },
    create(body: CreateDeveloperPlanRequest): Promise<DeveloperPlan> {
      return fetchJson(`${BASE}/developer-plans`, jsonBody("POST", body));
    },
    update(id: string, body: UpdateDeveloperPlanRequest): Promise<DeveloperPlan> {
      return fetchJson(`${BASE}/developer-plans/${encodeURIComponent(id)}`, jsonBody("PUT", body));
    },
    remove(id: string): Promise<void> {
      return fetchNoContent(`${BASE}/developer-plans/${encodeURIComponent(id)}`, { method: "DELETE" });
    },
  },

  auth: {
    me(): Promise<CurrentUser> {
      return fetchJson(`${BASE}/auth/me`);
    },
  },

  pricing: {
    get(): Promise<PricingInfo> {
      return fetchJson(`${BASE}/pricing`);
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
