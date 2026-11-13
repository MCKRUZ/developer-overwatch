export const INPUT_CLASS =
  "border dark:border-gray-600 rounded px-2 py-1 text-sm dark:bg-gray-800 dark:text-gray-100";
export const BUTTON_CLASS =
  "rounded bg-blue-600 px-3 py-1 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50";
export const LINK_BUTTON_CLASS = "text-sm text-blue-600 dark:text-blue-400 hover:underline disabled:opacity-50";
export const DANGER_BUTTON_CLASS = "text-sm text-red-600 dark:text-red-400 hover:underline disabled:opacity-50";
export const TABLE_WRAP_CLASS = "bg-white dark:bg-gray-800 rounded-lg border dark:border-gray-700 overflow-x-auto";
export const TH_CLASS = "text-left px-4 py-3 font-medium";
export const TD_CLASS = "px-4 py-3";
export const LABEL_CLASS = "flex flex-col gap-1 text-xs text-gray-600 dark:text-gray-300";

/** An optional amount: blank means none (null), anything else is sent for the server to validate. */
export function optionalAmount(text: string): number | null {
  return text.trim() === "" ? null : Number(text);
}
