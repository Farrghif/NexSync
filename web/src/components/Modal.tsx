import { useEffect, useRef, type ReactNode } from "react";
import { AlertIcon, CheckIcon, XIcon } from "./icons";

interface ModalProps {
  open: boolean;
  onClose: () => void;
  children: ReactNode;
}

export function Modal({ open, onClose, children }: ModalProps) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    ref.current?.focus();
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = prev;
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="modal-overlay" onMouseDown={onClose}>
      <div
        ref={ref}
        className="modal"
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
        onMouseDown={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  );
}

interface PromptDialogProps {
  open: boolean;
  title: string;
  description?: string;
  initialValue?: string;
  placeholder?: string;
  confirmLabel?: string;
  busy?: boolean;
  onCancel: () => void;
  onConfirm: (value: string) => void;
}

export function PromptDialog({
  open,
  title,
  description,
  initialValue = "",
  placeholder,
  confirmLabel = "Save",
  busy,
  onCancel,
  onConfirm,
}: PromptDialogProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      const id = window.setTimeout(() => {
        inputRef.current?.focus();
        inputRef.current?.select();
      }, 30);
      return () => window.clearTimeout(id);
    }
    return undefined;
  }, [open]);

  const submit = () => {
    const value = inputRef.current?.value.trim() ?? "";
    if (value) onConfirm(value);
  };

  return (
    <Modal open={open} onClose={onCancel}>
      <div className="modal-head">
        <div className="modal-ico info">
          <CheckIcon size={20} />
        </div>
        <div>
          <h2>{title}</h2>
          {description && <p className="modal-desc">{description}</p>}
        </div>
      </div>
      <div className="field">
        <input
          ref={inputRef}
          className="input"
          defaultValue={initialValue}
          placeholder={placeholder}
          onKeyDown={(e) => {
            if (e.key === "Enter") submit();
          }}
        />
      </div>
      <div className="modal-actions">
        <button className="btn btn-ghost" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button className="btn btn-primary" onClick={submit} disabled={busy}>
          {busy ? "Saving…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description: ReactNode;
  confirmLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Confirm",
  danger,
  busy,
  onCancel,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <Modal open={open} onClose={onCancel}>
      <div className="modal-head">
        <div className={`modal-ico ${danger ? "warn" : "info"}`}>
          {danger ? <AlertIcon size={20} /> : <CheckIcon size={20} />}
        </div>
        <div>
          <h2>{title}</h2>
          <div className="modal-desc">{description}</div>
        </div>
      </div>
      <div className="modal-actions">
        <button className="btn btn-ghost" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
        <button
          className={`btn ${danger ? "btn-danger" : "btn-primary"}`}
          onClick={onConfirm}
          disabled={busy}
        >
          {busy ? "Working…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}

interface InfoDialogProps {
  open: boolean;
  title: string;
  children: ReactNode;
  onClose: () => void;
}

export function InfoDialog({ open, title, children, onClose }: InfoDialogProps) {
  return (
    <Modal open={open} onClose={onClose}>
      <div className="modal-head">
        <div className="modal-ico info">
          <XIcon size={20} />
        </div>
        <div>
          <h2>{title}</h2>
        </div>
      </div>
      <div className="modal-desc">{children}</div>
      <div className="modal-actions">
        <button className="btn btn-primary" onClick={onClose}>
          Close
        </button>
      </div>
    </Modal>
  );
}
