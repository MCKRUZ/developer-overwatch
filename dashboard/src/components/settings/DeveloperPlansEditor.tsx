import { useState, type FormEvent } from "react";
import { api } from "../../api";
import type { DeveloperPlan } from "../../types";
import { useSettingsAction } from "../../hooks/useSettingsAction";
import { formatCost } from "../../utils/tokenFormat";
import RowActions, { EditActions } from "./RowActions";
import SettingsSection from "./SettingsSection";
import {
  BUTTON_CLASS,
  INPUT_CLASS,
  LABEL_CLASS,
  TABLE_WRAP_CLASS,
  TD_CLASS,
  TH_CLASS,
  optionalAmount,
} from "./settingsStyles";

type Run = ReturnType<typeof useSettingsAction>["run"];

const HEADERS = ["Developer", "Plan", "Monthly price", "5-hour window limit"];

interface PlanFields {
  planName: string;
  monthlyPrice: string;
  windowLimit: string;
}

function fieldsOf(plan: DeveloperPlan): PlanFields {
  return {
    planName: plan.planName,
    monthlyPrice: String(plan.monthlyPriceUsd),
    windowLimit: plan.windowLimitUsd === null ? "" : String(plan.windowLimitUsd),
  };
}

function requestOf(fields: PlanFields) {
  return {
    planName: fields.planName,
    monthlyPriceUsd: Number(fields.monthlyPrice),
    windowLimitUsd: optionalAmount(fields.windowLimit),
  };
}

function PlanRow({ plan, canEdit, busy, run }: { plan: DeveloperPlan; canEdit: boolean; busy: boolean; run: Run }) {
  const [editing, setEditing] = useState(false);
  const [fields, setFields] = useState<PlanFields>(() => fieldsOf(plan));
  const email = plan.developerEmail;
  const edit = (key: keyof PlanFields, label: string) => (
    <input
      aria-label={`${label} for ${email}`}
      type={key === "planName" ? "text" : "number"}
      step="any"
      className={INPUT_CLASS}
      value={fields[key]}
      onChange={(e) => setFields({ ...fields, [key]: e.target.value })}
    />
  );

  async function save() {
    if (await run(() => api.developerPlans.update(plan.id, requestOf(fields)))) setEditing(false);
  }

  return (
    <tr>
      <td className={TD_CLASS}>{email}</td>
      <td className={TD_CLASS}>{editing ? edit("planName", "Plan name") : plan.planName}</td>
      <td className={TD_CLASS}>{editing ? edit("monthlyPrice", "Monthly price") : formatCost(plan.monthlyPriceUsd)}</td>
      <td className={TD_CLASS}>
        {editing
          ? edit("windowLimit", "Window limit")
          : plan.windowLimitUsd === null
            ? "none"
            : formatCost(plan.windowLimitUsd)}
      </td>
      {canEdit && (
        <td className={TD_CLASS}>
          {editing ? (
            <EditActions busy={busy} onSave={save} onCancel={() => setEditing(false)} />
          ) : (
            <RowActions
              itemName={`plan for ${email}`}
              busy={busy}
              onEdit={() => {
                setFields(fieldsOf(plan));
                setEditing(true);
              }}
              onDelete={() => run(() => api.developerPlans.remove(plan.id))}
            />
          )}
        </td>
      )}
    </tr>
  );
}

const EMPTY_FIELDS: PlanFields = { planName: "", monthlyPrice: "", windowLimit: "" };

function AddPlanForm({ busy, run }: { busy: boolean; run: Run }) {
  const [email, setEmail] = useState("");
  const [fields, setFields] = useState<PlanFields>(EMPTY_FIELDS);
  const field = (key: keyof PlanFields, label: string, required: boolean) => (
    <label className={LABEL_CLASS}>
      {label}
      <input
        type={key === "planName" ? "text" : "number"}
        step="any"
        required={required}
        className={INPUT_CLASS}
        value={fields[key]}
        onChange={(e) => setFields({ ...fields, [key]: e.target.value })}
      />
    </label>
  );

  async function submit(e: FormEvent) {
    e.preventDefault();
    const developerEmail = email.trim();
    const conflict = `${developerEmail} already has a plan. Edit it instead.`;
    if (await run(() => api.developerPlans.create({ developerEmail, ...requestOf(fields) }), conflict)) {
      setEmail("");
      setFields(EMPTY_FIELDS);
    }
  }

  return (
    <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
      <label className={LABEL_CLASS}>
        Developer email
        <input type="email" required className={INPUT_CLASS} value={email} onChange={(e) => setEmail(e.target.value)} />
      </label>
      {field("planName", "Plan name", true)}
      {field("monthlyPrice", "Monthly price (USD)", true)}
      {field("windowLimit", "5-hour window limit (USD, optional)", false)}
      <button type="submit" className={BUTTON_CLASS} disabled={busy}>
        Add plan
      </button>
    </form>
  );
}

interface DeveloperPlansEditorProps {
  plans: readonly DeveloperPlan[];
  canEdit: boolean;
  onChanged: () => void;
}

/** Plans are entered by the lead; the window limit is API-equivalent USD per 5-hour window. */
export default function DeveloperPlansEditor({ plans, canEdit, onChanged }: DeveloperPlansEditorProps) {
  const { error, busy, run } = useSettingsAction(onChanged);
  return (
    <SettingsSection title="Developer plans" error={error}>
      {plans.length === 0 ? (
        <p className="text-sm text-gray-500 dark:text-gray-400">No developer plans yet.</p>
      ) : (
        <div className={TABLE_WRAP_CLASS}>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 dark:bg-gray-700 border-b dark:border-gray-600">
              <tr>
                {HEADERS.map((h) => (
                  <th key={h} className={TH_CLASS}>
                    {h}
                  </th>
                ))}
                {canEdit && <th className={TH_CLASS}>Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y dark:divide-gray-700">
              {plans.map((p) => (
                <PlanRow key={p.id} plan={p} canEdit={canEdit} busy={busy} run={run} />
              ))}
            </tbody>
          </table>
        </div>
      )}
      {canEdit && <AddPlanForm busy={busy} run={run} />}
    </SettingsSection>
  );
}
