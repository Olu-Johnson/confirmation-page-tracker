"use client";

import { useActionState } from "react";
import { login } from "../actions";

export default function LoginForm({ next }: { next: string }) {
  const [state, action, pending] = useActionState(login, undefined);
  return (
    <form action={action}>
      {state?.error && <div className="alert tone-bad" role="alert">{state.error}</div>}
      <input type="hidden" name="next" value={next} />
      <div className="field">
        <label className="label" htmlFor="username">Username</label>
        <input className="input" id="username" name="username" autoComplete="username" required autoFocus />
      </div>
      <div className="field">
        <label className="label" htmlFor="password">Password</label>
        <input className="input" id="password" name="password" type="password" autoComplete="current-password" required />
      </div>
      <button className="btn btn-primary" type="submit" disabled={pending}>{pending ? "Signing in…" : "Sign in"}</button>
    </form>
  );
}
