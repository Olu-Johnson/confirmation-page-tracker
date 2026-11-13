"use client";

import { useFormStatus } from "react-dom";

export default function ConfirmButton({ message, className, children, pendingLabel = "Sending…" }: { message: string; className?: string; children: React.ReactNode; pendingLabel?: string }) {
  const { pending } = useFormStatus();
  return (
    <button
      type="submit"
      className={className}
      disabled={pending}
      onClick={(e) => { if (!window.confirm(message)) e.preventDefault(); }}
    >
      {pending ? pendingLabel : children}
    </button>
  );
}
