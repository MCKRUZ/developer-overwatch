const severityStyles: Record<string, string> = {
  Critical: "bg-red-600 text-white",
  Error: "bg-red-100 text-red-800",
  Warning: "bg-yellow-100 text-yellow-800",
  Info: "bg-blue-100 text-blue-800",
};

export default function SeverityBadge({ severity }: { severity: string }) {
  const style = severityStyles[severity] ?? "bg-gray-100 text-gray-800";
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${style}`}>
      {severity}
    </span>
  );
}
