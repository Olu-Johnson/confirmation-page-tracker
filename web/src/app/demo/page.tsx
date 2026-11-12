import type { Metadata } from "next";
import { notFound } from "next/navigation";
import Script from "next/script";
import { env, getAppUrl } from "@/lib/env";
import DemoControls from "./DemoControls";

export const metadata: Metadata = { title: "Tracker test page" };

/** Dev-only stand-in for the client's site: loads tracker.js exactly like dept.habyyer.com would. */
export default async function DemoPage() {
  if (!env.demoEnabled()) notFound();
  const app = await getAppUrl();
  const zyluxPath = env.zyluxBookingPath();

  return (
    <main className="main" style={{ maxWidth: 820 }}>
      <div className="page-head">
        <div>
          <h1>Tracker test page</h1>
          <p>Pretends to be the client&apos;s website. Open it with <code>?click_id=…</code> as if coming from a TrackCrest link.</p>
        </div>
      </div>

      <DemoControls />

      {zyluxPath && (
        <>
          <h2 className="section-title">Real Zylux scheduler ({zyluxPath})</h2>
          <div className="alert tone-warn">Bookings made here are real bookings on the Zylux calendar.</div>
          <div className="card" style={{ padding: 8 }}>
            <div data-scheduler-inline={zyluxPath} style={{ minHeight: 600 }} />
          </div>
          <Script src={`${env.zyluxBaseUrl()}/embed.js`} strategy="afterInteractive" />
        </>
      )}

      <Script
        src="/tracker.js"
        strategy="afterInteractive"
        data-booking-tracker=""
        data-api={env.apiPublicUrl()}
        data-confirm-url={`${app}/booking-confirmed`}
      />
    </main>
  );
}
