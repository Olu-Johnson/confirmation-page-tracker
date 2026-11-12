import type { Metadata } from "next";
import { api, ApiError } from "@/lib/api";
import { env, getAppUrl } from "@/lib/env";
import { fmtDateTime } from "@/lib/format";
import type { ApiConfig } from "@/lib/types";
import { ApiErrorBox } from "@/components/Badge";

export const metadata: Metadata = { title: "Setup" };

export default async function SetupPage() {
  let cfg: ApiConfig | null = null;
  let error = "";
  try { cfg = await api.config(); } catch (e) { error = e instanceof ApiError ? e.message : "Unexpected error"; }

  const app = await getAppUrl();
  const apiUrl = env.apiPublicUrl();
  const site = env.brandSiteUrl().replace(/\/+$/, "");
  const snippet = `<script src="${app}/tracker.js" data-booking-tracker
        data-api="${apiUrl}"
        data-confirm-url="${app}/booking-confirmed" async></script>`;
  const nextSnippet = `// app/layout.tsx on ${site.replace(/^https?:\/\//, "")}
import Script from "next/script";
...
<Script src="${app}/tracker.js" strategy="afterInteractive"
        data-booking-tracker data-api="${apiUrl}"
        data-confirm-url="${app}/booking-confirmed" />`;

  const steps: { done: boolean | null; title: string; body: React.ReactNode }[] = [
    {
      done: cfg ? cfg.postbackConfigured : null,
      title: "TrackCrest postback URL (API setting TrackCrest:PostbackUrl)",
      body: (
        <>
          <p>Copy the postback URL from your TrackCrest dashboard and put the click ID placeholder where TrackCrest expects it. Available placeholders: <code>{"{click_id}"}</code> <code>{"{txid}"}</code> <code>{"{amount}"}</code> <code>{"{currency}"}</code> <code>{"{status}"}</code> <code>{"{affiliate_id}"}</code> <code>{"{sub1}"}</code>…<code>{"{sub5}"}</code>.</p>
          {cfg?.postbackHost && <p className="hint">Currently sending to <span className="mono">{cfg.postbackHost}</span></p>}
        </>
      ),
    },
    {
      done: null,
      title: "TrackCrest offer destination URL",
      body: (
        <>
          <p>Send affiliate clicks to the client&apos;s site with the click ID in the URL (use TrackCrest&apos;s click ID macro):</p>
          <div className="code-block">{`${site}/?click_id={click_id}`}</div>
          <p className="hint">If TrackCrest uses another parameter name, add it to <code>data-click-params</code> on the script tag.</p>
        </>
      ),
    },
    {
      done: cfg ? cfg.webhookSecretConfigured && !!cfg.lastWebhookAt : null,
      title: "Zylux Meet webhook",
      body: (
        <>
          <p>In Zylux Meet → Settings → Developer → Webhooks, add:</p>
          <div className="code-block">{`${apiUrl}/api/webhooks/zylux`}</div>
          <p style={{ marginTop: 8 }}>Triggers: <code>BOOKING_CREATED</code>, <code>BOOKING_REQUESTED</code>, <code>BOOKING_PAID</code>, <code>BOOKING_RESCHEDULED</code>, <code>BOOKING_CANCELLED</code>, <code>BOOKING_REJECTED</code>, <code>BOOKING_NO_SHOW_UPDATED</code>. Use the same secret as the API setting <code>Zylux:WebhookSecret</code>. The URL must be public HTTPS.</p>
          <p className="hint">
            Secret on API: {cfg?.webhookSecretConfigured ? "set" : "missing"} · Last webhook received: {fmtDateTime(cfg?.lastWebhookAt)}
          </p>
        </>
      ),
    },
    {
      done: null,
      title: "Zylux Meet redirect on booking (event → Advanced → Redirect on booking)",
      body: (
        <>
          <p>Point it back at the client&apos;s site, not at this app:</p>
          <div className="code-block">{`${site}/?bt_booked=1`}</div>
          <p style={{ marginTop: 8 }}>Zylux appends <code>uid</code>, <code>startTime</code> and <code>title</code>. <code>tracker.js</code> on that page still has the visitor&apos;s click ID, records the booking and forwards to <span className="mono">{`${app}/booking-confirmed`}</span>. This covers paid bookings whether payment opens in the same tab or a new one.</p>
          <p className="hint">Use the client&apos;s own domain: a redirect straight to /booking-confirmed shows the page but can&apos;t link the booking to an affiliate click.</p>
        </>
      ),
    },
    {
      done: cfg ? !!cfg.lastTrackedAt : null,
      title: "Add the tracker script to the client's website",
      body: (
        <>
          <p>Add it to every page (the site layout), so it covers the inline scheduler on /contact and the &quot;Book Consultation&quot; popup on the home page:</p>
          <div className="code-block">{snippet}</div>
          <p style={{ marginTop: 8 }}>On a Next.js site:</p>
          <div className="code-block">{nextSnippet}</div>
          <p className="hint">Last tracked booking: {fmtDateTime(cfg?.lastTrackedAt)}</p>
        </>
      ),
    },
    {
      done: cfg ? cfg.allowedOrigins.some((o) => o.replace(/\/+$/, "") === site) : null,
      title: "Allow the client's site on the API (Tracking:AllowedOrigins)",
      body: <p>Currently allowed: {cfg?.allowedOrigins.length ? cfg.allowedOrigins.map((o) => <code key={o} style={{ marginRight: 6 }}>{o}</code>) : "none"}</p>,
    },
  ];

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Setup</h1>
          <p>Everything that has to be in place for a tracked booking to reach TrackCrest.</p>
        </div>
        {env.demoEnabled() && <a className="btn" href="/demo?click_id=demo_click_123&sub1=test" target="_blank">Test the tracker ↗</a>}
      </div>

      {error && <ApiErrorBox message={error} />}
      {cfg && !cfg.requireWebhookConfirmation && (
        <div className="alert tone-warn">Tracking:RequireWebhookConfirmation is off: conversions can be sent from browser reports alone, which can be faked.</div>
      )}

      <div className="card card-pad">
        <ol className="checklist">
          {steps.map((s, i) => (
            <li key={i}>
              <span className={`check ${s.done === true ? "tone-ok" : s.done === false ? "tone-bad" : "tone-muted"}`}>
                {s.done === true ? "✓" : s.done === false ? "!" : i + 1}
              </span>
              <div>
                <strong>{s.title}</strong>
                <div style={{ marginTop: 6, color: "var(--text-2)" }}>{s.body}</div>
              </div>
            </li>
          ))}
        </ol>
      </div>
      <p className="hint" style={{ marginTop: 12 }}>Last conversion sent to TrackCrest: {fmtDateTime(cfg?.lastConversionSentAt)}</p>
    </>
  );
}
