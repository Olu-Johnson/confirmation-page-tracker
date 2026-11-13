import type { Metadata } from "next";
import { env } from "@/lib/env";
import Confirmation from "./Confirmation";

export const metadata: Metadata = { title: "Booking confirmed" };

const UID_RX = /^[A-Za-z0-9_-]{3,100}$/;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v) ?? "";

const STATUSES = ["accepted", "pending", "awaiting_payment", "cancelled", "rejected"];

interface LiveBooking { status: string; title?: string; startTime?: string; endTime?: string }

/**
 * The booking from Zylux Meet's public booking API (the uid is the key): its real status, so an unpaid
 * booking is never shown as confirmed and a paid one never as unpaid, and its real title and times
 * (Zylux's redirect doesn't include the end time). Null when Zylux can't be reached in time; the page
 * then falls back to the URL.
 */
async function liveBooking(uid: string): Promise<LiveBooking | null> {
  try {
    const res = await fetch(`${env.zyluxBaseUrl()}/api/bookings/${encodeURIComponent(uid)}`, {
      cache: "no-store",
      signal: AbortSignal.timeout(3000),
    });
    if (!res.ok) return null;
    const b = (await res.json())?.booking;
    if (typeof b?.status !== "string" || !STATUSES.includes(b.status)) return null;
    const str = (v: unknown) => (typeof v === "string" ? v : undefined);
    return { status: b.status, title: str(b.title), startTime: str(b.startTime), endTime: str(b.endTime) };
  } catch {
    return null;
  }
}

/**
 * Reached via tracker.js after "scheduler:booked" / "scheduler:paid", or Zylux "Redirect on booking"
 * (?uid=…&startTime=…&title=…). Values in the URL are untrusted and only displayed as text.
 */
export default async function BookingConfirmedPage({ searchParams }: PageProps<"/booking-confirmed">) {
  const sp = await searchParams;
  const uid = UID_RX.test(one(sp.uid).trim()) ? one(sp.uid).trim() : "";
  const live = uid ? await liveBooking(uid) : null;
  const status = live?.status ?? one(sp.status).trim();

  return (
    <Confirmation
      uid={uid}
      title={(live?.title ?? one(sp.title)).slice(0, 200)}
      startTime={live?.startTime ?? one(sp.startTime)}
      endTime={live?.endTime ?? one(sp.endTime)}
      status={STATUSES.includes(status) ? status : "accepted"}
      brandName={env.brandName()}
      brandSiteUrl={env.brandSiteUrl()}
      zyluxBaseUrl={env.zyluxBaseUrl()}
    />
  );
}
