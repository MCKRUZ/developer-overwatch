import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { api } from "./api";

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

  describe("error handling", () => {
    it("throws on non-ok response with status and body", async () => {
      globalThis.fetch = mockFetch("Not Found", false, 404, "Not Found");
      await expect(api.sessions.list()).rejects.toThrow("404: Not Found");
    });

    it("propagates network errors", async () => {
      globalThis.fetch = vi.fn().mockRejectedValue(new TypeError("Failed to fetch"));
      await expect(api.sessions.list()).rejects.toThrow("Failed to fetch");
    });
  });
});
