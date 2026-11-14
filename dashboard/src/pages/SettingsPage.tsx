import { useCallback, useEffect, useState } from "react";
import { api } from "../api";
import type { Budget, DeveloperPlan } from "../types";
import BudgetsEditor from "../components/settings/BudgetsEditor";
import DeveloperPlansEditor from "../components/settings/DeveloperPlansEditor";

const READ_ONLY_NOTE =
  "Editing needs the Lead role. Servers without sign-in only allow editing in local development.";

interface SettingsData {
  budgets: Budget[];
  plans: DeveloperPlan[];
}

/** Edit rights come from the server's verdict; until it answers, or if it fails, the page is read-only. */
function useCanEditBudgets(): boolean {
  const [canEdit, setCanEdit] = useState(false);
  useEffect(() => {
    let active = true;
    api.auth.me().then(
      (me) => {
        if (active) setCanEdit(me.canEditBudgets);
      },
      () => {
        if (active) setCanEdit(false);
      },
    );
    return () => {
      active = false;
    };
  }, []);
  return canEdit;
}

export default function SettingsPage() {
  const canEdit = useCanEditBudgets();
  const [data, setData] = useState<SettingsData | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    Promise.all([api.budgets.list(), api.developerPlans.list()]).then(
      ([budgets, plans]) => {
        setData({ budgets, plans });
        setError(null);
      },
      (e: unknown) => setError(e instanceof Error ? e.message : String(e)),
    );
  }, []);

  useEffect(reload, [reload]);

  return (
    <div className="space-y-6">
      <h2 className="text-xl font-semibold">Settings</h2>
      {!canEdit && <p className="text-sm text-gray-600 dark:text-gray-300">{READ_ONLY_NOTE}</p>}
      {error && <p className="text-sm text-red-600 dark:text-red-400">{`Could not load settings: ${error}`}</p>}
      {!data && !error && <p className="text-gray-500 dark:text-gray-400">Loading settings...</p>}
      {data && (
        <>
          <BudgetsEditor budgets={data.budgets} canEdit={canEdit} onChanged={reload} />
          <DeveloperPlansEditor plans={data.plans} canEdit={canEdit} onChanged={reload} />
        </>
      )}
    </div>
  );
}
