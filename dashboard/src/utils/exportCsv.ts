import { downloadFile } from "./downloadFile";

// OWASP CSV Injection: spreadsheets evaluate cells starting with these
// (including full-width variants) as formulas.
const FORMULA_LEADING = /^[=+\-@\t\r\n＝＋－＠]/;
const PLAIN_NUMBER = /^[+-]?\d+(\.\d+)?(e[+-]?\d+)?$/i;

function neutraliseFormula(value: string): string {
  return FORMULA_LEADING.test(value) && !PLAIN_NUMBER.test(value) ? `'${value}` : value;
}

function escapeCsvValue(raw: string): string {
  const value = neutraliseFormula(raw);
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
