import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import { env, getAppUrl } from "@/lib/env";
import type { Site } from "@/lib/types";
import { ApiErrorBox } from "@/components/Badge";
import { regenerateSiteSecret } from "../../../actions";
import ConfirmButton from "../../bookings/[id]/ConfirmButton";
import SiteForm from "../SiteForm";

export const metadata: Metadata = { title: "Site" };

const TRIGGERS = [
  "BOOKING_CREATED", "BOOKING_REQUESTED", "BOOKING_PAYMENT_INITIATED", "BOOKING_PAID",
  "BOOKING_RESCHEDULED", "BOOKING_CANCELLED", "BOOKING_REJECTED", "BOOKING_NO_SHOW_UPDATED",
];

export default async function SitePage({ params, searchParams }: PageProps<"/admin/sites/[id]">) {
  const { id } = await params;
  const { msg } = await searchParams;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  let site: Site;
  try {
    site = await api.site(id);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound();
    return <ApiErrorBox message={e instanceof ApiError ? e.message : "Unexpected error"} />;
  }

  const message = typeof msg === "string" ? msg : "";
  const app = await getAppUrl();
  const apiUrl = env.apiPublicUrl();
  const origin = site.allowedOrigins[0] ?? "https://client-site.com";
  const snippet = `<script src="${app}/tracker.js" data-booking-tracker
        data-site="${site.key}"
        data-api="${apiUrl}"
        data-confirm-url="${app}/booking-confirmed" async></script>`;
  const nextSnippet = `// app/layout.tsx
import Script from "next/script";
...
<Script src="${app}/tracker.js" strategy="afterInteractive"
        data-booking-tracker data-site="${site.key}" data-api="${apiUrl}"
        data-confirm-url="${app}/booking-confirmed" />`;

  return (
    <>
      <div className="page-head">
        <div>
          <Link className="crumb" href="/admin/sites">← Sites</Link>
          <h1>{site.name}</h1>
          <p className="mono">{site.key}</p>
        </div>
        <Link className="btn" href={`/admin?site=${site.id}`}>View its bookings</Link>
      </div>

      {message === "saved" && <div className="alert tone-ok">Site saved.</div>}
      {message === "secret" && <div className="alert tone-ok">New webhook secret created. Update it in Zylux Meet now: webhooks signed with the old one are rejected.</div>}
      {message.startsWith("error:") && <div className="alert tone-bad">{message.slice(6)}</div>}

      <div className="detail-grid">
        <div className="stack">
          <section className="card card-pad">
            <h2>Connect this site</h2>
            <ol className="checklist">
              <li>
                <span className="check tone-muted">1</span>
                <div>
                  <strong>TrackCrest offer destination URL</strong>
                  <p>Send affiliate clicks to the site with TrackCrest&apos;s click ID macro:</p>
                  <div className="code-block">{`${origin}/?click_id={click_id}`}</div>
                  <p className="hint">Or straight to the Zylux Meet booking link, e.g. <span className="mono">{`${env.zyluxBaseUrl()}/{username}/{link}?click_id={click_id}`}</span>. Zylux Meet keeps the click ID on the booking either way.</p>
                </div>
              </li>
              <li>
                <span className="check tone-muted">2</span>
                <div>
                  <strong>Tracker script on every page of the site</strong>
                  <div className="code-block">{snippet}</div>
                  <p style={{ marginTop: 8 }}>On a Next.js site:</p>
                  <div className="code-block">{nextSnippet}</div>
                  <p style={{ marginTop: 8 }}>And turn on attribution in the Zylux Meet embed script the site already has, so the click ID is saved on the booking itself:</p>
                  <div className="code-block">{`<script src="${env.zyluxBaseUrl()}/embed.js" data-tracking="on" async></script>`}</div>
                  <p className="hint">With it, Zylux Meet keeps the click ID on the booking even if the visitor books on another page or days later, and paid bookings are still tracked if the visitor closes the tab after paying. It stores the click ID in the visitor&apos;s browser, which may need cookie consent in the EU/UK.</p>
                </div>
              </li>
              <li>
                <span className="check tone-muted">3</span>
                <div>
                  <strong>Zylux Meet webhook</strong>
                  <p>In the site owner&apos;s Zylux Meet account → Settings → Developer → Webhooks, add:</p>
                  <div className="code-block">{`${apiUrl}/api/webhooks/zylux/${site.key}`}</div>
                  <p style={{ marginTop: 8 }}>Triggers: {TRIGGERS.map((t) => <code key={t} style={{ marginRight: 6 }}>{t}</code>)}</p>
                  <p style={{ marginTop: 8 }}>Secret:</p>
                  <details>
                    <summary>Show webhook secret</summary>
                    <div className="code-block">{site.webhookSecret || "(none set)"}</div>
                  </details>
                  <form action={regenerateSiteSecret} className="actions-row">
                    <input type="hidden" name="id" value={site.id} />
                    <ConfirmButton className="btn btn-sm" pendingLabel="Saving…" message="Create a new webhook secret? Webhooks signed with the current one will be rejected until you update Zylux Meet.">
                      New secret
                    </ConfirmButton>
                  </form>
                  <p className="hint"><code>BOOKING_PAYMENT_INITIATED</code> lets a paid meeting count as a lead as soon as it&apos;s booked; without it the lead is sent when the payment goes through.</p>
                </div>
              </li>
              <li>
                <span className="check tone-muted">4</span>
                <div>
                  <strong>Zylux Meet redirect on booking</strong>
                  <p>In each event → Advanced → Redirect on booking, enter this site&apos;s confirmation page:</p>
                  <div className="code-block">{`${app}/booking-confirmed`}</div>
                  <p className="hint">Zylux Meet sends people there once everything is done: right after booking for a free meeting, after the payment goes through for a paid one. It works from the embed and from the Zylux Meet page itself, and the page checks the booking&apos;s real status with Zylux Meet. The lead/sale doesn&apos;t depend on it: that comes from the webhook.</p>
                </div>
              </li>
            </ol>
          </section>
        </div>

        <div className="stack">
          <section className="card card-pad">
            <h2>Settings</h2>
            <SiteForm site={site} />
          </section>
        </div>
      </div>
    </>
  );
}
