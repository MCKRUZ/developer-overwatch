import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { getAccessToken } from "../api";
import { isAuthConfigured, useAuthContext } from "../auth/AuthProvider";

const HUB_URL = "/hubs/overwatch";
const MAX_RETRY_DELAY_MS = 30_000;

/** Waits before each new start() after a failed start or a close; the last delay repeats forever. */
export const START_RETRY_DELAYS_MS: readonly number[] = [2_000, 5_000, 10_000, 30_000];

function startRetryDelay(failedAttempts: number): number {
  return START_RETRY_DELAYS_MS[Math.min(failedAttempts, START_RETRY_DELAYS_MS.length - 1)]!;
}

/**
 * Keeps one hub connection open while the user may connect. Automatic reconnect covers a drop
 * of a live connection; a failed first start, or a close the client may not auto-reconnect from
 * (the server ends it when the token expires), is retried here on `START_RETRY_DELAYS_MS`.
 *
 * `connectedEpoch` goes up on every transition into Connected. Events sent while disconnected
 * are lost, so subscribers refetch when it changes.
 */
export function useSignalR() {
  const { token } = useAuthContext();
  // A boolean, not the token itself: a silent token renewal must not rebuild the connection.
  // The factory below reads the current token on every (re)connect instead.
  const canConnect = !isAuthConfigured || token !== null;
  const connectionRef = useRef<HubConnection | null>(null);
  const [state, setState] = useState<HubConnectionState>(HubConnectionState.Disconnected);
  const [connectedEpoch, setConnectedEpoch] = useState(0);

  useEffect(() => {
    if (!canConnect) return;

    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => getAccessToken() ?? "" })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) =>
          Math.min(1000 * Math.pow(2, ctx.previousRetryCount), MAX_RETRY_DELAY_MS),
      })
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;
    let disposed = false;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    let failedAttempts = 0;

    // A stopped connection still settles its start and fires onclose afterwards; only the
    // current connection may report state or retry, or a replaced one would disturb the live one.
    const isCurrent = () => !disposed && connectionRef.current === connection;

    const markConnected = () => {
      if (!isCurrent()) return;
      failedAttempts = 0;
      setState(HubConnectionState.Connected);
      setConnectedEpoch((epoch) => epoch + 1);
    };

    const scheduleStart = () => {
      if (!isCurrent()) return;
      setState(HubConnectionState.Disconnected);
      if (retryTimer !== null) return;
      retryTimer = setTimeout(() => {
        retryTimer = null;
        start();
      }, startRetryDelay(failedAttempts));
      failedAttempts += 1;
    };

    const start = () => {
      if (!isCurrent()) return;
      connection.start().then(markConnected, scheduleStart);
    };

    connection.onreconnecting(() => {
      if (isCurrent()) setState(HubConnectionState.Reconnecting);
    });
    connection.onreconnected(markConnected);
    connection.onclose(scheduleStart);
    start();

    return () => {
      disposed = true;
      if (retryTimer !== null) clearTimeout(retryTimer);
      connectionRef.current = null;
      setState(HubConnectionState.Disconnected);
      connection.stop();
    };
  }, [canConnect]);

  return { connection: connectionRef.current, state, connectedEpoch };
}
