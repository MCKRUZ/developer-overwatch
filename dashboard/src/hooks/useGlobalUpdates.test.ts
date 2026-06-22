import { renderHook, act } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import { useGlobalUpdates } from "./useGlobalUpdates";
import type { ViolationData } from "./useSessionUpdates";

function makeConnection() {
  const handlers = new Map<string, (...args: unknown[]) => void>();
  const connection = {
    on: vi.fn((event: string, handler: (...args: unknown[]) => void) => {
      handlers.set(event, handler);
    }),
    off: vi.fn((event: string) => {
      handlers.delete(event);
    }),
  } as unknown as HubConnection;
  const emit = (event: string, ...args: unknown[]) => handlers.get(event)?.(...args);
  return { connection, emit, handlers };
}

const violation: ViolationData = {
  id: "v1",
  sessionId: "s1",
  ruleId: "no-secrets",
  severity: "high",
  message: "secret detected",
};

describe("useGlobalUpdates", () => {
  it("does not subscribe when connection is null", () => {
    const { result } = renderHook(() =>
      useGlobalUpdates({ connection: null, connectionState: HubConnectionState.Connected }),
    );
    expect(result.current.counts.newSessions).toBe(0);
  });

  it("does not subscribe when not connected", () => {
    const { connection } = makeConnection();
    renderHook(() =>
      useGlobalUpdates({ connection, connectionState: HubConnectionState.Disconnected }),
    );
    expect(connection.on).not.toHaveBeenCalled();
  });

  it("increments newSessions and invokes callback on SessionStarted", () => {
    const { connection, emit } = makeConnection();
    const onSessionStarted = vi.fn();
    const { result } = renderHook(() =>
      useGlobalUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        onSessionStarted,
      }),
    );

    act(() => emit("SessionStarted", "s1", "dev@x.com", "/repo"));

    expect(result.current.counts.newSessions).toBe(1);
    expect(onSessionStarted).toHaveBeenCalledWith("s1", "dev@x.com", "/repo");
  });

  it("tracks endedSessions, newViolations, and resolvedViolations", () => {
    const { connection, emit } = makeConnection();
    const onSessionEnded = vi.fn();
    const onViolationCreated = vi.fn();
    const onViolationResolved = vi.fn();
    const { result } = renderHook(() =>
      useGlobalUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        onSessionEnded,
        onViolationCreated,
        onViolationResolved,
      }),
    );

    act(() => emit("SessionEnded", "s1"));
    act(() => emit("ViolationCreated", violation));
    act(() => emit("ViolationResolved", "v1"));

    expect(result.current.counts.endedSessions).toBe(1);
    expect(result.current.counts.newViolations).toBe(1);
    expect(result.current.counts.resolvedViolations).toBe(1);
    expect(onSessionEnded).toHaveBeenCalledWith("s1");
    expect(onViolationCreated).toHaveBeenCalledWith(violation);
    expect(onViolationResolved).toHaveBeenCalledWith("v1");
  });

  it("fires without optional callbacks", () => {
    const { connection, emit } = makeConnection();
    const { result } = renderHook(() =>
      useGlobalUpdates({ connection, connectionState: HubConnectionState.Connected }),
    );

    act(() => emit("SessionStarted", "s1", "dev@x.com", null));

    expect(result.current.counts.newSessions).toBe(1);
  });

  it("resetCounts clears all counters", () => {
    const { connection, emit } = makeConnection();
    const { result } = renderHook(() =>
      useGlobalUpdates({ connection, connectionState: HubConnectionState.Connected }),
    );

    act(() => emit("SessionStarted", "s1", "dev@x.com", null));
    act(() => result.current.resetCounts());

    expect(result.current.counts.newSessions).toBe(0);
  });

  it("unsubscribes on unmount", () => {
    const { connection } = makeConnection();
    const { unmount } = renderHook(() =>
      useGlobalUpdates({ connection, connectionState: HubConnectionState.Connected }),
    );

    unmount();

    expect(connection.off).toHaveBeenCalledWith("SessionStarted", expect.any(Function));
    expect(connection.off).toHaveBeenCalledWith("ViolationResolved", expect.any(Function));
  });
});
