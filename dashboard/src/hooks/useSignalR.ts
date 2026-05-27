import { useEffect, useRef, useState } from "react";
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";

const HUB_URL = "/hubs/overwatch";
const MAX_RETRY_DELAY_MS = 30_000;

export function useSignalR() {
  const connectionRef = useRef<HubConnection | null>(null);
  const [state, setState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected
  );

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (ctx) =>
          Math.min(1000 * Math.pow(2, ctx.previousRetryCount), MAX_RETRY_DELAY_MS),
      })
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.onreconnecting(() => setState(HubConnectionState.Reconnecting));
    connection.onreconnected(() => setState(HubConnectionState.Connected));
    connection.onclose(() => setState(HubConnectionState.Disconnected));

    connection
      .start()
      .then(() => setState(HubConnectionState.Connected))
      .catch(() => setState(HubConnectionState.Disconnected));

    return () => {
      connection.stop();
    };
  }, []);

  return { connection: connectionRef.current, state };
}
