interface MeterBarProps {
  /** 0..1; anything outside is clamped for drawing only, so the caller's text can still say 104%. */
  fraction: number;
  label: string;
  colorClass: string;
}

/** A thin horizontal bar filled to `fraction` of its track. */
export default function MeterBar({ fraction, label, colorClass }: MeterBarProps) {
  const percent = Math.round(Math.min(1, Math.max(0, fraction)) * 100);
  return (
    <div
      role="meter"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={percent}
      className="h-2 w-full rounded bg-gray-200 dark:bg-gray-700 overflow-hidden"
    >
      <div className={`h-full ${colorClass}`} style={{ width: `${percent}%` }} />
    </div>
  );
}
