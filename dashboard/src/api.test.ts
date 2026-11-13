import { describe, it, expect, vi, afterEach } from "vitest";
import { api, ApiError, setTokenGetter, clearTokenGetter, getAccessToken } from "./api";

function mockFetch(body: unknown, ok = true, status = 200, statusText = "OK") {
  return vi.fn().mockResolvedValue({
    ok,
    status,
    statusText,
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(typeof body === "string" ? body : JSON.stringify(body)),
  });
}

describe("api", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  describe("sessions", () => {
    it("list() calls GET /api/sessions with no params", async () => {
      const data = [{ id: "s1" }];
      globalThis.fetch = mockFetch(data);
      const result = await api.sessions.list();
      expect(result).toEqual(data);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions", undefined);
    });

    it("list(filters) appends query params", async () => {
      globalThis.fetch = mockFetch([]);
      await api.sessions.list({ developer: "dev@test.com", repo: "org/repo" });
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/sessions?developer=dev%40test.com&repo=org%2Frepo",
        undefined,
      );
    });

    it("should send every list filter so the server, not the browser, narrows the session list", async () => {
      globalThis.fetch = mockFetch([]);
      await api.sessions.list({
        developer: "d@x.io",
        repo: "r",
        from: "2026-11-01T00:00:00Z",
        to: "2026-11-02T00:00:00Z",
        status: "Active",
      });
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/sessions?developer=d%40x.io&repo=r&from=2026-11-01T00%3A00%3A00Z&to=2026-11-02T00%3A00%3A00Z&status=Active",
        undefined,
      );
    });

    it("should page the timeline backwards from a cursor only when one is given", async () => {
      globalThis.fetch = mockFetch({ items: [] });
      await api.sessions.timeline("s1");
      await api.sessions.timeline("s1", "2026-11-01T10:00:00Z", 20);
      expect(globalThis.fetch).toHaveBeenNthCalledWith(1, "/api/sessions/s1/timeline?limit=50", undefined);
      expect(globalThis.fetch).toHaveBeenNthCalledWith(
        2,
        "/api/sessions/s1/timeline?before=2026-11-01T10%3A00%3A00Z&limit=20",
        undefined,
      );
    });

    it("should anchor the timeline on whichever item a governance link points at", async () => {
      globalThis.fetch = mockFetch({ items: [] });
      await api.sessions.timelineAround("s1", { turnId: "t1" });
      await api.sessions.timelineAround("s1", { toolExecutionId: "x1" }, 10);
      expect(globalThis.fetch).toHaveBeenNthCalledWith(
        1,
        "/api/sessions/s1/timeline/around?turnId=t1&limit=50",
        undefined,
      );
      expect(globalThis.fetch).toHaveBeenNthCalledWith(
        2,
        "/api/sessions/s1/timeline/around?toolExecutionId=x1&limit=10",
        undefined,
      );
    });

    it("should read a session's daily usage and model breakdown from their own endpoints", async () => {
      globalThis.fetch = mockFetch([]);
      await api.sessions.dailyUsage("s1");
      await api.sessions.modelBreakdown("s1");
      expect(globalThis.fetch).toHaveBeenNthCalledWith(1, "/api/sessions/s1/daily-usage", undefined);
      expect(globalThis.fetch).toHaveBeenNthCalledWith(2, "/api/sessions/s1/model-breakdown", undefined);
    });

    it("get(id) calls GET /api/sessions/{id}", async () => {
      const session = { id: "sess-123" };
      globalThis.fetch = mockFetch(session);
      const result = await api.sessions.get("sess-123");
      expect(result).toEqual(session);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions/sess-123", undefined);
    });
  });

  describe("pullRequests", () => {
    it("list() calls GET /api/prs with no params", async () => {
      globalThis.fetch = mockFetch([]);
      await api.pullRequests.list();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs", undefined);
    });

    it("list(repo) appends repo param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.pullRequests.list("org/repo");
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs?repo=org%2Frepo", undefined);
    });

    it("get(owner, repo, pr) calls GET /api/prs/{owner}/{repo}/{pr}", async () => {
      globalThis.fetch = mockFetch({ id: "pr-1" });
      await api.pullRequests.get("myorg", "myrepo", 42);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs/myorg/myrepo/42", undefined);
    });

    it("review() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ reviewId: "r1" });
      const body = { repo: "org/repo", prNumber: 42, status: "Approved" as const, reviewedBy: "me@test.com" };
      await api.pullRequests.review("myorg", "myrepo", 42, body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/prs/myorg/myrepo/42/review", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("analytics", () => {
    it("team() calls GET /api/analytics/team", async () => {
      globalThis.fetch = mockFetch({ totalSessions: 10 });
      await api.analytics.team();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/analytics/team", undefined);
    });

    it("should pass the team date range and developer through to the server", async () => {
      globalThis.fetch = mockFetch({});
      await api.analytics.team("2026-11-01", "2026-11-30", "d@x.io");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/analytics/team?from=2026-11-01&to=2026-11-30&developer=d%40x.io",
        undefined,
      );
    });

    it("developer(email) encodes email in URL", async () => {
      globalThis.fetch = mockFetch({ email: "dev@test.com" });
      await api.analytics.developer("dev@test.com");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/analytics/developer/dev%40test.com",
        undefined,
      );
    });
  });

  describe("governance", () => {
    it("violations(sessionId) calls GET with no severity param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.governance.violations("sess-1");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/governance/violations/sess-1",
        undefined,
      );
    });

    it("violations(sessionId, severity) appends severity param", async () => {
      globalThis.fetch = mockFetch([]);
      await api.governance.violations("sess-1", "Critical");
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/governance/violations/sess-1?severity=Critical",
        undefined,
      );
    });

    it("resolve() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ violationId: "v1" });
      const body = { resolvedBy: "admin@test.com", resolutionReason: "False positive" };
      await api.governance.resolve("viol-1", body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/governance/violations/viol-1/resolve", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("annotations", () => {
    it("create() sends POST with JSON body", async () => {
      globalThis.fetch = mockFetch({ annotationId: "a1" });
      const body = { sessionId: "s1", content: "Good work", createdBy: "lead@test.com" };
      await api.annotations.create(body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/annotations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("usage", () => {
    afterEach(() => vi.restoreAllMocks());

    it("should send the viewer's IANA time zone, because the server buckets days in the viewer's local time", async () => {
      vi.spyOn(Intl.DateTimeFormat.prototype, "resolvedOptions").mockReturnValue({
        timeZone: "America/New_York",
      } as Intl.ResolvedDateTimeFormatOptions);
      globalThis.fetch = mockFetch({});
      await api.usage.summary({ from: "2026-11-01", to: "2026-11-07", granularity: "day" });
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/usage/summary?from=2026-11-01&to=2026-11-07&granularity=day&timeZone=America%2FNew_York",
        undefined,
      );
    });

    it("should pass the developer and project filters only when they are set", async () => {
      vi.spyOn(Intl.DateTimeFormat.prototype, "resolvedOptions").mockReturnValue({
        timeZone: "Asia/Kolkata",
      } as Intl.ResolvedDateTimeFormatOptions);
      globalThis.fetch = mockFetch({});
      await api.usage.summary({
        from: "2026-11-01",
        to: "2026-11-30",
        granularity: "week",
        developer: "d@x.io",
        project: "overwatch",
      });
      expect(globalThis.fetch).toHaveBeenCalledWith(
        "/api/usage/summary?from=2026-11-01&to=2026-11-30&granularity=week&timeZone=Asia%2FKolkata&developer=d%40x.io&project=overwatch",
        undefined,
      );
    });

    it("should list running sessions from /api/usage/running", async () => {
      globalThis.fetch = mockFetch([{ id: "s1" }]);
      await expect(api.usage.running()).resolves.toEqual([{ id: "s1" }]);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/usage/running", undefined);
    });

    it("should pass the developer and project filters to the running list, so it matches the filtered totals", async () => {
      globalThis.fetch = mockFetch([]);
      await api.usage.running({ developer: "alice@x.io", project: "over watch" });
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/usage/running?developer=alice%40x.io&project=over+watch", undefined);
    });
  });

  describe("meter reads", () => {
    afterEach(() => vi.restoreAllMocks());

    function useZone(timeZone: string) {
      vi.spyOn(Intl.DateTimeFormat.prototype, "resolvedOptions").mockReturnValue({
        timeZone,
      } as Intl.ResolvedDateTimeFormatOptions);
    }

    it("should ask for budget status in the viewer's time zone, because daily and monthly periods are the viewer's local ones", async () => {
      useZone("America/New_York");
      globalThis.fetch = mockFetch([]);
      await api.budgets.status();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/budgets/status?tz=America%2FNew_York", undefined);
    });

    it("should ask for plan value in the viewer's time zone, because month to date is the viewer's local month", async () => {
      useZone("Asia/Kolkata");
      globalThis.fetch = mockFetch([]);
      await api.usage.planValue();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/usage/plan-value?tz=Asia%2FKolkata", undefined);
    });

    it("should read 5-hour windows from /api/usage/windows", async () => {
      globalThis.fetch = mockFetch([{ developerEmail: "a@x.io" }]);
      await expect(api.usage.windows()).resolves.toEqual([{ developerEmail: "a@x.io" }]);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/usage/windows", undefined);
    });

    it("should read the signed-in user's edit rights from /api/auth/me", async () => {
      globalThis.fetch = mockFetch({ canEditBudgets: true });
      await expect(api.auth.me()).resolves.toEqual({ canEditBudgets: true });
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/auth/me", undefined);
    });
  });

  describe("budgets", () => {
    it("should list budgets", async () => {
      globalThis.fetch = mockFetch([]);
      await api.budgets.list();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/budgets", undefined);
    });

    it("should create a budget with a JSON body", async () => {
      globalThis.fetch = mockFetch({ id: "b1" });
      const body = { scope: "Team" as const, developerEmail: null, period: "Daily" as const, amountUsd: 50 };
      await expect(api.budgets.create(body)).resolves.toEqual({ id: "b1" });
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/budgets", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });

    it("should send only the amount when a budget is edited, since scope and period identify it", async () => {
      globalThis.fetch = mockFetch({ id: "b 1" });
      await api.budgets.update("b 1", 75);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/budgets/b%201", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ amountUsd: 75 }),
      });
    });

    it("should delete a budget without reading a body from the 204", async () => {
      const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 204, json: () => Promise.reject(new Error("empty")) });
      globalThis.fetch = fetchMock;
      await expect(api.budgets.remove("b1")).resolves.toBeUndefined();
      expect(fetchMock).toHaveBeenCalledWith("/api/budgets/b1", expect.objectContaining({ method: "DELETE" }));
    });
  });

  describe("developer plans", () => {
    it("should list developer plans", async () => {
      globalThis.fetch = mockFetch([]);
      await api.developerPlans.list();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/developer-plans", undefined);
    });

    it("should create a plan with a JSON body", async () => {
      globalThis.fetch = mockFetch({ id: "p1" });
      const body = { developerEmail: "a@x.io", planName: "Max", monthlyPriceUsd: 200, windowLimitUsd: null };
      await api.developerPlans.create(body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/developer-plans", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });

    it("should edit a plan without its email, which identifies it", async () => {
      globalThis.fetch = mockFetch({ id: "p1" });
      const body = { planName: "Pro", monthlyPriceUsd: 20, windowLimitUsd: 12.5 };
      await api.developerPlans.update("p1", body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/developer-plans/p1", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
    });

    it("should delete a plan", async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 204 });
      await api.developerPlans.remove("p1");
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/developer-plans/p1", expect.objectContaining({ method: "DELETE" }));
    });

    it("should reject a failed delete with the status, so the page can say the plan was already gone", async () => {
      globalThis.fetch = mockFetch("gone", false, 404, "Not Found");
      await expect(api.developerPlans.remove("p1")).rejects.toMatchObject({ status: 404, body: "gone" });
    });
  });

  describe("pricing", () => {
    it("should read the price list's as-of date from /api/pricing", async () => {
      globalThis.fetch = mockFetch({ asOf: "2026-11-01" });
      await expect(api.pricing.get()).resolves.toEqual({ asOf: "2026-11-01" });
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/pricing", undefined);
    });
  });

  describe("bearer token", () => {
    afterEach(() => clearTokenGetter());

    it("should attach the signed-in user's token, because every API route requires it when sign-in is on", async () => {
      setTokenGetter(() => "tok-1");
      globalThis.fetch = mockFetch([]);
      await api.sessions.list();
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions", {
        headers: { Authorization: "Bearer tok-1" },
      });
    });

    it("should keep the request's own headers and body when it adds the token to a POST", async () => {
      setTokenGetter(() => "tok-1");
      globalThis.fetch = mockFetch({ annotationId: "a1" });
      const body = { sessionId: "s1", content: "c", createdBy: "me" };
      await api.annotations.create(body);
      expect(globalThis.fetch).toHaveBeenCalledWith("/api/annotations", {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: "Bearer tok-1" },
        body: JSON.stringify(body),
      });
    });
  });

  describe("error handling", () => {
    it("throws on non-ok response with status and body", async () => {
      globalThis.fetch = mockFetch("Not Found", false, 404, "Not Found");
      await expect(api.sessions.list()).rejects.toThrow("404: Not Found");
    });

    it("should carry the status and raw body, so a caller can tell a conflict from a validation failure", async () => {
      globalThis.fetch = mockFetch({ title: "dup" }, false, 409, "Conflict");
      const error = await api.budgets.list().catch((e: unknown) => e);
      expect(error).toBeInstanceOf(ApiError);
      expect(error).toMatchObject({ status: 409, body: JSON.stringify({ title: "dup" }) });
    });

    it("propagates network errors", async () => {
      globalThis.fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
      await expect(api.sessions.list()).rejects.toThrow("Failed to fetch");
    });
  });
});

describe("getAccessToken", () => {
  afterEach(() => clearTokenGetter());

  it("should return null when no sign-in has registered a token, so callers connect anonymously", () => {
    expect(getAccessToken()).toBeNull();
  });

  it("should return the current token so the hub's token factory picks up silent renewals", () => {
    let token = "first";
    setTokenGetter(() => token);
    expect(getAccessToken()).toBe("first");
    token = "renewed";
    expect(getAccessToken()).toBe("renewed");
  });

  it("should return null again after sign-out clears the getter", () => {
    setTokenGetter(() => "t");
    clearTokenGetter();
    expect(getAccessToken()).toBeNull();
  });
});
