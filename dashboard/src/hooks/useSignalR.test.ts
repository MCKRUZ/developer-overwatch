import { renderHook, act, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { HubConnectionState } from "@microsoft/signalr";
import { setTokenGetter, clearTokenGetter } from "../api";

const { conn, makeConn, builder, auth } = vi.hoisted(() => {
  function makeConn(): Record<string, unknown> {
    const c: Record<string, unknown> = {};
    c.start = vi.fn().mockResolvedValue(undefined);
    c.stop = vi.fn().mockResolvedValue(undefined);
    c.onreconnecting = vi.fn((cb: () => void) => {
      c._reconnecting = cb;
    });
    c.onreconnected = vi.fn((cb: () => void) => {
      c._reconnected = cb;
    });
    c.onclose = vi.fn((cb: () => void) => {
      c._close = cb;
    });
    return c;
  }
  const conn = makeConn();
  const builder: Record<string, unknown> = {};
  builder.withUrl = vi.fn(() => builder);
  builder.withAutomaticReconnect = vi.fn(() => builder);
  builder.configureLogging = vi.fn(() => builder);
  builder.build = vi.fn(() => conn);
  const auth = { configured: false, token: null as string | null };
  return { conn, makeConn, builder, auth };
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

vi.mock("../auth/AuthProvider", () => ({
  get isAuthConfigured() {
    return auth.configured;
  },
  useAuthContext: () => ({ token: auth.token }),
}));

import { useSignalR } from "./useSignalR";

type TokenFactory = { accessTokenFactory: () => string };

function lastTokenFactory(): TokenFactory["accessTokenFactory"] {
  const calls = (builder.withUrl as ReturnType<typeof vi.fn>).mock.calls;
  return (calls[calls.length - 1]![1] as TokenFactory).accessTokenFactory;
}

describe("useSignalR", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    (conn.start as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);
    auth.configured = false;
    auth.token = null;
  });

  afterEach(() => clearTokenGetter());

  it("should connect to the hub URL and reach Connected after start resolves", async () => {
    const { result } = renderHook(() => useSignalR());

    expect(builder.withUrl).toHaveBeenCalledWith("/hubs/overwatch", expect.any(Object));
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));
    expect(result.current.connection).toBe(conn);
  });

  it("should compute exponential backoff capped at 30s", () => {
    renderHook(() => useSignalR());

    const config = (builder.withAutomaticReconnect as ReturnType<typeof vi.fn>).mock.calls[0]![0];
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 0 })).toBe(1000);
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 2 })).toBe(4000);
    expect(config.nextRetryDelayInMilliseconds({ previousRetryCount: 20 })).toBe(30000);
  });

  it("should reflect reconnecting / reconnected / close lifecycle", async () => {
    const { result } = renderHook(() => useSignalR());
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));

    act(() => (conn._reconnecting as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Reconnecting);

    act(() => (conn._reconnected as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Connected);

    act(() => (conn._close as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Disconnected);
  });

  it("should stop the connection on unmount", () => {
    const { unmount } = renderHook(() => useSignalR());
    unmount();
    expect(conn.stop).toHaveBeenCalled();
  });

  it("should send no token when sign-in is not configured, so the anonymous API accepts the hub", () => {
    renderHook(() => useSignalR());
    expect(lastTokenFactory()()).toBe("");
  });

  it("should not start before a token exists when sign-in is configured, because the hub would reject it", () => {
    auth.configured = true;
    const { result } = renderHook(() => useSignalR());

    expect(builder.build).not.toHaveBeenCalled();
    expect(conn.start).not.toHaveBeenCalled();
    expect(result.current.connection).toBeNull();
  });

  it("should start once the token arrives and hand the hub the current bearer token", async () => {
    auth.configured = true;
    const { result, rerender } = renderHook(() => useSignalR());

    auth.token = "tok-1";
    setTokenGetter(() => "tok-1");
    rerender();

    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));
    expect(conn.start).toHaveBeenCalledTimes(1);
    expect(lastTokenFactory()()).toBe("tok-1");
  });

  it("should keep the connection when the token silently renews, so the live feed does not drop hourly", async () => {
    auth.configured = true;
    auth.token = "tok-1";
    let current = "tok-1";
    setTokenGetter(() => current);
    const { result, rerender } = renderHook(() => useSignalR());
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));

    auth.token = "tok-2";
    current = "tok-2";
    rerender();

    expect(conn.start).toHaveBeenCalledTimes(1);
    expect(conn.stop).not.toHaveBeenCalled();
    expect(lastTokenFactory()()).toBe("tok-2");
  });

  it("should stop the connection when the token goes away on sign-out", async () => {
    auth.configured = true;
    auth.token = "tok-1";
    const { result, rerender } = renderHook(() => useSignalR());
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));

    auth.token = null;
    rerender();

    expect(conn.stop).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Disconnected));
  });

  it("should ignore late callbacks from a replaced connection, so a stopped old one cannot mark the live one disconnected", async () => {
    let rejectOldStart: (e: Error) => void = () => {};
    (conn.start as ReturnType<typeof vi.fn>).mockReturnValueOnce(
      new Promise<void>((_resolve, reject) => {
        rejectOldStart = reject;
      }),
    );
    const replacement = makeConn();
    (builder.build as ReturnType<typeof vi.fn>)
      .mockImplementationOnce(() => conn)
      .mockImplementationOnce(() => replacement);
    auth.configured = true;
    auth.token = "tok-1";
    const { result, rerender } = renderHook(() => useSignalR());

    auth.token = null;
    rerender();
    auth.token = "tok-2";
    rerender();
    await waitFor(() => expect(result.current.state).toBe(HubConnectionState.Connected));
    expect(result.current.connection).toBe(replacement);

    // Stopping a connection mid-start rejects its start and fires its close handler afterwards.
    await act(async () => {
      rejectOldStart(new Error("stopped during negotiation"));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(result.current.state).toBe(HubConnectionState.Connected);
    act(() => (conn._reconnecting as () => void)());
    act(() => (conn._close as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Connected);

    act(() => (replacement._close as () => void)());
    act(() => (conn._reconnected as () => void)());
    expect(result.current.state).toBe(HubConnectionState.Disconnected);
  });

  describe("start retry", () => {
    const start = () => conn.start as ReturnType<typeof vi.fn>;
    const advance = (ms: number) => act(async () => {
      await vi.advanceTimersByTimeAsync(ms);
    });

    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it("should retry a failed first start on a 2, 5, 10, 30 s backoff until it connects, so a hub that was down at page load still goes live", async () => {
      start()
        .mockRejectedValueOnce(new Error("down"))
        .mockRejectedValueOnce(new Error("down"))
        .mockRejectedValueOnce(new Error("down"))
        .mockRejectedValueOnce(new Error("down"))
        .mockRejectedValueOnce(new Error("down"));
      const { result } = renderHook(() => useSignalR());
      await advance(0);
      expect(result.current.state).toBe(HubConnectionState.Disconnected);

      await advance(1_999);
      expect(start()).toHaveBeenCalledTimes(1);
      await advance(1);
      expect(start()).toHaveBeenCalledTimes(2);
      await advance(5_000);
      expect(start()).toHaveBeenCalledTimes(3);
      await advance(10_000);
      expect(start()).toHaveBeenCalledTimes(4);
      await advance(30_000);
      expect(start()).toHaveBeenCalledTimes(5);
      // The schedule is capped: every later attempt waits 30 s.
      await advance(30_000);
      expect(start()).toHaveBeenCalledTimes(6);

      expect(result.current.state).toBe(HubConnectionState.Connected);
      expect(result.current.connectedEpoch).toBe(1);
    });

    it("should restart after the server closes the connection, as it does when the token expires, and count it as a new connect", async () => {
      const { result } = renderHook(() => useSignalR());
      await advance(0);
      expect(result.current.connectedEpoch).toBe(1);

      act(() => (conn._close as () => void)());
      expect(result.current.state).toBe(HubConnectionState.Disconnected);
      await advance(2_000);

      expect(start()).toHaveBeenCalledTimes(2);
      expect(result.current.state).toBe(HubConnectionState.Connected);
      expect(result.current.connectedEpoch).toBe(2);
    });

    it("should restart the backoff from 2 s after a connect, so one old outage does not slow the next recovery", async () => {
      start().mockRejectedValueOnce(new Error("down"));
      renderHook(() => useSignalR());
      await advance(2_000);
      expect(start()).toHaveBeenCalledTimes(2);

      act(() => (conn._close as () => void)());
      await advance(2_000);

      expect(start()).toHaveBeenCalledTimes(3);
    });

    it("should count an automatic reconnect as a new connect, so subscribers resync what they missed", async () => {
      const { result } = renderHook(() => useSignalR());
      await advance(0);

      act(() => (conn._reconnecting as () => void)());
      act(() => (conn._reconnected as () => void)());

      expect(result.current.connectedEpoch).toBe(2);
    });

    it("should cancel a pending retry on unmount, so a closed page never reconnects", async () => {
      start().mockRejectedValueOnce(new Error("down"));
      const { unmount } = renderHook(() => useSignalR());
      await advance(0);

      unmount();
      await advance(60_000);

      expect(start()).toHaveBeenCalledTimes(1);
    });

    it("should cancel a pending retry on sign-out, so a signed-out page does not keep knocking on the hub", async () => {
      start().mockRejectedValueOnce(new Error("down"));
      auth.configured = true;
      auth.token = "tok-1";
      const { rerender } = renderHook(() => useSignalR());
      await advance(0);

      auth.token = null;
      rerender();
      await advance(60_000);

      expect(start()).toHaveBeenCalledTimes(1);
    });

    it("should not schedule a retry from the close a deliberate stop fires", async () => {
      const { unmount } = renderHook(() => useSignalR());
      await advance(0);

      unmount();
      act(() => (conn._close as () => void)());
      await advance(60_000);

      expect(start()).toHaveBeenCalledTimes(1);
    });
  });
});
