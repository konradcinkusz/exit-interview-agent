"use client";

import { useEffect, useRef } from "react";

interface Props {
  open: boolean;
  title: string;
  body: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
  testId?: string;
}

/**
 * A modal confirmation for an action that cannot be undone. It is the native <dialog> opened with showModal(): the rest of the
 * page is inert, so focus stays inside it; Escape cancels. Cancel comes first and takes focus, so Enter on arrival keeps the data.
 * On close, focus returns to the control that opened it.
 */
export function ConfirmDialog({ open, title, body, confirmLabel, cancelLabel, onConfirm, onCancel, testId }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const opener = useRef<Element | null>(null);

  useEffect(() => {
    const d = dialog.current;
    if (!d) return;
    if (open && !d.open) {
      opener.current = document.activeElement;
      d.showModal();
      cancel.current?.focus();
    } else if (!open && d.open) {
      d.close();
    }
  }, [open]);

  useEffect(() => {
    const d = dialog.current;
    if (!d) return;
    const restore = () => {
      const el = opener.current;
      if (el instanceof HTMLElement && el.isConnected) el.focus();
    };
    d.addEventListener("close", restore);
    return () => d.removeEventListener("close", restore);
  }, []);

  return (
    <dialog
      ref={dialog}
      className="confirm-dialog"
      aria-labelledby="confirm-title"
      aria-describedby="confirm-body"
      data-testid={testId}
      onCancel={(e) => {
        e.preventDefault();
        onCancel();
      }}
    >
      <h2 id="confirm-title">{title}</h2>
      <p id="confirm-body">{body}</p>
      <div className="actions">
        <button type="button" className="secondary" ref={cancel} onClick={onCancel}>{cancelLabel}</button>
        <button type="button" className="danger" onClick={onConfirm}>{confirmLabel}</button>
      </div>
    </dialog>
  );
}
