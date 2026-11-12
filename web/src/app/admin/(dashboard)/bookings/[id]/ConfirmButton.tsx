"use client";

import { useFormStatus } from "react-dom";

export default function ConfirmButton({ message, className, children }: { message: string; className?: string; children: React.ReactNode }) {
  const { pending } = useFormStatus();
  return (
    <button
      type="submit"
      className={className}
      disabled={pending}
      onClick={(e) => { if (!window.confirm(message)) e.preventDefault(); }}
    >
      {pending ? "Sending…" : children}
    </button>
  );
}
