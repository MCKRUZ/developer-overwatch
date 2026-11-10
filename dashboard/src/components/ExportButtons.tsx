const BUTTON_CLASS =
  "border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-300 px-3 py-1.5 rounded text-sm hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors";

interface ExportButtonsProps {
  onExportCsv: () => void;
  onExportPdf: () => void;
  /** Shown only when given, so pages without a raw-number export keep CSV and PDF alone. */
  onExportJson?: () => void;
}

export default function ExportButtons({ onExportCsv, onExportPdf, onExportJson }: ExportButtonsProps) {
  return (
    <div className="flex gap-2">
      <button type="button" onClick={onExportCsv} className={BUTTON_CLASS}>
        Export CSV
      </button>
      {onExportJson && (
        <button type="button" onClick={onExportJson} className={BUTTON_CLASS}>
          Export JSON
        </button>
      )}
      <button type="button" onClick={onExportPdf} className={BUTTON_CLASS}>
        Export PDF
      </button>
    </div>
  );
}
