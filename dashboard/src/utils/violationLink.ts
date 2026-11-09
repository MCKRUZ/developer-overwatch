import type { GovernanceViolation } from "../types";

/** Deep-links a violation to the session detail page, anchored to where it actually occurred
 * (a specific turn or tool execution) when that's known, instead of just opening the session. */
export function violationSessionLink(v: GovernanceViolation): string {
  if (v.conversationTurnId) return `/sessions/${v.sessionId}?turn=${v.conversationTurnId}`;
  if (v.toolExecutionId) return `/sessions/${v.sessionId}?tool=${v.toolExecutionId}`;
  return `/sessions/${v.sessionId}`;
}
