import { useEffect, useCallback, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { HubConnectionState } from "@microsoft/signalr";

export interface SessionEvent {
  type: "eventsIngested" | "sessionEnded" | "violationCreated" | "violationResolved";
  timestamp: number;
  data: unknown;
}

export interface EventsIngestedData {
  sessionId: string;
  turnCount: number;
  toolCount: number;
  fileCount: number;
}

export interface ViolationData {
  id: string;
  sessionId: string;
  ruleId: string;
  severity: string;
  message: string;
}

interface UseSessionUpdatesOptions {
  connection: HubConnection | null;
  connectionState: HubConnectionState;
  sessionId: string;
  onEventsIngested?: (data: EventsIngestedData) => void;
  onSessionEnded?: () => void;
  onViolationCreated?: (data: ViolationData) => void;
  onViolationResolved?: (violationId: string) => void;
}

export function useSessionUpdates({
  connection,
  connectionState,
  sessionId,
  onEventsIngested,
  onSessionEnded,
  onViolationCreated,
  onViolationResolved,
}: UseSessionUpdatesOptions) {
  const [events, setEvents] = useState<SessionEvent[]>([]);

  const appendEvent = useCallback(
    (type: SessionEvent["type"], data: unknown) => {
      setEvents((prev) => [...prev, { type, timestamp: Date.now(), data }]);
    },
    []
  );

  useEffect(() => {
    if (!connection || connectionState !== HubConnectionState.Connected) return;

    connection.invoke("JoinSession", sessionId).catch(() => {});

    const handleEventsIngested = (
      sid: string,
      turnCount: number,
      toolCount: number,
      fileCount: number
    ) => {
      if (sid !== sessionId) return;
      const data: EventsIngestedData = { sessionId: sid, turnCount, toolCount, fileCount };
      appendEvent("eventsIngested", data);
      onEventsIngested?.(data);
    };

    const handleSessionEnded = (sid: string) => {
      if (sid !== sessionId) return;
      appendEvent("sessionEnded", null);
      onSessionEnded?.();
    };

    const handleViolationCreated = (violation: ViolationData) => {
      if (violation.sessionId !== sessionId) return;
      appendEvent("violationCreated", violation);
      onViolationCreated?.(violation);
    };

    const handleViolationResolved = (violationId: string) => {
      appendEvent("violationResolved", violationId);
      onViolationResolved?.(violationId);
    };

    connection.on("EventsIngested", handleEventsIngested);
    connection.on("SessionEnded", handleSessionEnded);
    connection.on("ViolationCreated", handleViolationCreated);
    connection.on("ViolationResolved", handleViolationResolved);

    return () => {
      connection.off("EventsIngested", handleEventsIngested);
      connection.off("SessionEnded", handleSessionEnded);
      connection.off("ViolationCreated", handleViolationCreated);
      connection.off("ViolationResolved", handleViolationResolved);
      connection.invoke("LeaveSession", sessionId).catch(() => {});
    };
  }, [
    connection,
    connectionState,
    sessionId,
    appendEvent,
    onEventsIngested,
    onSessionEnded,
    onViolationCreated,
    onViolationResolved,
  ]);

  return { events };
}
