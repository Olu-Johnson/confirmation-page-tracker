import { fmtDateTime } from "@/lib/format";
import type { SiteUser } from "@/lib/types";
import { Badge } from "@/components/Badge";
import { createSiteUser, deleteSiteUser, updateSiteUser } from "../../../actions";
import ConfirmButton from "../../bookings/[id]/ConfirmButton";

/** Logins for this site's team. They see only this site's bookings, read-only. */
export default function SiteUsers({ siteId, siteName, users }: { siteId: string; siteName: string; users: SiteUser[] }) {
  return (
    <section className="card card-pad" id="users">
      <h2>Users</h2>
      <p className="hint" style={{ marginTop: 0 }}>
        People who sign in here with their email see only {siteName}&apos;s bookings. They can&apos;t change anything (affiliate payments, reviews,
        postbacks or settings).
      </p>

      {users.length === 0 ? (
        <p className="cell-sub">No users yet.</p>
      ) : (
        <ul className="user-list">
          {users.map((u) => (
            <li key={u.id}>
              <div className="user-head">
                <div>
                  <div className="cell-main">{u.name} {!u.active && <Badge tone="muted">Off</Badge>}</div>
                  <div className="cell-sub">{u.email} · last sign-in {fmtDateTime(u.lastLoginAt)}</div>
                </div>
                <div className="actions-row" style={{ marginTop: 0 }}>
                  <form action={updateSiteUser}>
                    <input type="hidden" name="siteId" value={siteId} />
                    <input type="hidden" name="id" value={u.id} />
                    <input type="hidden" name="op" value={u.active ? "deactivate" : "activate"} />
                    <button className="btn btn-sm" type="submit">{u.active ? "Turn off" : "Turn on"}</button>
                  </form>
                  <form action={deleteSiteUser}>
                    <input type="hidden" name="siteId" value={siteId} />
                    <input type="hidden" name="id" value={u.id} />
                    <ConfirmButton className="btn btn-sm btn-danger" pendingLabel="Deleting…" message={`Delete ${u.email}? They won't be able to sign in any more.`}>
                      Delete
                    </ConfirmButton>
                  </form>
                </div>
              </div>
              <details>
                <summary>Set a new password</summary>
                <form action={updateSiteUser} className="actions-row">
                  <input type="hidden" name="siteId" value={siteId} />
                  <input type="hidden" name="id" value={u.id} />
                  <input type="hidden" name="op" value="password" />
                  <input className="input" type="password" name="password" required minLength={8} maxLength={200} autoComplete="new-password" placeholder="New password (8+ characters)" />
                  <button className="btn btn-sm" type="submit">Save password</button>
                </form>
              </details>
            </li>
          ))}
        </ul>
      )}

      <h3 style={{ marginTop: 16 }}>Add a user</h3>
      <form action={createSiteUser} className="site-form">
        <input type="hidden" name="siteId" value={siteId} />
        <div className="field">
          <label className="label" htmlFor="u-name">Name</label>
          <input className="input" id="u-name" name="name" required maxLength={200} autoComplete="off" />
        </div>
        <div className="field">
          <label className="label" htmlFor="u-email">Email (used to sign in)</label>
          <input className="input" id="u-email" name="email" type="email" required maxLength={200} autoComplete="off" />
        </div>
        <div className="field">
          <label className="label" htmlFor="u-password">Password</label>
          <input className="input" id="u-password" name="password" type="password" required minLength={8} maxLength={200} autoComplete="new-password" />
          <p className="hint">At least 8 characters. Share it with them yourself; no email is sent.</p>
        </div>
        <button className="btn btn-primary" type="submit">Add user</button>
      </form>
    </section>
  );
}
