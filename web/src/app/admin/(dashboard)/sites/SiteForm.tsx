"use client";

import { useActionState } from "react";
import type { Site, TrackMode } from "@/lib/types";
import { DEFAULT_THEME, THEMES } from "@/lib/themes";
import { saveSite } from "../../actions";

const MODES: { value: TrackMode; label: string; help: string }[] = [
  { value: "Lead", label: "Leads", help: "Every booked meeting, free or paid, is reported as a lead." },
  { value: "Sale", label: "Sales", help: "Only paid meetings are reported, once the payment goes through." },
  { value: "Both", label: "Leads and sales", help: "A lead when a meeting is booked, plus a sale when a paid meeting is paid." },
];

const PLACEHOLDERS = "{click_id} {conversion_id} {event} {txid} {amount} {currency} {status} {affiliate_id} {sub1}…{sub5}";

export default function SiteForm({ site }: { site?: Site }) {
  const [state, action, pending] = useActionState(saveSite, undefined);
  return (
    <form action={action} className="site-form">
      {state?.error && <div className="alert tone-bad" role="alert">{state.error}</div>}
      {site && <input type="hidden" name="id" value={site.id} />}

      <div className="field">
        <label className="label" htmlFor="name">Name</label>
        <input className="input" id="name" name="name" required maxLength={200} defaultValue={site?.name} placeholder="Dept Global Solutions" />
      </div>
      <div className="field">
        <label className="label" htmlFor="key">Key</label>
        <input className="input mono" id="key" name="key" required pattern="[a-z0-9][a-z0-9\-]{1,49}" defaultValue={site?.key} placeholder="dept-habyyer" />
        <p className="hint">Lowercase letters, digits and dashes. Used in the tracker script (<code>data-site</code>) and the webhook URL, so changing it means updating both.</p>
      </div>

      <div className="field">
        <span className="label">Report to TrackCrest</span>
        <div className="modes">
          {MODES.map((m) => (
            <label key={m.value} className="mode">
              <input type="radio" name="mode" value={m.value} defaultChecked={(site?.mode ?? "Both") === m.value} />
              <span><strong>{m.label}</strong><small>{m.help}</small></span>
            </label>
          ))}
        </div>
      </div>

      <div className="field">
        <label className="label" htmlFor="leadPostbackUrl">Lead postback URL</label>
        <input className="input mono" id="leadPostbackUrl" name="leadPostbackUrl" defaultValue={site?.leadPostbackUrl ?? ""} placeholder="https://trackcrest.com/postback?click_id={click_id}&event=lead&txid={conversion_id}" />
      </div>
      <div className="field">
        <label className="label" htmlFor="salePostbackUrl">Sale postback URL</label>
        <input className="input mono" id="salePostbackUrl" name="salePostbackUrl" defaultValue={site?.salePostbackUrl ?? ""} placeholder="https://trackcrest.com/postback?click_id={click_id}&event=sale&amount={amount}&txid={conversion_id}" />
        <p className="hint">
          From the TrackCrest offer. Only the URLs for what this site reports are used; they can be the same URL with <code>{"{event}"}</code> (lead / sale) in it.
          Placeholders: <code>{PLACEHOLDERS}</code>. With leads and sales, use <code>{"{conversion_id}"}</code> as the transaction ID so TrackCrest doesn&apos;t treat the sale as a duplicate of the lead.
        </p>
      </div>

      <div className="field">
        <span className="label">Confirmation page theme</span>
        <div className="themes">
          {THEMES.map((t) => (
            <label key={t.key} className="theme">
              <input type="radio" name="confirmationTheme" value={t.key} defaultChecked={(site?.confirmationTheme ?? DEFAULT_THEME) === t.key} />
              <span className="theme-swatch" aria-hidden="true" style={{ background: t.swatch[0] }}>
                <span style={{ background: t.swatch[1] }}><i style={{ background: t.swatch[2] }} /></span>
              </span>
              <span className="theme-text">
                <strong>{t.name}{t.key === DEFAULT_THEME && <small className="theme-default">Default</small>}</strong>
                <small>{t.description}</small>
              </span>
              <a className="theme-preview" target="_blank" rel="noopener noreferrer"
                href={`/booking-confirmed${site ? `/${site.key}` : ""}?preview=1&theme=${t.key}`}>Preview ↗</a>
            </label>
          ))}
        </div>
        <p className="hint">What visitors see after booking. Preview opens it with a sample booking{site ? ` and ${site.name}'s name` : ""}; save to apply it.</p>
      </div>

      <div className="field">
        <label className="label" htmlFor="allowedOrigins">Website addresses</label>
        <textarea className="textarea mono" id="allowedOrigins" name="allowedOrigins" rows={2} defaultValue={site?.allowedOrigins.join("\n")} placeholder="https://dept.habyyer.com" />
        <p className="hint">One per line. The tracker script only reports bookings from these.</p>
      </div>

      <div className="field">
        <label className="label" htmlFor="webhookSecret">Zylux webhook secret</label>
        <input className="input mono" id="webhookSecret" name="webhookSecret" minLength={16} maxLength={200} autoComplete="off"
          placeholder={site ? "Leave empty to keep the current secret" : "Paste the secret from the Zylux Meet webhook, or leave empty to generate one"} />
        <p className="hint">
          Must be the same as the <strong>Secret</strong> on the webhook in the site owner&apos;s Zylux Meet account (Settings → Developer → Webhooks), or every
          webhook is rejected. Either paste Zylux&apos;s secret here, or leave this empty and copy this site&apos;s secret (shown on the site page) into Zylux.
        </p>
      </div>

      <div className="field">
        <label className="check-row">
          <input type="checkbox" name="active" defaultChecked={site?.active ?? true} /> Active (turning it off stops tracker calls and webhooks for this site)
        </label>
      </div>

      <button className="btn btn-primary" type="submit" disabled={pending}>{pending ? "Saving…" : site ? "Save changes" : "Create site"}</button>
    </form>
  );
}
