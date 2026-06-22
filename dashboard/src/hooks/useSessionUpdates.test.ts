import { renderHook, act } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";
import type { HubConnection } from "@microsoft/signalr";
import { useSessionUpdates } from "./useSessionUpdates";
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
    invoke: vi.fn().mockResolvedValue(undefined),
  } as unknown as HubConnection;
  const emit = (event: string, ...args: unknown[]) => handlers.get(event)?.(...args);
  return { connection, emit };
}

const SID = "s1";

describe("useSessionUpdates", () => {
  it("does not subscribe when connection is null", () => {
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection: null,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
      }),
    );
    expect(result.current.events).toEqual([]);
  });

  it("joins the session group when connected", () => {
    const { connection } = makeConnection();
    renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
      }),
    );
    expect(connection.invoke).toHaveBeenCalledWith("JoinSession", SID);
  });

  it("records EventsIngested for the matching session and invokes callback", () => {
    const { connection, emit } = makeConnection();
    const onEventsIngested = vi.fn();
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
        onEventsIngested,
      }),
    );

    act(() => emit("EventsIngested", SID, 2, 3, 4));

    expect(result.current.events).toHaveLength(1);
    expect(result.current.events[0].type).toBe("eventsIngested");
    expect(onEventsIngested).toHaveBeenCalledWith({
      sessionId: SID,
      turnCount: 2,
      toolCount: 3,
      fileCount: 4,
    });
  });

  it("ignores EventsIngested for a different session", () => {
    const { connection, emit } = makeConnection();
    const onEventsIngested = vi.fn();
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
        onEventsIngested,
      }),
    );

    act(() => emit("EventsIngested", "other", 1, 1, 1));

    expect(result.current.events).toHaveLength(0);
    expect(onEventsIngested).not.toHaveBeenCalled();
  });

  it("records SessionEnded for the matching session", () => {
    const { connection, emit } = makeConnection();
    const onSessionEnded = vi.fn();
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
        onSessionEnded,
      }),
    );

    act(() => emit("SessionEnded", "other"));
    expect(result.current.events).toHaveLength(0);

    act(() => emit("SessionEnded", SID));
    expect(result.current.events).toHaveLength(1);
    expect(onSessionEnded).toHaveBeenCalledTimes(1);
  });

  it("records ViolationCreated only for the matching session", () => {
    const { connection, emit } = makeConnection();
    const onViolationCreated = vi.fn();
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
        onViolationCreated,
      }),
    );

    act(() => emit("ViolationCreated", { id: "v1", sessionId: "other" }));
    expect(result.current.events).toHaveLength(0);

    const v = { id: "v2", sessionId: SID, ruleId: "r", severity: "low", message: "m" };
    act(() => emit("ViolationCreated", v));
    expect(result.current.events).toHaveLength(1);
    expect(onViolationCreated).toHaveBeenCalledWith(v);
  });

  it("records ViolationResolved regardless of session", () => {
    const { connection, emit } = makeConnection();
    const onViolationResolved = vi.fn();
    const { result } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
        onViolationResolved,
      }),
    );

    act(() => emit("ViolationResolved", "v9"));

    expect(result.current.events).toHaveLength(1);
    expect(result.current.events[0].type).toBe("violationResolved");
    expect(onViolationResolved).toHaveBeenCalledWith("v9");
  });

  it("leaves the session group on unmount", () => {
    const { connection } = makeConnection();
    const { unmount } = renderHook(() =>
      useSessionUpdates({
        connection,
        connectionState: HubConnectionState.Connected,
        sessionId: SID,
      }),
    );

    unmount();

    expect(connection.invoke).toHaveBeenCalledWith("LeaveSession", SID);
    expect(connection.off).toHaveBeenCalledWith("EventsIngested", expect.any(Function));
  });
});
