const statusStyles: Record<string, string> = {
  Pending: "bg-yellow-100 text-yellow-800",
  Approved: "bg-green-100 text-green-800",
  NeedsChanges: "bg-red-100 text-red-800",
  Active: "bg-blue-100 text-blue-800",
  Completed: "bg-gray-100 text-gray-800",
  Failed: "bg-red-100 text-red-800",
  Stale: "bg-amber-100 text-amber-800",
};

export default function StatusBadge({ status }: { status: string }) {
  const style = statusStyles[status] ?? "bg-gray-100 text-gray-800";
  const label = status === "NeedsChanges" ? "Needs Changes" : status;
  const title =
    status === "Stale"
      ? "No activity for a while — likely never got a proper end (crash, sleep, dropped request). Resuming the session reactivates it normally."
      : undefined;
  return (
    <span
      title={title}
      className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${style}`}
    >
      {label}
    </span>
  );
}
