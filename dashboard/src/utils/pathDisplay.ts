export function repoDisplayName(repoPath?: string): string {
  if (!repoPath) return "—";
  const segments = repoPath.split(/[\\/]/).filter(Boolean);
  const last = segments[segments.length - 1];
  return last ?? repoPath;
}

export function osUserFromRepoPath(repoPath?: string): string {
  if (!repoPath) return "—";
  const match = repoPath.match(/users[\\/]([^\\/]+)/i);
  return match?.[1] ?? "—";
}
