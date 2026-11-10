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

export function useSignalR() {
  const { token } = useAuthContext();
  // A boolean, not the token itself: a silent token renewal must not rebuild the connection.
  // The factory below reads the current token on every (re)connect instead.
  const canConnect = !isAuthConfigured || token !== null;
  const connectionRef = useRef<HubConnection | null>(null);
  const [state, setState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected
  );

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

    // A stopped connection still settles its start and fires onclose afterwards; only the
    // current connection may report state, or a replaced one would mark the live one disconnected.
    const setStateIfCurrent = (next: HubConnectionState) => {
      if (connectionRef.current !== connection) return;
      setState(next);
    };

    connection.onreconnecting(() => setStateIfCurrent(HubConnectionState.Reconnecting));
    connection.onreconnected(() => setStateIfCurrent(HubConnectionState.Connected));
    connection.onclose(() => setStateIfCurrent(HubConnectionState.Disconnected));

    connection
      .start()
      .then(() => setStateIfCurrent(HubConnectionState.Connected))
      .catch(() => setStateIfCurrent(HubConnectionState.Disconnected));

    return () => {
      connectionRef.current = null;
      setState(HubConnectionState.Disconnected);
      connection.stop();
    };
  }, [canConnect]);

  return { connection: connectionRef.current, state };
}
