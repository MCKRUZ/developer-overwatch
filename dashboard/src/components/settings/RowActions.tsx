import { useState } from "react";
import { DANGER_BUTTON_CLASS, LINK_BUTTON_CLASS } from "./settingsStyles";

interface RowActionsProps {
  /** e.g. "Team Daily budget", used in the buttons' accessible names. */
  itemName: string;
  busy: boolean;
  onEdit: () => void;
  onDelete: () => void;
}

/** Edit and Delete, with an inline confirm step so a stray click never deletes. */
export default function RowActions({ itemName, busy, onEdit, onDelete }: RowActionsProps) {
  const [confirming, setConfirming] = useState(false);

  if (confirming) {
    return (
      <div className="flex gap-3">
        <button type="button" className={DANGER_BUTTON_CLASS} disabled={busy} onClick={onDelete}>
          Confirm delete
        </button>
        <button type="button" className={LINK_BUTTON_CLASS} onClick={() => setConfirming(false)}>
          Cancel
        </button>
      </div>
    );
  }

  return (
    <div className="flex gap-3">
      <button type="button" className={LINK_BUTTON_CLASS} aria-label={`Edit ${itemName}`} onClick={onEdit}>
        Edit
      </button>
      <button
        type="button"
        className={DANGER_BUTTON_CLASS}
        aria-label={`Delete ${itemName}`}
        onClick={() => setConfirming(true)}
      >
        Delete
      </button>
    </div>
  );
}

interface EditActionsProps {
  busy: boolean;
  onSave: () => void;
  onCancel: () => void;
}

export function EditActions({ busy, onSave, onCancel }: EditActionsProps) {
  return (
    <div className="flex gap-3">
      <button type="button" className={LINK_BUTTON_CLASS} disabled={busy} onClick={onSave}>
        Save
      </button>
      <button type="button" className={LINK_BUTTON_CLASS} onClick={onCancel}>
        Cancel
      </button>
    </div>
  );
}
