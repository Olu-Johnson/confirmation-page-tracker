import type { Metadata } from "next";
import { requireSuperAdmin } from "@/lib/session";
import Link from "next/link";
import { api, ApiError } from "@/lib/api";
import { env } from "@/lib/env";
import { fmtDateTime } from "@/lib/format";
import type { ApiConfig, SiteStatus } from "@/lib/types";
import { ApiErrorBox } from "@/components/Badge";

export const metadata: Metadata = { title: "Setup" };

export default async function SetupPage() {
  await requireSuperAdmin();
  let cfg: ApiConfig | null = null;
  let error = "";
  try { cfg = await api.config(); } catch (e) { error = e instanceof ApiError ? e.message : "Unexpected error"; }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Setup</h1>
          <p>Everything that has to be in place for a tracked booking to reach TrackCrest, per site.</p>
        </div>
        <div className="actions-row" style={{ marginTop: 0 }}>
          {env.demoEnabled() && <a className="btn" href="/demo?click_id=demo_click_123&sub1=test" target="_blank">Test the tracker ↗</a>}
          <Link className="btn btn-primary" href="/admin/sites/new">Add site</Link>
        </div>
      </div>

      {error && <ApiErrorBox message={error} />}
      {cfg && !cfg.requireWebhookConfirmation && (
        <div className="alert tone-warn">Tracking:RequireWebhookConfirmation is off: conversions can be sent from browser reports alone, which can be faked.</div>
      )}

      <div className="card card-pad" style={{ marginBottom: 16 }}>
        <h2>How a booking is tracked</h2>
        <ol style={{ margin: "8px 0 0 18px", color: "var(--text-2)", lineHeight: 1.7 }}>
          <li>A TrackCrest link sends the visitor to the client&apos;s site (or the Zylux Meet booking link) with <code>?click_id=…</code>.</li>
          <li>Zylux Meet&apos;s <code>embed.js</code> passes the click ID into the booking; <code>tracker.js</code> also reports it from the browser.</li>
          <li>Zylux Meet&apos;s signed webhooks carry the click ID in <code>payload.tracking</code>, so the booking is tracked even if the visitor closes the tab after paying.</li>
          <li>Depending on the site, a <strong>lead</strong> is sent when the meeting is booked and/or a <strong>sale</strong> when a paid meeting is paid.</li>
        </ol>
      </div>

      {cfg && cfg.sites.length === 0 && (
        <div className="card card-pad"><p>No sites yet. <Link href="/admin/sites/new">Add the first site</Link> to get its tracker script and webhook URL.</p></div>
      )}
      {cfg?.sites.map((s) => <SiteChecklist key={s.id} site={s} />)}

      <p className="hint" style={{ marginTop: 12 }}>Last conversion sent to TrackCrest: {fmtDateTime(cfg?.lastConversionSentAt)}</p>
    </>
  );
}

function SiteChecklist({ site: s }: { site: SiteStatus }) {
  const steps: { done: boolean; title: string; hint?: string }[] = [
    ...(s.mode !== "Sale" ? [{ done: s.leadPostbackConfigured, title: "Lead postback URL" }] : []),
    ...(s.mode !== "Lead" ? [{ done: s.salePostbackConfigured, title: "Sale postback URL" }] : []),
    { done: s.allowedOrigins.length > 0, title: "Website address", hint: s.allowedOrigins.join(", ") || undefined },
    { done: s.webhookSecretConfigured && !!s.lastWebhookAt, title: "Zylux Meet webhook", hint: `Last webhook: ${fmtDateTime(s.lastWebhookAt)}` },
    { done: !!s.lastTrackedAt, title: "First tracked booking", hint: `Last tracked: ${fmtDateTime(s.lastTrackedAt)}` },
  ];
  return (
    <div className="card card-pad" style={{ marginBottom: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", gap: 12, alignItems: "baseline" }}>
        <h2>{s.name} <span className="cell-sub mono">{s.key}</span>{!s.active && <span className="cell-sub"> · off</span>}</h2>
        <Link className="btn btn-sm" href={`/admin/sites/${s.id}`}>Settings &amp; snippets</Link>
      </div>
      <ol className="checklist">
        {steps.map((step, i) => (
          <li key={step.title}>
            <span className={`check ${step.done ? "tone-ok" : "tone-bad"}`}>{step.done ? "✓" : i + 1}</span>
            <div>
              <strong>{step.title}</strong>
              {step.hint && <div className="hint" style={{ marginTop: 2 }}>{step.hint}</div>}
            </div>
          </li>
        ))}
      </ol>
    </div>
  );
}
