import { describe, it, expect, vi, afterEach } from "vitest";
import { api } from "./api";

function mockFetch(body: unknown) {
  return vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    statusText: "OK",
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  });
}

describe("api.sessions session-level reads", () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it("should read the context, prompts and subagent split from their own session routes", async () => {
    globalThis.fetch = mockFetch([]);
    await api.sessions.context("s1");
    await api.sessions.prompts("s1");
    await api.sessions.subagentBreakdown("s1");
    expect(globalThis.fetch).toHaveBeenNthCalledWith(1, "/api/sessions/s1/context", undefined);
    expect(globalThis.fetch).toHaveBeenNthCalledWith(2, "/api/sessions/s1/prompts", undefined);
    expect(globalThis.fetch).toHaveBeenNthCalledWith(3, "/api/sessions/s1/subagent-breakdown", undefined);
  });

  it("should escape the session id so it cannot change the route", async () => {
    globalThis.fetch = mockFetch({});
    await api.sessions.context("a/b");
    expect(globalThis.fetch).toHaveBeenCalledWith("/api/sessions/a%2Fb/context", undefined);
  });
});
