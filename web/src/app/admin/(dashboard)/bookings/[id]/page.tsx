import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { api, ApiError } from "@/lib/api";
import { conversionStatusMeta, fmtDateTime, fmtMoney } from "@/lib/format";
import type { TrackedBooking } from "@/lib/types";
import { env } from "@/lib/env";
import { ApiErrorBox, Badge, BookingBadge, ConversionBadge, ReconciliationBadge } from "@/components/Badge";
import { sendConversion, updateReconciliation } from "../../../actions";
import ConfirmButton from "./ConfirmButton";

export const metadata: Metadata = { title: "Booking" };

export default async function BookingDetailPage({ params, searchParams }: PageProps<"/admin/bookings/[id]">) {
  const { id } = await params;
  const { msg } = await searchParams;
  if (!/^[0-9a-f-]{36}$/i.test(id)) notFound();

  let b: TrackedBooking;
  try {
    b = await api.booking(id);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) notFound();
    return <ApiErrorBox message={e instanceof ApiError ? e.message : "Unexpected error"} />;
  }

  const message = typeof msg === "string" ? msg : "";
  const zylux = env.zyluxBaseUrl();
  const cancelledAfterSent = b.conversionStatus === "Sent" && (b.status === "Cancelled" || b.status === "Rejected");
  const subs = [b.sub1, b.sub2, b.sub3, b.sub4, b.sub5].map((s, i) => (s ? `sub${i + 1}=${s}` : null)).filter(Boolean);

  return (
    <>
      <div className="page-head">
        <div>
          <Link className="crumb" href="/admin">← All bookings</Link>
          <h1>{b.title ?? "Untitled booking"}</h1>
          <div className="badges" style={{ marginTop: 8 }}>
            <BookingBadge status={b.status} />
            <ConversionBadge status={b.conversionStatus} />
            <ReconciliationBadge status={b.reconciliationStatus} />
            {b.webhookConfirmed ? <Badge tone="ok">Verified by Zylux</Badge> : <Badge tone="warn">Unverified</Badge>}
          </div>
        </div>
        <a className="btn" href={`${zylux}/booking/${encodeURIComponent(b.bookingUid)}`} target="_blank" rel="noopener noreferrer">Open in Zylux Meet ↗</a>
      </div>

      {message === "saved" && <div className="alert tone-ok">Review saved.</div>}
      {message === "sent" && <div className="alert tone-ok">Conversion sent to TrackCrest.</div>}
      {message.startsWith("error:") && <div className="alert tone-bad">{message.slice(6)}</div>}
      {cancelledAfterSent && (
        <div className="alert tone-warn">
          This booking was {b.status.toLowerCase()} after the conversion was sent to TrackCrest. Reverse or reject the conversion in TrackCrest so the affiliate isn&apos;t paid for it.
        </div>
      )}
      {!b.webhookConfirmed && (
        <div className="alert tone-warn">
          No signed Zylux webhook has been received for this booking. It was reported only by the visitor&apos;s browser, so it isn&apos;t sent to TrackCrest yet. Check that the Zylux webhook is set up (see Setup).
        </div>
      )}

      <div className="detail-grid">
        <div className="stack">
          <section className="card card-pad">
            <h2>Booking</h2>
            <dl className="kv">
              <dt>Booking ID</dt><dd className="mono">{b.bookingUid}{b.originalBookingUid && <span className="cell-sub"> (rescheduled from {b.originalBookingUid})</span>}</dd>
              <dt>Meeting time</dt><dd>{fmtDateTime(b.startTime)}{b.endTime && ` – ${fmtDateTime(b.endTime)}`}</dd>
              <dt>Attendee</dt><dd>{b.attendeeName ?? "—"}{b.attendeeEmail && <> · <a href={`mailto:${b.attendeeEmail}`}>{b.attendeeEmail}</a></>}</dd>
              <dt>Attendee time zone</dt><dd>{b.attendeeTimeZone ?? "—"}</dd>
              <dt>Organizer</dt><dd>{b.organizerName ?? "—"}</dd>
              <dt>Location</dt><dd>{b.locationType ?? "—"}{b.locationUrl && <> · <span className="mono">{b.locationUrl}</span></>}</dd>
              <dt>No-show</dt><dd>{b.noShow == null ? "—" : b.noShow ? "Yes" : "No"}</dd>
            </dl>
          </section>

          <section className="card card-pad">
            <h2>Payment</h2>
            <dl className="kv">
              <dt>Required</dt><dd>{b.paymentRequired ? "Yes" : "No"}</dd>
              <dt>Amount</dt><dd>{fmtMoney(b.amount, b.currency)}</dd>
              <dt>Payment status</dt><dd>{b.paymentStatus ?? "—"}</dd>
              <dt>Provider</dt><dd>{b.paymentProvider ?? "—"}</dd>
              <dt>Paid at</dt><dd>{fmtDateTime(b.paidAt)}</dd>
            </dl>
          </section>

          <section className="card card-pad">
            <h2>Attribution (TrackCrest)</h2>
            <dl className="kv">
              <dt>Click ID</dt><dd className="mono">{b.clickId}</dd>
              <dt>Affiliate ID</dt><dd>{b.affiliateId ?? "—"}</dd>
              <dt>Sub IDs</dt><dd className="mono">{subs.length ? subs.join("  ") : "—"}</dd>
              <dt>Clicked at</dt><dd>{fmtDateTime(b.clickedAt)}</dd>
              <dt>Tracked at</dt><dd>{fmtDateTime(b.createdAt)}</dd>
              <dt>Site</dt><dd>{b.site ?? "—"}</dd>
              <dt>Landing URL</dt><dd className="mono">{b.landingUrl ?? "—"}</dd>
              <dt>Referrer</dt><dd className="mono">{b.referrer ?? "—"}</dd>
            </dl>
          </section>

          <section className="card card-pad">
            <h2>Zylux webhook events</h2>
            {b.events.length === 0 ? (
              <p className="cell-sub">None received yet.</p>
            ) : (
              <ul className="timeline">
                {b.events.map((e) => (
                  <li key={e.id}>
                    <div><strong className="mono">{e.trigger}</strong> <span className="when">· {fmtDateTime(e.receivedAt)}</span></div>
                    <details>
                      <summary>Payload</summary>
                      <pre className="json">{pretty(e.payload)}</pre>
                    </details>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>

        <div className="stack">
          <section className="card card-pad">
            <h2>TrackCrest conversion</h2>
            <dl className="kv" style={{ gridTemplateColumns: "110px minmax(0,1fr)" }}>
              <dt>Status</dt><dd><ConversionBadge status={b.conversionStatus} /></dd>
              <dt>Sent at</dt><dd>{fmtDateTime(b.conversionSentAt)}</dd>
              <dt>Attempts</dt><dd>{b.conversionAttemptCount}</dd>
              {b.nextConversionAttemptAt && b.conversionStatus === "Queued" && <><dt>Next try</dt><dd>{fmtDateTime(b.nextConversionAttemptAt)}</dd></>}
              {b.lastConversionError && <><dt>Last error</dt><dd style={{ color: "var(--bad)" }}>{b.lastConversionError}</dd></>}
            </dl>
            <p className="hint">{conversionStatusMeta[b.conversionStatus]?.help}</p>
            {b.conversionStatus !== "Sent" && (
              <form action={sendConversion} className="actions-row">
                <input type="hidden" name="id" value={b.id} />
                <ConfirmButton
                  className="btn btn-sm"
                  message={b.conversionStatus === "Waiting" || b.conversionStatus === "NotEligible"
                    ? "This booking hasn't qualified yet (not paid/confirmed, or cancelled). Send the conversion to TrackCrest anyway?"
                    : "Send this conversion to TrackCrest now?"}
                >
                  {b.conversionStatus === "Failed" || b.conversionStatus === "Queued" ? "Retry now" : "Send manually"}
                </ConfirmButton>
              </form>
            )}
            {b.attempts.length > 0 && (
              <ul className="timeline" style={{ marginTop: 12 }}>
                {b.attempts.map((a) => (
                  <li key={a.id}>
                    <div>
                      <Badge tone={a.success ? "ok" : "bad"}>{a.httpStatus ?? "error"}</Badge>{" "}
                      <span className="when">{fmtDateTime(a.attemptedAt)}{a.manual ? " · manual" : ""}</span>
                    </div>
                    {a.error && <div className="cell-sub">{a.error}</div>}
                    <details>
                      <summary>Request &amp; response</summary>
                      <pre className="json">{`GET ${a.url}\n\n${a.response ?? ""}`}</pre>
                    </details>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card card-pad">
            <h2>Reconciliation</h2>
            <form action={updateReconciliation}>
              <input type="hidden" name="id" value={b.id} />
              <label className="label" htmlFor="status">Review status</label>
              <select className="select" id="status" name="status" defaultValue={b.reconciliationStatus} style={{ width: "100%", marginBottom: 12 }}>
                <option value="Unreviewed">Unreviewed</option>
                <option value="Confirmed">Confirmed with owner</option>
                <option value="Disputed">Disputed</option>
              </select>
              <label className="label" htmlFor="notes">Notes</label>
              <textarea className="textarea" id="notes" name="notes" rows={4} maxLength={2000} defaultValue={b.reconciliationNotes ?? ""} placeholder="e.g. Owner confirmed payment received on 12 Nov" />
              {b.reconciledAt && <p className="hint">Last reviewed {fmtDateTime(b.reconciledAt)}</p>}
              <div className="actions-row"><button className="btn btn-primary btn-sm" type="submit">Save review</button></div>
            </form>
          </section>
        </div>
      </div>
    </>
  );
}

function pretty(json: string) {
  try { return JSON.stringify(JSON.parse(json), null, 2); } catch { return json; }
}
