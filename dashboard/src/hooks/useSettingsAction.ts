import { useCallback, useState } from "react";
import { ApiError } from "../api";
import { mutationErrorMessage } from "../utils/apiErrorMessage";

const DEFAULT_CONFLICT = "That already exists.";

/**
 * Runs one Settings mutation at a time. Success, or a 404 (someone else already removed the item),
 * reloads the lists through `onChanged`; any failure leaves a message for the user.
 */
export function useSettingsAction(onChanged: () => void) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const run = useCallback(
    async (action: () => Promise<unknown>, conflictMessage = DEFAULT_CONFLICT): Promise<boolean> => {
      setBusy(true);
      setError(null);
      try {
        await action();
        onChanged();
        return true;
      } catch (e) {
        setError(mutationErrorMessage(e, conflictMessage));
        if (e instanceof ApiError && e.status === 404) onChanged();
        return false;
      } finally {
        setBusy(false);
      }
    },
    [onChanged],
  );

  return { error, busy, run };
}
