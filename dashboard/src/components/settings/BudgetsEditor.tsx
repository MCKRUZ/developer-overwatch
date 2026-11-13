import { useState, type FormEvent } from "react";
import { api } from "../../api";
import type { Budget, BudgetPeriod, BudgetScope } from "../../types";
import { useSettingsAction } from "../../hooks/useSettingsAction";
import { formatCost } from "../../utils/tokenFormat";
import { budgetWho } from "../../utils/budgetFormat";
import RowActions, { EditActions } from "./RowActions";
import SettingsSection from "./SettingsSection";
import { BUTTON_CLASS, INPUT_CLASS, LABEL_CLASS, TABLE_WRAP_CLASS, TD_CLASS, TH_CLASS } from "./settingsStyles";

type Run = ReturnType<typeof useSettingsAction>["run"];

function budgetName(budget: Pick<Budget, "scope" | "developerEmail" | "period">): string {
  return `${budgetWho(budget)} ${budget.period} budget`;
}

function BudgetRow({ budget, canEdit, busy, run }: { budget: Budget; canEdit: boolean; busy: boolean; run: Run }) {
  const [editing, setEditing] = useState(false);
  const [amount, setAmount] = useState(String(budget.amountUsd));
  const name = budgetName(budget);

  async function save() {
    if (await run(() => api.budgets.update(budget.id, Number(amount)))) setEditing(false);
  }

  return (
    <tr>
      <td className={TD_CLASS}>{budgetWho(budget)}</td>
      <td className={TD_CLASS}>{budget.period}</td>
      <td className={TD_CLASS}>
        {editing ? (
          <input
            aria-label={`New amount for ${name}`}
            type="number"
            step="any"
            className={INPUT_CLASS}
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
        ) : (
          formatCost(budget.amountUsd)
        )}
      </td>
      {canEdit && (
        <td className={TD_CLASS}>
          {editing ? (
            <EditActions busy={busy} onSave={save} onCancel={() => setEditing(false)} />
          ) : (
            <RowActions
              itemName={name}
              busy={busy}
              onEdit={() => {
                setAmount(String(budget.amountUsd));
                setEditing(true);
              }}
              onDelete={() => run(() => api.budgets.remove(budget.id))}
            />
          )}
        </td>
      )}
    </tr>
  );
}

function AddBudgetForm({ busy, run }: { busy: boolean; run: Run }) {
  const [scope, setScope] = useState<BudgetScope>("Team");
  const [email, setEmail] = useState("");
  const [period, setPeriod] = useState<BudgetPeriod>("Daily");
  const [amount, setAmount] = useState("");

  async function submit(e: FormEvent) {
    e.preventDefault();
    const developerEmail = scope === "Developer" ? email.trim() : null;
    const conflict = `A ${budgetName({ scope, developerEmail, period })} already exists. Edit it instead.`;
    const body = { scope, developerEmail, period, amountUsd: Number(amount) };
    if (await run(() => api.budgets.create(body), conflict)) setAmount("");
  }

  return (
    <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
      <label className={LABEL_CLASS}>
        Scope
        <select className={INPUT_CLASS} value={scope} onChange={(e) => setScope(e.target.value as BudgetScope)}>
          <option value="Team">Team</option>
          <option value="Developer">Developer</option>
        </select>
      </label>
      {scope === "Developer" && (
        <label className={LABEL_CLASS}>
          Developer email
          <input type="email" required className={INPUT_CLASS} value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
      )}
      <label className={LABEL_CLASS}>
        Period
        <select className={INPUT_CLASS} value={period} onChange={(e) => setPeriod(e.target.value as BudgetPeriod)}>
          <option value="Daily">Daily</option>
          <option value="Monthly">Monthly</option>
        </select>
      </label>
      <label className={LABEL_CLASS}>
        Amount (USD)
        <input type="number" step="any" required className={INPUT_CLASS} value={amount} onChange={(e) => setAmount(e.target.value)} />
      </label>
      <button type="submit" className={BUTTON_CLASS} disabled={busy}>
        Add budget
      </button>
    </form>
  );
}

interface BudgetsEditorProps {
  budgets: readonly Budget[];
  canEdit: boolean;
  onChanged: () => void;
}

export default function BudgetsEditor({ budgets, canEdit, onChanged }: BudgetsEditorProps) {
  const { error, busy, run } = useSettingsAction(onChanged);
  return (
    <SettingsSection title="Budgets" error={error}>
      {budgets.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-gray-400">No budgets yet.</p>
      ) : (
        <div className={TABLE_WRAP_CLASS}>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
              <tr>
                <th className={TH_CLASS}>Who</th>
                <th className={TH_CLASS}>Period</th>
                <th className={TH_CLASS}>Amount</th>
                {canEdit && <th className={TH_CLASS}>Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y dark:divide-gray-700">
              {budgets.map((b) => (
                <BudgetRow key={b.id} budget={b} canEdit={canEdit} busy={busy} run={run} />
              ))}
            </tbody>
          </table>
        </div>
      )}
      {canEdit && <AddBudgetForm busy={busy} run={run} />}
    </SettingsSection>
  );
}
