import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act } from "@testing-library/react";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import { render } from "../test/render";
import UsagePage from "./UsagePage";
import { api } from "../api";
import { buildRunningSession, buildUsageSummary } from "../test/usageFactories";
import { buildBudgetStatus, buildDeveloperWindow, buildPlanValue } from "../test/meterFactories";

vi.mock("../api", () => ({
  api: {
    usage: { summary: vi.fn(), running: vi.fn(), windows: vi.fn(), planValue: vi.fn() },
    budgets: { status: vi.fn() },
  },
}));

// Only the transport is faked: the throttles, intervals and coalescing under test are the real ones.
const { hub } = vi.hoisted(() => {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const connection = {
    on: (event: string, handler: (...args: unknown[]) => void) => handlers.set(event, handler),
    off: (event: string) => handlers.delete(event),
  };
  const emit = (event: string, ...args: unknown[]) => handlers.get(event)?.(...args);
  return { hub: { connection, emit } };
});

vi.mock("../hooks/useSignalR", () => ({
  useSignalR: () => ({
    connection: hub.connection as unknown as HubConnection,
    state: HubConnectionState.Connected,
    connectedEpoch: 1,
  }),
}));

const mockApi = vi.mocked(api, true);
const STEP_MS = 250;

function requestCount(): number {
  return [
    mockApi.usage.summary,
    mockApi.usage.running,
    mockApi.budgets.status,
    mockApi.usage.windows,
    mockApi.usage.planValue,
  ].reduce((sum, fn) => sum + fn.mock.calls.length, 0);
}

/** One minute of a busy fleet: usage every 250 ms, a session start and end every 5 s. */
async function streamOneMinute(): Promise<void> {
  for (let ms = 0; ms < 60_000; ms += STEP_MS) {
    act(() => {
      hub.emit("UsageUpdated", { sessionId: "s1", at: "2026-11-10T09:00:00Z" });
      if (ms % 5_000 === 0) hub.emit("SessionStarted", "s2", "dev@x.io", null);
      if (ms % 5_000 === 2_500) hub.emit("SessionEnded", "s2");
    });
    // Async advance lets each response settle between ticks, as a fast server would; a sync
    // advance would leave every load in flight and hide requests behind the coalescing.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(STEP_MS);
    });
  }
}

describe("UsagePage request budget", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2026, 10, 10, 9, 0));
    mockApi.usage.summary.mockResolvedValue(buildUsageSummary());
    mockApi.usage.running.mockResolvedValue([buildRunningSession()]);
    mockApi.budgets.status.mockResolvedValue([buildBudgetStatus()]);
    mockApi.usage.windows.mockResolvedValue([buildDeveloperWindow()]);
    mockApi.usage.planValue.mockResolvedValue([buildPlanValue()]);
  });

  afterEach(() => vi.useRealTimers());

  it("should send 15 requests a minute per tab under a constant event stream, budgets, windows and plan value included, so five open tabs stay well under the API's 100-per-minute limit", async () => {
    const perMinute: number[] = [];
    let counted = 0;
    render(<UsagePage />);
    for (let minute = 0; minute < 5; minute++) {
      await streamOneMinute();
      perMinute.push(requestCount() - counted);
      counted = requestCount();
    }

    // Minute one also carries the mount load and the first-connect resync.
    expect(perMinute).toEqual([24, 15, 15, 15, 15]);
  });
});
