import { downloadFile } from "./downloadFile";

export function exportJson(filename: string, data: unknown): void {
  downloadFile(filename, ".json", JSON.stringify(data, null, 2), "application/json;charset=utf-8;");
}
