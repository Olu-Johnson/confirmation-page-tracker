"use client";

import { useEffect, useState } from "react";

type Attribution = { clickId: string; affiliateId?: string; sub1?: string; ts: number } | null;
declare global {
  interface Window { BookingTracker?: { getAttribution: () => Attribution; clear: () => void } }
}

export default function DemoControls() {
  const [attr, setAttr] = useState<Attribution>(null);
  const [ready, setReady] = useState(false);
  const [paid, setPaid] = useState(true);

  useEffect(() => {
    const t = setInterval(() => {
      if (window.BookingTracker) { setReady(true); setAttr(window.BookingTracker.getAttribution()); clearInterval(t); }
    }, 100);
    return () => clearInterval(t);
  }, []);

  function simulate() {
    const start = new Date(Date.now() + 3 * 86400000);
    start.setUTCMinutes(0, 0, 0);
    const end = new Date(start.getTime() + 60 * 60000);
    window.dispatchEvent(new CustomEvent("scheduler:booked", {
      detail: {
        uid: `bk_demo${Date.now()}`,
        title: "Consultation (demo booking)",
        startTime: start.toISOString(),
        endTime: end.toISOString(),
        status: paid ? "awaiting_payment" : "accepted",
      },
    }));
  }

  return (
    <div className="card card-pad">
      <h2>Attribution saved on this site</h2>
      {!ready ? <p className="cell-sub">Loading tracker.js…</p> : attr ? (
        <dl className="kv">
          <dt>Click ID</dt><dd className="mono">{attr.clickId}</dd>
          <dt>Affiliate</dt><dd>{attr.affiliateId ?? "—"}</dd>
          <dt>sub1</dt><dd>{attr.sub1 ?? "—"}</dd>
          <dt>Captured</dt><dd>{new Date(attr.ts).toLocaleString()}</dd>
        </dl>
      ) : (
        <p className="cell-sub">No click ID saved. Add <code>?click_id=test12345</code> to the URL. Without one, bookings redirect but aren&apos;t recorded.</p>
      )}
      <div className="actions-row">
        <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 14 }}>
          <input type="checkbox" checked={paid} onChange={(e) => setPaid(e.target.checked)} /> Paid booking (awaiting payment)
        </label>
      </div>
      <div className="actions-row">
        <button className="btn btn-primary" onClick={simulate} disabled={!ready}>Simulate &quot;scheduler:booked&quot;</button>
        <button className="btn" disabled={!ready} onClick={() => { window.BookingTracker?.clear(); setAttr(null); }}>Clear attribution</button>
      </div>
      <p className="hint">The simulated booking is recorded as &quot;Unverified&quot; because no signed Zylux webhook follows it. Use <code>scripts/simulate-booking.sh</code> to also send webhooks.</p>
    </div>
  );
}
