import type { ReactNode } from "react";

export interface TimelineEventProps {
  type: "turn" | "tool" | "file";
  timestamp: string;
  children: ReactNode;
  isDecision?: boolean;
}

const dotColors: Record<TimelineEventProps["type"], string> = {
  turn: "bg-blue-500",
  tool: "bg-purple-500",
  file: "bg-green-500",
};

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString();
}

export default function TimelineEvent({
  type,
  timestamp,
  children,
  isDecision = false,
}: TimelineEventProps) {
  return (
    <div className="relative pl-8" data-testid="timeline-event">
      {/* Dot on the timeline */}
      <div
        className={`absolute left-0 top-2 w-3 h-3 rounded-full ring-2 ring-white dark:ring-gray-800 ${dotColors[type]}`}
        data-testid={`timeline-dot-${type}`}
      />

      {/* Content card */}
      <div
        className={`pb-6 ${isDecision ? "border-l-4 border-yellow-400 pl-3" : ""}`}
      >
        <span className="text-xs text-gray-400 dark:text-gray-500">{formatTime(timestamp)}</span>
        {isDecision && (
          <span className="ml-2 text-xs bg-amber-100 text-amber-800 px-1.5 py-0.5 rounded font-medium">
            Decision
          </span>
        )}
        <div className="mt-1">{children}</div>
      </div>
    </div>
  );
}
