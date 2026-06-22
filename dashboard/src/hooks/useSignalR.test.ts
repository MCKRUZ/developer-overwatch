import { renderHook, act, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";

const { conn, builder } = vi.hoisted(() => {
  const conn: Record<string, unknown> = {};
  conn.start = vi.fn().mockResolvedValue(undefined);
  conn.stop = vi.fn().mockResolvedValue(undefined);
  conn.onreconnecting = vi.fn((cb: () => void) => {
    conn._reconnecting = cb;
  });
  conn.onreconnected = vi.fn((cb: () => void) => {
    conn._reconnected = cb;
  });
  conn.onclose = vi.fn((cb: () => void) => {
    conn._close = cb;
  });
  const builder: Record<string, unknown> = {};
  builder.withUrl = vi.fn(() => builder);
  builder.withAutomaticReconnect = vi.fn(() => builder);
  builder.configureLogging = vi.fn(() => builder);
  builder.build = vi.fn(() => conn);
  return { conn, builder };
});

vi.mock("@microsoft/signalr", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@microsoft/signalr")>();
  return {
    ...actual,
    HubConnectionBuilder: vi.fn(function HubConnectionBuilder() {
      return builder;
    }),
  };
});

import { useSignalR } from "./useSignalR";

describe("useSignalR", () => {
  beforeEach(() => {
    (conn.start as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
  });

  it("connects to the hub URL and reaches Connected after start resolves", async () => {
    const { result } = renderHook(() => useSignalR());

    expect(builder.withUrl).toHaveBeenCalledWith("/hubs/overwatch");
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));
    expect(result.current.connection).toBe(conn);
  });

  it("computes exponential backoff capped at 30s", () => {
    renderHook(() => useSignalR());

    const config = (builder.withAutomaticReconnect as ReturnType<typeof vi.fn>).mock.calls[0][0];
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 0 })).toBe(1000);
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 2 })).toBe(4000);
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 20 })).toBe(30000);
  });

  it("reflects reconnecting / reconnected / close lifecycle", async () => {
    const { result } = renderHook(() => useSignalR());
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));

    act(() => (conn._reconnecting as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Reconnecting);

    act(() => (conn._reconnected as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Connected);

    act(() => (conn._close as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Disconnected);
  });

  it("falls back to Disconnected when start rejects", async () => {
    (conn.start as ReturnType<typeof vi.fn>).mockRejectedValueOnce(new Error("boom"));
    const { result } = renderHook(() => useSignalR());

    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Disconnected));
  });

  it("stops the connection on unmount", () => {
    const { unmount } = renderHook(() => useSignalR());
    unmount();
    expect(conn.stop).toHaveBeenCalled();
  });
});
