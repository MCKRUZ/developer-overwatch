import type { ReactNode } from "react";

interface ChartCardProps {
  title: string;
  children: ReactNode;
}

export default function ChartCard({ title, children }: ChartCardProps) {
  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 p-4">
      <h3 className="font-semibold text-sm mb-3 dark:text-gray-100">{title}</h3>
      {children}
    </div>
  );
}
