import { useEffect, useState, useCallback } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { HubConnectionState } from "@microsoft/signalr";
import type { ViolationData } from "./useSessionUpdates";

/** Sent by the API after it stores new turns for a session. */
export interface UsageUpdate {
  sessionId: string;
  developerEmail: string;
  at: string;
}

interface GlobalCounts {
  newSessions: number;
  endedSessions: number;
  newViolations: number;
  resolvedViolations: number;
}

interface UseGlobalUpdatesOptions {
  connection: HubConnection | null;
  connectionState: HubConnectionState;
  onSessionStarted?: (sessionId: string, developerEmail: string, repoPath: string | null) => void;
  onSessionEnded?: (sessionId: string) => void;
  onViolationCreated?: (violation: ViolationData) => void;
  onViolationResolved?: (violationId: string) => void;
  onUsageUpdated?: (update: UsageUpdate) => void;
}

export function useGlobalUpdates({
  connection,
  connectionState,
  onSessionStarted,
  onSessionEnded,
  onViolationCreated,
  onViolationResolved,
  onUsageUpdated,
}: UseGlobalUpdatesOptions) {
  const [counts, setCounts] = useState<GlobalCounts>({
    newSessions: 0,
    endedSessions: 0,
    newViolations: 0,
    resolvedViolations: 0,
  });

  const resetCounts = useCallback(() => {
    setCounts({ newSessions: 0, endedSessions: 0, newViolations: 0, resolvedViolations: 0 });
  }, []);

  useEffect(() => {
    if (!connection || connectionState !== HubConnectionState.Connected) return;

    const handleSessionStarted = (
      sessionId: string,
      developerEmail: string,
      repoPath: string | null
    ) => {
      setCounts((prev) => ({ ...prev, newSessions: prev.newSessions + 1 }));
      onSessionStarted?.(sessionId, developerEmail, repoPath);
    };

    const handleSessionEnded = (sessionId: string) => {
      setCounts((prev) => ({ ...prev, endedSessions: prev.endedSessions + 1 }));
      onSessionEnded?.(sessionId);
    };

    const handleViolationCreated = (violation: ViolationData) => {
      setCounts((prev) => ({ ...prev, newViolations: prev.newViolations + 1 }));
      onViolationCreated?.(violation);
    };

    const handleViolationResolved = (violationId: string) => {
      setCounts((prev) => ({ ...prev, resolvedViolations: prev.resolvedViolations + 1 }));
      onViolationResolved?.(violationId);
    };

    const handleUsageUpdated = (update: UsageUpdate) => onUsageUpdated?.(update);

    connection.on("SessionStarted", handleSessionStarted);
    connection.on("SessionEnded", handleSessionEnded);
    connection.on("ViolationCreated", handleViolationCreated);
    connection.on("ViolationResolved", handleViolationResolved);
    connection.on("UsageUpdated", handleUsageUpdated);

    return () => {
      connection.off("SessionStarted", handleSessionStarted);
      connection.off("SessionEnded", handleSessionEnded);
      connection.off("ViolationCreated", handleViolationCreated);
      connection.off("ViolationResolved", handleViolationResolved);
      connection.off("UsageUpdated", handleUsageUpdated);
    };
  }, [
    connection,
    connectionState,
    onSessionStarted,
    onSessionEnded,
    onViolationCreated,
    onViolationResolved,
    onUsageUpdated,
  ]);

  return { counts, resetCounts };
}
