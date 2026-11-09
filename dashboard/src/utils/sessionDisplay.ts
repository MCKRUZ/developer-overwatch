export function sessionDisplayName(session: { id: string; title?: string }, guidPrefixLength = 8): string {
  return session.title?.trim() || `${session.id.slice(0, guidPrefixLength)}...`;
}
