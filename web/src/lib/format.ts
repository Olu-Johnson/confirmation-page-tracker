import type { AffiliatePaymentStatus, BookingStatus, ConversionStatus, ReconciliationStatus } from "./types";

const TZ = process.env.ADMIN_TIMEZONE || "UTC";

export function fmtDateTime(iso: string | null | undefined) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: TZ,
  }).format(d);
}

export const adminTimeZone = TZ;

export function fmtMoney(amount: number | null | undefined, currency: string | null | undefined) {
  if (amount == null) return "—";
  try {
    return new Intl.NumberFormat("en-US", { style: "currency", currency: currency || "USD" }).format(amount);
  } catch {
    return `${amount} ${currency ?? ""}`.trim();
  }
}

type Tone = "ok" | "warn" | "bad" | "info" | "muted";

export const bookingStatusMeta: Record<BookingStatus, { label: string; tone: Tone }> = {
  Pending: { label: "Pending approval", tone: "warn" },
  AwaitingPayment: { label: "Awaiting payment", tone: "warn" },
  Accepted: { label: "Booked", tone: "info" },
  Paid: { label: "Paid", tone: "ok" },
  Cancelled: { label: "Cancelled", tone: "bad" },
  Rejected: { label: "Rejected", tone: "bad" },
};

export const conversionStatusMeta: Record<ConversionStatus, { label: string; tone: Tone; help: string }> = {
  Waiting: { label: "Waiting", tone: "muted", help: "Not eligible yet (a lead needs the meeting booked, a sale needs payment; both need the Zylux webhook)." },
  Queued: { label: "Queued", tone: "info", help: "Eligible; the API is sending it to TrackCrest (with retries)." },
  Sent: { label: "Sent", tone: "ok", help: "TrackCrest accepted the postback." },
  Failed: { label: "Failed", tone: "bad", help: "TrackCrest kept failing; retry from the booking page." },
  NotEligible: { label: "Not eligible", tone: "muted", help: "Cancelled or rejected before it qualified, or a sale for a free meeting." },
};

export const reconciliationMeta: Record<ReconciliationStatus, { label: string; tone: Tone }> = {
  Unreviewed: { label: "Unreviewed", tone: "muted" },
  Confirmed: { label: "Confirmed", tone: "ok" },
  Disputed: { label: "Disputed", tone: "bad" },
};

export const affiliatePaymentMeta: Record<AffiliatePaymentStatus, { label: string; tone: Tone }> = {
  Pending: { label: "Pending", tone: "warn" },
  Paid: { label: "Paid", tone: "ok" },
  Rejected: { label: "Rejected", tone: "bad" },
};
