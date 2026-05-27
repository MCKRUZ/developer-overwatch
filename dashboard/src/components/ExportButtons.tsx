interface ExportButtonsProps {
  onExportCsv: () => void;
  onExportPdf: () => void;
}

export default function ExportButtons({ onExportCsv, onExportPdf }: ExportButtonsProps) {
  return (
    <div className="flex gap-2">
      <button
        type="button"
        onClick={onExportCsv}
        className="border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-300 px-3 py-1.5 rounded text-sm hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
      >
        Export CSV
      </button>
      <button
        type="button"
        onClick={onExportPdf}
        className="border border-gray-300 dark:border-gray-600 text-gray-600 dark:text-gray-300 px-3 py-1.5 rounded text-sm hover:bg-gray-50 dark:hover:bg-gray-700 transition-colors"
      >
        Export PDF
      </button>
    </div>
  );
}
