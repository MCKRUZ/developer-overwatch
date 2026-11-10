import { downloadFile } from "./downloadFile";

function escapeCsvValue(value: string): string {
  if (value.includes(",") || value.includes('"') || value.includes("\n") || value.includes("\r")) {
    return `"${value.replace(/"/g, '""')}"`;
  }
  return value;
}

export function exportCsv(filename: string, headers: string[], rows: string[][]): void {
  const headerLine = headers.map(escapeCsvValue).join(",");
  const dataLines = rows.map((row) => row.map(escapeCsvValue).join(","));
  const csv = [headerLine, ...dataLines].join("\r\n");

  downloadFile(filename, ".csv", csv, "text/csv;charset=utf-8;");
}
